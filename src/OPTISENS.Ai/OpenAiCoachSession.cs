using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OPTISENS.Core.Config;

namespace OPTISENS.Ai;

/// <summary>
/// Streaming OpenAI-compatible chat completions client supporting tool calls
/// (OpenAI, OpenRouter, Ollama, LM Studio, vLLM, etc.).
/// </summary>
public sealed class OpenAiCoachSession : ICoachSession
{
    private const int MaxHistoryMessages = 14;

    private readonly string _endpointUrl;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ToolDispatcher _dispatcher;
    private readonly string _systemPrompt;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly List<ChatMessage> _history = new();

    public event Action<ToolCallRecord>? ToolCallObserved;
    public IReadOnlyList<ChatMessage> History => _history;

    public OpenAiCoachSession(
        string baseUrl,
        string? apiKey,
        string model,
        ToolDispatcher dispatcher,
        string? systemPrompt = null,
        IEnumerable<ChatMessage>? history = null,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("OpenAI-compatible Base URL is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name is required.", nameof(model));

        _endpointUrl = BuildChatCompletionsUrl(baseUrl);
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _model = model.Trim();
        _dispatcher = dispatcher;
        _systemPrompt = systemPrompt ?? CoachSession.LoadEmbeddedSystemPrompt();
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        if (history is not null)
            _history.AddRange(history);
    }

    public static string BuildChatCompletionsUrl(string baseUrl)
    {
        string trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return $"{trimmed}/chat/completions";
    }

