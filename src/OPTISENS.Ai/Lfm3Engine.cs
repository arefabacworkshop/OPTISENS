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
    private readonly InferenceSession _session;
    private readonly Lfm3Tokenizer _tokenizer;
    private readonly Dictionary<string, OrtValue> _caches = new();
    private readonly int[] _eosIds;

    private readonly List<GCHandle> _pins = new();
    private readonly List<Array> _pinBuffers = new();

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

    public Lfm3Engine(string modelDir)
    {
        ValidateModelDir(modelDir);

        var so = new SessionOptions();
        so.AppendExecutionProvider_CPU();
        so.InterOpNumThreads = 1;
        so.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        _session = new InferenceSession(Path.Combine(modelDir, "onnx", "model_q4.onnx"), so);
        _tokenizer = new Lfm3Tokenizer(modelDir);
        _eosIds = ReadEosIds(Path.Combine(modelDir, "generation_config.json"));
        ResetCaches();
    }

    private void ResetCaches()
    {
        FreePins();
        foreach (var kv in _session.InputMetadata)
        {
            if (kv.Key is not ("input_ids" or "attention_mask" or "position_ids" or "logits_to_keep"))
            {
                if (_caches.TryGetValue(kv.Key, out var old)) old.Dispose();
                _caches[kv.Key] = CreateZero(kv.Value.ElementDataType, kv.Value.Dimensions, kv.Value.SymbolicDimensions);
            }
        }
    }

    private OrtValue CreateZero(TensorElementType type, int[] dims, string[]? symbolic)
    {
        var shape = new long[dims.Length];
        long total = 1;
        for (int i = 0; i < dims.Length; i++)
        {
            long d = dims[i];
            if (d <= 0)
            {
                d = symbolic is not null && i < symbolic.Length &&
                    symbolic[i].Contains("sequence", StringComparison.OrdinalIgnoreCase) ? 0L : 1L;
            }
            shape[i] = d;
            total *= d;
        }

        Array buffer = type switch
        {
            TensorElementType.Float   => new float[total],
            TensorElementType.Int64   => new long[total],
            TensorElementType.Float16 => new ushort[total],
            _ => throw new NotSupportedException($"cache element type {type} not handled")
        };
        return Adopt(buffer, shape, type);
    }

    private OrtValue Adopt(Array buffer, long[] shape, TensorElementType type)
    {
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        _pins.Add(pin);
        _pinBuffers.Add(buffer);
        return OrtValue.CreateTensorValueWithData(
            OrtMemoryInfo.DefaultInstance,
            type,
            shape,
            pin.AddrOfPinnedObject(),
            (long)(buffer.Length * ElementSize(type)));
    }

    private static int ElementSize(TensorElementType type) => type switch
    {
        TensorElementType.Float   => sizeof(float),
        TensorElementType.Int64   => sizeof(long),
        TensorElementType.Float16 => sizeof(ushort),
        _ => throw new NotSupportedException($"element type {type}")
    };

    private void FreePins()
    {
        foreach (var p in _pins)
            if (p.IsAllocated) p.Free();
        _pins.Clear();
        _pinBuffers.Clear();
    }

    private OrtValue CloneTensor(OrtValue src)
    {
        var info = src.GetTensorTypeAndShape();
        long[] shape = info.Shape;
        long count = info.ElementCount;
        return info.ElementDataType switch
        {
            TensorElementType.Float   => CopyTo(src.GetTensorDataAsSpan<float>(), new float[count], shape, TensorElementType.Float),
            TensorElementType.Int64   => CopyTo(src.GetTensorDataAsSpan<long>(), new long[count], shape, TensorElementType.Int64),
            TensorElementType.Float16 => CopyTo(src.GetTensorDataAsSpan<ushort>(), new ushort[count], shape, TensorElementType.Float16),
            _ => throw new NotSupportedException($"cache element type {info.ElementDataType} not handled")
        };
    }

    private OrtValue CopyTo<T>(ReadOnlySpan<T> src, T[] dst, long[] shape, TensorElementType type) where T : unmanaged
    {
        src.CopyTo(dst);
        return Adopt(dst, shape, type);
    }

    public LlmResult Generate(
        string prompt,
        int maxNewTokens,
        SamplingConfig cfg,
        Action<string>? onToken = null,
        CancellationToken ct = default)
    {
        ResetCaches();
        var ids = _tokenizer.Encode(prompt).ToList();
        int promptLen = ids.Count;
        long total = promptLen;
        var generated = new List<int>();
        bool hasPosIds = _session.InputMetadata.ContainsKey("position_ids");
        bool hasLogitsKeep = _session.InputMetadata.ContainsKey("logits_to_keep");

        for (int step = 0; step < maxNewTokens && !ct.IsCancellationRequested; step++)
        {
            long[] feedIds = step == 0
                ? ids.Select(i => (long)i).ToArray()
                : new[] { (long)generated[^1] };
            long feedLen = feedIds.Length;
            var attnMask = Enumerable.Repeat(1L, (int)total).ToArray();
            long[]? posIds = hasPosIds
                ? (step == 0 ? Enumerable.Range(0, promptLen).Select(i => (long)i).ToArray() : new[] { total - 1 })
                : null;
            long[]? logitsKeep = hasLogitsKeep ? new[] { 1L } : null;

            using var inputIdsVal = OrtValue.CreateTensorValueFromMemory(feedIds, [1, feedLen]);
            using var attnMaskVal = OrtValue.CreateTensorValueFromMemory(attnMask, [1, total]);
            using var posIdsVal = posIds is not null ? OrtValue.CreateTensorValueFromMemory(posIds, [1, feedLen]) : null;
            using var logitsKeepVal = logitsKeep is not null ? OrtValue.CreateTensorValueFromMemory(logitsKeep, [1]) : null;

            var feed = new Dictionary<string, OrtValue>
            {
                ["input_ids"] = inputIdsVal,
                ["attention_mask"] = attnMaskVal,
            };
            if (posIdsVal is not null) feed["position_ids"] = posIdsVal;
            if (logitsKeepVal is not null) feed["logits_to_keep"] = logitsKeepVal;
            foreach (var (name, val) in _caches) feed[name] = val;

            using var runOptions = new RunOptions();
            using var results = _session.Run(runOptions, feed, _session.OutputNames);

            var logits = results[0].GetTensorDataAsSpan<float>();
            int next = Sampler.Sample(logits, ids, generated, cfg);
            if (Array.IndexOf(_eosIds, next) >= 0) return Finish(generated, true);

            generated.Add(next);
            ids.Add(next);
            total++;
            var piece = _tokenizer.Decode(new[] { next });
            onToken?.Invoke(piece);

            for (int i = 1; i < results.Count; i++)
            {
                string outName = _session.OutputNames[i];
                string inName = outName.Replace("present_conv.", "past_conv.")
                                       .Replace("present.", "past_key_values.");
                if (_caches.TryGetValue(inName, out var prev)) prev.Dispose();
                _caches[inName] = CloneTensor(results[i]);
            }
        }
        return Finish(generated, false);
    }

    private LlmResult Finish(List<int> generated, bool eos) =>
        new() { Text = _tokenizer.Decode(generated), TokenIds = generated.ToArray(), EndedByEos = eos };

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
        foreach (var v in _caches.Values) v.Dispose();
        _caches.Clear();
        FreePins();
        _session.Dispose();
        _tokenizer.Dispose();
    }
}
