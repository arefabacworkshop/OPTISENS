using System.Reflection;
using OPTISENS.Core.Config;

namespace OPTISENS.Ai;

public sealed class CoachSession
{
    /// <summary>Cap: initial telemetry exchange + the most recent 6 exchanges.</summary>
    private const int MaxHistoryMessages = 14;

    private readonly Lfm3Engine _engine;
    private readonly ToolDispatcher _dispatcher;
    private readonly List<ChatMessage> _history = new();
    private readonly string _systemPrompt;
    private readonly string[] _toolSchemas;

    public event Action<ToolCallRecord>? ToolCallObserved;

    /// <summary>Conversation history preserved across engine reloads.</summary>
    public IReadOnlyList<ChatMessage> History => _history;

    public CoachSession(
        Lfm3Engine engine,
        ToolDispatcher dispatcher,
        string? systemPrompt = null,
        string[]? toolSchemas = null,
        IEnumerable<ChatMessage>? history = null)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _systemPrompt = systemPrompt ?? LoadEmbeddedSystemPrompt();
        _toolSchemas = toolSchemas ?? ToolDispatcher.DefaultToolSchemas;
        if (history is not null)
            _history.AddRange(history);
    }

    public static string LoadEmbeddedSystemPrompt()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("OPTISENS.Ai.SystemPrompt.txt")
            ?? throw new InvalidOperationException("Embedded resource OPTISENS.Ai.SystemPrompt.txt not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Streaming user turn. Raises onToken(text) on the AI thread; result arrives via Task.</summary>
    public async Task<string> AskAsync(string userMessage, Action<string> onToken, CancellationToken ct)
    {
        AddMessage(new ChatMessage("user", userMessage));
        for (int trip = 0; trip < Constants.MaxToolRoundTrips; trip++)
        {
            var prompt = Lfm3ChatTemplate.Render(_history, _systemPrompt, _toolSchemas);
            var res = await Task.Run(
                () => _engine.Generate(prompt, Constants.AiMaxNewTokens, SamplingConfig.Default, onToken, ct),
                ct);
            var calls = ToolCallParser.Parse(res.Text);
            if (calls.Length == 0)
            {
                AddMessage(new ChatMessage("assistant", res.Text));
                return res.Text;
            }
            AddMessage(new ChatMessage("assistant", res.Text, calls));
            foreach (var call in calls)
            {
                ToolCallObserved?.Invoke(call);
                AddMessage(new ChatMessage("tool", _dispatcher.Execute(call)));
            }
        }
        var final = await Task.Run(
            () => _engine.Generate(
                Lfm3ChatTemplate.Render(_history, _systemPrompt, _toolSchemas),
                Constants.AiMaxNewTokens,
                SamplingConfig.Default,
                onToken,
                ct),
            ct);
        AddMessage(new ChatMessage("assistant", final.Text));
        return final.Text;
    }

    private void AddMessage(ChatMessage message)
    {
        _history.Add(message);
        TrimHistory();
    }

    /// <summary>
    /// Keeps the initial telemetry exchange plus whole recent follow-up turns within the
    /// cap, dropping old middle turns (never mid tool round-trip, never the latest turn).
    /// </summary>
    private void TrimHistory()
    {
        while (_history.Count > MaxHistoryMessages)
        {
            // Start of the oldest follow-up turn (first "user" after the initial exchange)
            int cut = 1;
            while (cut < _history.Count && _history[cut].Role != "user")
                cut++;
            if (cut >= _history.Count)
            {
                // Pathological: a single turn exceeds the cap — keep head + tail
                _history.RemoveRange(2, _history.Count - MaxHistoryMessages);
                break;
            }

            // End of that turn = start of the next "user"
            int end = cut + 1;
            while (end < _history.Count && _history[end].Role != "user")
                end++;
            if (end >= _history.Count)
            {
                // The latest turn alone exceeds the cap — drop its oldest messages
                _history.RemoveRange(cut, _history.Count - MaxHistoryMessages);
                break;
            }
            _history.RemoveRange(cut, end - cut);
        }
    }
}