    public static async Task<(bool Ok, string Message, long LatencyMs)> TestConnectionAsync(
        string baseUrl,
        string? apiKey,
        string model,
        CancellationToken ct = default,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return (false, "Enter a Base URL first.", 0);
        if (string.IsNullOrWhiteSpace(model))
            return (false, "Enter a Model name first.", 0);

        string url = BuildChatCompletionsUrl(baseUrl);
        using var owned = httpClient is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) } : null;
        var client = httpClient ?? owned!;

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(apiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

            string payload = BuildRequestJson(
                model.Trim(),
                "Reply with OK.",
                history: [],
                toolExchangeTurns: [],
                includeTools: false,
                stream: false,
                maxTokens: 4);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                string snippet = body.Length > 160 ? body[..160] + "…" : body;
                return (false, $"HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase}): {snippet}", sw.ElapsedMilliseconds);
            }

            return (true, $"Connected · {sw.ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return (false, "Connection test cancelled or timed out.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<string> AskAsync(string userMessage, Action<string> onToken, CancellationToken ct)
    {
        AddMessage(new ChatMessage("user", userMessage));
        var toolTurns = new List<ToolTurn>();

        for (int trip = 0; trip < Constants.MaxToolRoundTrips; trip++)
        {
            var result = await SendCompletionAsync(toolTurns, includeTools: true, onToken, ct);
            var validCalls = result.ToolCalls
                .Where(c => ToolDispatcher.IsKnownTool(c.Record.Name))
                .ToArray();

            if (validCalls.Length == 0)
            {
                AddMessage(new ChatMessage("assistant", result.Content));
                return result.Content;
            }

            var assistantCalls = new List<AssistantToolCall>(validCalls.Length);
            var toolResponses = new List<ToolResponse>(validCalls.Length);

            foreach (var call in validCalls)
            {
                ToolCallObserved?.Invoke(call.Record);
                string output = _dispatcher.Execute(call.Record);
                assistantCalls.Add(call);
                toolResponses.Add(new ToolResponse(call.Id, output));
            }

            toolTurns.Add(new ToolTurn(result.Content, assistantCalls, toolResponses));
        }

        var final = await SendCompletionAsync(toolTurns, includeTools: false, onToken, ct);
        AddMessage(new ChatMessage("assistant", final.Content));
        return final.Content;
    }

    private async Task<CompletionResult> SendCompletionAsync(
        IReadOnlyList<ToolTurn> toolTurns,
        bool includeTools,
        Action<string> onToken,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
        if (_apiKey is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        string json = BuildRequestJson(
            _model,
            _systemPrompt,
            _history,
            toolTurns,
            includeTools,
            stream: true,
            maxTokens: Constants.AiMaxNewTokens);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"OpenAI-compatible endpoint returned HTTP {(int)resp.StatusCode}: {err}");
        }

        string? mediaType = resp.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            string fullBody = await resp.Content.ReadAsStringAsync(ct);
            return ParseNonStreamingResponse(fullBody, onToken);
        }

        var contentBuilder = new StringBuilder();
        var toolAccumulators = new Dictionary<int, ToolCallAccumulator>();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (line.Length == 0 || line.StartsWith(':')) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            string data = line[5..].Trim();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;

            using var doc = JsonDocument.Parse(data);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                continue;

            if (delta.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                string? chunk = contentProp.GetString();
                if (!string.IsNullOrEmpty(chunk))
                {
                    contentBuilder.Append(chunk);
                    onToken(chunk);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var tcArray) && tcArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcArray.EnumerateArray())
                {
                    int idx = tc.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out int parsedIdx)
                        ? parsedIdx
                        : 0;
                    if (!toolAccumulators.TryGetValue(idx, out var acc))
                    {
                        acc = new ToolCallAccumulator($"call_{idx}");
                        toolAccumulators[idx] = acc;
                    }

                    if (tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        string? id = idProp.GetString();
                        if (!string.IsNullOrEmpty(id)) acc.Id = id;
                    }

                    if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                            acc.Name.Append(nameProp.GetString());
                        if (fn.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.String)
                            acc.Arguments.Append(argsProp.GetString());
                    }
                }
            }
        }

        var calls = toolAccumulators
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value.Build())
            .Where(c => !string.IsNullOrWhiteSpace(c.Record.Name))
            .ToArray();

        return new CompletionResult(contentBuilder.ToString(), calls);
    }

    private static CompletionResult ParseNonStreamingResponse(string json, Action<string> onToken)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return new CompletionResult("", []);
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            return new CompletionResult("", []);

        string content = msg.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String
            ? cProp.GetString() ?? ""
            : "";
        if (content.Length > 0)
            onToken(content);

        var calls = new List<AssistantToolCall>();
        if (msg.TryGetProperty("tool_calls", out var tcArray) && tcArray.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var tc in tcArray.EnumerateArray())
            {
                string id = tc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString() ?? $"call_{idx}"
                    : $"call_{idx}";
                if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                {
                    string name = fn.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                    string args = fn.TryGetProperty("arguments", out var aProp) ? aProp.GetString() ?? "{}" : "{}";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        calls.Add(new AssistantToolCall(id, args, new ToolCallRecord(name, ParseArguments(args))));
                    }
                }
                idx++;
            }
        }

        return new CompletionResult(content, calls.ToArray());
    }

    private static string BuildRequestJson(
        string model,
        string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ToolTurn> toolExchangeTurns,
        bool includeTools,
        bool stream,
        int maxTokens)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);

        writer.WriteStartObject();
        writer.WriteString("model", model);
        writer.WriteBoolean("stream", stream);
        writer.WriteNumber("max_tokens", maxTokens);

        writer.WriteStartArray("messages");
        writer.WriteStartObject();
        writer.WriteString("role", "system");
        writer.WriteString("content", systemPrompt);
        writer.WriteEndObject();

        foreach (var msg in history)
        {
            if (msg.Role == "tool") continue;
            writer.WriteStartObject();
            writer.WriteString("role", msg.Role);
            writer.WriteString("content", msg.Content);
            writer.WriteEndObject();
        }

        foreach (var turn in toolExchangeTurns)
        {
            writer.WriteStartObject();
            writer.WriteString("role", "assistant");
            writer.WriteString("content", turn.AssistantContent);
            writer.WriteStartArray("tool_calls");
            foreach (var call in turn.Calls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", call.Record.Name);
                writer.WriteString("arguments", call.RawArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();

            foreach (var tr in turn.Responses)
            {
                writer.WriteStartObject();
                writer.WriteString("role", "tool");
                writer.WriteString("tool_call_id", tr.ToolCallId);
                writer.WriteString("content", tr.Content);
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();

        if (includeTools)
        {
            writer.WriteStartArray("tools");
            foreach (var schemaJson in ToolDispatcher.DefaultToolSchemas)
            {
                using var schemaDoc = JsonDocument.Parse(schemaJson);
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WritePropertyName("function");
                schemaDoc.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static (string Key, string Value)[] ParseArguments(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            var list = new List<(string, string)>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string val = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.GetRawText(),
                    _ => prop.Value.GetRawText()
                };
                list.Add((prop.Name, val));
            }
            return list.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private void AddMessage(ChatMessage message)
    {
        _history.Add(message);
        while (_history.Count > MaxHistoryMessages)
        {
            _history.RemoveAt(1);
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }

    private sealed class ToolCallAccumulator(string initialId)
    {
        public string Id { get; set; } = initialId;
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();

        public AssistantToolCall Build()
        {
            string rawArgs = Arguments.ToString();
            if (string.IsNullOrWhiteSpace(rawArgs)) rawArgs = "{}";
            return new AssistantToolCall(Id, rawArgs, new ToolCallRecord(Name.ToString(), ParseArguments(rawArgs)));
        }
    }

    private sealed record AssistantToolCall(string Id, string RawArgumentsJson, ToolCallRecord Record);
    private sealed record ToolResponse(string ToolCallId, string Content);
    private sealed record ToolTurn(string AssistantContent, IReadOnlyList<AssistantToolCall> Calls, IReadOnlyList<ToolResponse> Responses);
    private sealed record CompletionResult(string Content, IReadOnlyList<AssistantToolCall> ToolCalls);
}
