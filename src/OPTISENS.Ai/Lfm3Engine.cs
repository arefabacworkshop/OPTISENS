using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OPTISENS.Ai;

public sealed class LlmResult
{
    public required string Text { get; init; }
    public required int[] TokenIds { get; init; }
    public required bool EndedByEos { get; init; }
}

public sealed class ModelMissingException : Exception
{
    public string Remediation { get; }

    public ModelMissingException(string message, string remediation) : base(message)
    {
        Remediation = remediation;
    }
}

public sealed class Lfm3Engine : IDisposable
{
    /// <summary>Hard KV-cache token budget — 2048 tokens keeps cache under ~64 MB.</summary>
    public const int MaxContextTokens = 2048;

    private readonly InferenceSession _session;
    private readonly Lfm3Tokenizer _tokenizer;
    private readonly Dictionary<string, CacheBuf> _caches = new();

    /// <summary>
    /// Reusable managed copy of one past_* cache tensor. Feeding the previous run's
    /// output OrtValues directly back as inputs works on CPU but reads garbage on
    /// DirectML (outputs are EP-owned device buffers), so each step's present_* output
    /// is copied into this buffer and fed back as a fresh CPU-backed OrtValue.
    /// The buffer is grow-only and fully overwritten each step (present_* holds the
    /// accumulated cache), so generation stays allocation-light.
    /// </summary>
    private sealed class CacheBuf
    {
        public Array Data = Array.Empty<float>();
        public long[] Shape = [];

        public OrtValue ToOrtValue() => Data switch
        {
            float[] f => OrtValue.CreateTensorValueFromMemory(f, Shape),
            long[] l => OrtValue.CreateTensorValueFromMemory(l, Shape),
            Float16[] h => OrtValue.CreateTensorValueFromMemory(h, Shape),
            _ => throw new NotSupportedException($"Cache buffer type {Data.GetType()} not handled"),
        };

        public void CopyFrom(OrtValue src)
        {
            var shape = src.GetTensorTypeAndShape().Shape;
            long total = 1;
            foreach (var d in shape) total *= d;

            if (Data.Length < total)
            {
                long newLen = Math.Max(total, Data.Length * 2);
                Data = Data switch
                {
                    float[] => new float[newLen],
                    long[] => new long[newLen],
                    Float16[] => new Float16[newLen],
                    _ => throw new NotSupportedException($"Cache buffer type {Data.GetType()} not handled"),
                };
            }

            switch (Data)
            {
                case float[] f: src.GetTensorDataAsSpan<float>().CopyTo(f); break;
                case long[] l: src.GetTensorDataAsSpan<long>().CopyTo(l); break;
                case Float16[] h: src.GetTensorDataAsSpan<Float16>().CopyTo(h); break;
                default: throw new NotSupportedException($"Cache buffer type {Data.GetType()} not handled");
            }

            Shape = shape;
        }
    }
    private readonly List<int> _cachedTokenIds = new();
    private readonly int[] _eosIds;

    // Reusable single-token decode feed buffers (zero per-step allocation)
    private readonly long[] _singleFeedId = new long[1];
    private readonly long[] _singlePosId = new long[1];
    private readonly long[] _logitsKeep = [1L];

    // Serialize concurrent Generate() calls
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Arena-shrink RunOptions entry: ORT rejects (at Run entry, before executing) a
    // shrink list naming a device without an arena-based allocator, and which devices
    // have arenas depends on the EP and session flags (e.g. use_device_allocator_for_initializers
    // can leave the session with no arena allocators at all). Probe once per engine,
    // most-specific first; "" is terminal and always valid (means: no arena shrinkage).
    private static readonly string[] ShrinkCandidates = ["cpu:0;gpu:0", "cpu:0", ""];
    private int _shrinkCandidate;

    /// <summary>The arena-shrink entry in effect for this session ("" = none apply).</summary>
    public string ShrinkEntryInUse => ShrinkCandidates[_shrinkCandidate];

    public string ExecutionProviderName { get; }

    public static void ValidateModelDir(string modelDir)
    {
        string[] required =
        [
            Path.Combine("onnx", "model_q4.onnx"),
            Path.Combine("onnx", "model_q4.onnx_data"),
            "tokenizer.json",
            "tokenizer_config.json",
            "config.json",
            "generation_config.json",
            "chat_template.jinja"
        ];

        foreach (var rel in required)
        {
            string full = Path.Combine(modelDir, rel);
            if (!File.Exists(full))
                throw new ModelMissingException(
                    $"AI model file missing: {full}",
                    "Run scripts/fetch-model.ps1 (Windows) or scripts/fetch-model.sh (Linux/macOS), or pass --model-dir <path>, or launch with --skip-ai.");
        }

        var dataInfo = new FileInfo(Path.Combine(modelDir, "onnx", "model_q4.onnx_data"));
        if (dataInfo.Length < 1_500_000_000L)
            throw new ModelMissingException(
                $"AI model weights incomplete ({dataInfo.Length} bytes < 1.5 GB): {dataInfo.FullName}",
                "Re-run scripts/fetch-model.ps1 or scripts/fetch-model.sh to complete the download, or pass --skip-ai.");
    }

    public Lfm3Engine(string modelDir, bool preferGpu = true)
    {
        ValidateModelDir(modelDir);

        string modelPath = Path.Combine(modelDir, "onnx", "model_q4.onnx");
        (_session, ExecutionProviderName) = CreateSession(modelPath, preferGpu);
        _tokenizer = new Lfm3Tokenizer(modelDir);
        _eosIds = ReadEosIds(Path.Combine(modelDir, "generation_config.json"));
        ResetCaches();
    }

    private static (InferenceSession Session, string Provider) CreateSession(string modelPath, bool preferGpu)
    {
        if (preferGpu && OperatingSystem.IsWindows())
        {
            try
            {
                var dmlOpts = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    EnableMemoryPattern = false,
                    // Arena must be enabled for memory.enable_memory_arena_shrinkage to
                    // have an allocator to shrink (it is rejected when no arena exists).
                    EnableCpuMemArena = true,
                    InterOpNumThreads = 1,
                    IntraOpNumThreads = 2
                };
                // RAM_OPTIMIZATION_PLAN M2.2 session flags (initializers on the device
                // allocator + no prepacking keep CPU-side copies down; spin-wait off
                // avoids burning cores while the UI thread stays responsive).
                dmlOpts.AddSessionConfigEntry("session.use_device_allocator_for_initializers", "1");
                dmlOpts.AddSessionConfigEntry("session.disable_prepacking", "1");
                dmlOpts.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
                dmlOpts.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                dmlOpts.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
                dmlOpts.AddSessionConfigEntry("ep.dml.enable_graph_capture", "0");
                dmlOpts.AppendExecutionProvider_DML(0);
                var session = new InferenceSession(modelPath, dmlOpts);
                return (session, "DirectML (GPU)");
            }
            catch
            {
                // Fall through to bounded CPU Eco provider
            }
        }

        var cpuOpts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            EnableMemoryPattern = false,
            // Arena recycling + end-of-generate shrinkage (see RunPrefillWithShrink).
            EnableCpuMemArena = true,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
        };
        cpuOpts.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
        cpuOpts.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        cpuOpts.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
        cpuOpts.AppendExecutionProvider_CPU();
        return (new InferenceSession(modelPath, cpuOpts), $"CPU ({cpuOpts.IntraOpNumThreads}T Eco)");
    }

    public void ResetCaches()
    {
        _caches.Clear();
        _cachedTokenIds.Clear();

        foreach (var kv in _session.InputMetadata)
        {
            if (kv.Key is not ("input_ids" or "attention_mask" or "position_ids" or "logits_to_keep"))
            {
                var shape = ResolveShape(kv.Value.Dimensions, kv.Value.SymbolicDimensions);
                long total = 1;
                foreach (var d in shape) total *= d;
                var buf = new CacheBuf { Shape = shape };
                buf.Data = kv.Value.ElementDataType switch
                {
                    TensorElementType.Float => new float[total],
                    TensorElementType.Int64 => new long[total],
                    TensorElementType.Float16 => new Float16[total],
                    _ => throw new NotSupportedException($"Cache element type {kv.Value.ElementDataType} not handled"),
                };
                _caches[kv.Key] = buf;
            }
        }

        TrimProcessMemory();
    }

    /// <summary>Force GC and return freed OS pages to Windows Task Manager immediately.</summary>
    public static void TrimProcessMemory()
    {
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                // (SIZE_T)-1, (SIZE_T)-1 = trim as many pages as possible
                SetProcessWorkingSetSize(GetCurrentProcess(), UIntPtr.MaxValue, UIntPtr.MaxValue);
            }
            catch { /* best effort */ }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, UIntPtr dwMinimumWorkingSetSize, UIntPtr dwMaximumWorkingSetSize);

    private static long[] ResolveShape(int[] dims, string[]? symbolic)
    {
        var shape = new long[dims.Length];
        for (int i = 0; i < dims.Length; i++)
        {
            long d = dims[i];
            if (d <= 0)
            {
                d = symbolic is not null && i < symbolic.Length &&
                    symbolic[i].Contains("sequence", StringComparison.OrdinalIgnoreCase) ? 0L : 1L;
            }
            shape[i] = d;
        }
        return shape;
    }

    public LlmResult Generate(
        string prompt,
        int maxNewTokens,
        SamplingConfig cfg,
        Action<string>? onToken = null,
        CancellationToken ct = default)
    {
        _gate.Wait(ct);
        try
        {
            return GenerateCore(prompt, maxNewTokens, cfg, onToken, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LlmResult GenerateCore(
        string prompt,
        int maxNewTokens,
        SamplingConfig cfg,
        Action<string>? onToken,
        CancellationToken ct)
    {
        var promptIds = _tokenizer.Encode(prompt);

        // Strict context window guard: keep KV cache bounded
        if (promptIds.Length > MaxContextTokens)
        {
            // Truncate oldest middle tokens, keeping first 512 (system prompt) + last N
            int keepHead = 512;
            int keepTail = MaxContextTokens - keepHead;
            var trimmed = new int[keepHead + keepTail];
            Array.Copy(promptIds, 0, trimmed, 0, keepHead);
            Array.Copy(promptIds, promptIds.Length - keepTail, trimmed, keepHead, keepTail);
            promptIds = trimmed;
        }

        if (promptIds.Length == 0)
            return new LlmResult { Text = "", TokenIds = [], EndedByEos = true };

        // Check if current KV cache matches a prefix of promptIds (must leave at least 1 token to feed)
        int prefixLen = 0;
        if (_cachedTokenIds.Count > 0 && _cachedTokenIds.Count < promptIds.Length)
        {
            bool match = true;
            for (int i = 0; i < _cachedTokenIds.Count; i++)
            {
                if (_cachedTokenIds[i] != promptIds[i])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                prefixLen = _cachedTokenIds.Count;
        }

        if (prefixLen == 0)
            ResetCaches();

        var allIds = new List<int>(promptIds.Length + maxNewTokens);
        allIds.AddRange(promptIds);

        var generated = new List<int>(maxNewTokens);
        var streamDecoder = onToken is null ? null : _tokenizer.CreateStreamDecoder();

        bool hasPosIds = _session.InputMetadata.ContainsKey("position_ids");
        bool hasLogitsKeep = _session.InputMetadata.ContainsKey("logits_to_keep");

        // Prefill RunOptions shrinkage is applied by RunPrefillWithShrink below; decode
        // RunOptions carry no shrinkage during streaming — the entry is attached before
        // the final run so the arena is released at the end of Generate().
        using var decodeRunOptions = new RunOptions();

        // Reusable all-ones attention-mask buffer for the whole call (zero per-step alloc)
        var attnMask = new long[promptIds.Length + maxNewTokens];
        Array.Fill(attnMask, 1L);

        for (int step = 0; step < maxNewTokens && !ct.IsCancellationRequested; step++)
        {
            bool isPrefill = step == 0;
            bool isLastStep = step == maxNewTokens - 1;
            if (isLastStep && ShrinkCandidates[_shrinkCandidate].Length > 0)
                decodeRunOptions.AddRunConfigEntry("memory.enable_memory_arena_shrinkage", ShrinkCandidates[_shrinkCandidate]);

            long[] feedIds;
            long[]? posIds = null;

            if (isPrefill)
            {
                int deltaCount = promptIds.Length - prefixLen;
                feedIds = new long[deltaCount];
                if (hasPosIds) posIds = new long[deltaCount];
                for (int i = 0; i < deltaCount; i++)
                {
                    feedIds[i] = promptIds[prefixLen + i];
                    if (posIds is not null) posIds[i] = prefixLen + i;
                }
            }
            else
            {
                // Reuse single-token buffers (zero allocation per decode step)
                _singleFeedId[0] = generated[^1];
                feedIds = _singleFeedId;
                if (hasPosIds)
                {
                    _singlePosId[0] = allIds.Count - 1;
                    posIds = _singlePosId;
                }
            }

            long feedLen = feedIds.Length;
            long totalSeqLen = allIds.Count;
            long[]? logitsKeep = hasLogitsKeep ? _logitsKeep : null;

            using var inputIdsVal = OrtValue.CreateTensorValueFromMemory(feedIds, [1, feedLen]);
            // ORT requires buffer.Length >= shape elements (not equality), so the
            // max-size mask allocated once per Generate call serves every step.
            using var attnMaskVal = OrtValue.CreateTensorValueFromMemory(attnMask, [1, totalSeqLen]);
            using var posIdsVal = posIds is not null ? OrtValue.CreateTensorValueFromMemory(posIds, [1, feedLen]) : null;
            using var logitsKeepVal = logitsKeep is not null ? OrtValue.CreateTensorValueFromMemory(logitsKeep, [1]) : null;

            var feed = new Dictionary<string, OrtValue>(_caches.Count + 4)
            {
                ["input_ids"] = inputIdsVal,
                ["attention_mask"] = attnMaskVal,
            };
            if (posIdsVal is not null) feed["position_ids"] = posIdsVal;
            if (logitsKeepVal is not null) feed["logits_to_keep"] = logitsKeepVal;
            foreach (var (name, buf) in _caches)
                feed[name] = buf.ToOrtValue();

            IDisposableReadOnlyCollection<OrtValue> results;
            try
            {
                results = isPrefill
                    ? RunPrefillWithShrink(feed) // discovers + applies the arena-shrink entry
                    : _session.Run(decodeRunOptions, feed, _session.OutputNames);
            }
            finally
            {
                // The cache feed handles are done once Run returns (or throws); the
                // managed buffers behind them live in _caches and keep their values.
                foreach (var name in _caches.Keys)
                    feed[name].Dispose();
            }

            int next;
            using (results)
            {
                // Copy each present_* output into its reusable managed cache buffer
                // (see CacheBuf: re-feeding output OrtValues directly is DML-unsafe).
                for (int i = 1; i < results.Count; i++)
                {
                    string outName = _session.OutputNames[i];
                    string inName = outName.Replace("present_conv.", "past_conv.")
                                           .Replace("present.", "past_key_values.");
                    _caches[inName].CopyFrom(results[i]);
                }

                var logitsSpan = results[0].GetTensorDataAsSpan<float>();
                int vocabSize = (int)(results[0].GetTensorTypeAndShape().Shape[^1]);
                var lastLogits = logitsSpan.Length > vocabSize
                    ? logitsSpan.Slice(logitsSpan.Length - vocabSize, vocabSize)
                    : logitsSpan;

                next = Sampler.Sample(lastLogits, allIds, generated, cfg);
            }

            // Record the tokens that are now reflected in _caches
            if (isPrefill)
            {
                _cachedTokenIds.Clear();
                _cachedTokenIds.AddRange(promptIds);
            }
            else
            {
                _cachedTokenIds.Add( (int)feedIds[0] );
            }

            if (Array.IndexOf(_eosIds, next) >= 0)
            {
                // EOS: no further Run can carry a shrink entry, so Finish() trims the
                // process working set instead (the arena shrinks on the next prefill run).
                return Finish(generated, true);
            }

            generated.Add(next);
            allIds.Add(next);

            if (streamDecoder is not null && onToken is not null)
            {
                string delta = streamDecoder.Append(next);
                if (delta.Length > 0)
                    onToken(delta);
            }
        }

        return Finish(generated, false);
    }

    /// <summary>
    /// Prefill Run with the arena-shrink entry. If ORT rejects the entry (a listed
    /// device has no arena-based allocator in this session), advance to the next
    /// candidate and retry — validation throws at Run entry, before execution, so the
    /// retry is side-effect free. Settles once per engine.
    /// </summary>
    private IDisposableReadOnlyCollection<OrtValue> RunPrefillWithShrink(Dictionary<string, OrtValue> feed)
    {
        while (true)
        {
            using var ro = new RunOptions();
            string candidate = ShrinkCandidates[_shrinkCandidate];
            if (candidate.Length > 0)
                ro.AddRunConfigEntry("memory.enable_memory_arena_shrinkage", candidate);
            try
            {
                return _session.Run(ro, feed, _session.OutputNames);
            }
            catch (OnnxRuntimeException ex) when (
                ex.Message.Contains("memory arena shrink list", StringComparison.Ordinal))
            {
                _shrinkCandidate++; // candidates terminate at "" which cannot fail
            }
        }
    }

    private LlmResult Finish(List<int> generated, bool eos)
    {
        var text = _tokenizer.Decode(generated);
        // End of Generate: return freed pages to the OS immediately.
        TrimProcessMemory();
        return new() { Text = text, TokenIds = generated.ToArray(), EndedByEos = eos };
    }

    private static int[] ReadEosIds(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("eos_token_id", out var e))
            throw new InvalidDataException($"generation_config.json: eos_token_id missing ({path})");
        return e.ValueKind switch
        {
            JsonValueKind.Number => [e.GetInt32()],
            JsonValueKind.Array  => e.EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            _ => throw new InvalidDataException($"generation_config.json: eos_token_id has unexpected kind {e.ValueKind} ({path})")
        };
    }

    public void Dispose()
    {
        _caches.Clear(); // managed buffers only — nothing native to release
        _cachedTokenIds.Clear();
        _session.Dispose();
        _tokenizer.Dispose();
        _gate.Dispose();
        TrimProcessMemory();
    }
}
