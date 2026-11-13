using System.Reflection;
using OPTISENS.Core.Config;

namespace OPTISENS.Ai;

public sealed class CoachSession
{
    private readonly Lfm3Engine _engine;
    private readonly ToolDispatcher _dispatcher;
    private readonly List<ChatMessage> _history = new();
    private readonly string _systemPrompt;
    private readonly string[] _toolSchemas;

    public event Action<ToolCallRecord>? ToolCallObserved;

    public CoachSession(
        Lfm3Engine engine,
        ToolDispatcher dispatcher,
        string? systemPrompt = null,
        string[]? toolSchemas = null)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _systemPrompt = systemPrompt ?? LoadEmbeddedSystemPrompt();
        _toolSchemas = toolSchemas ?? ToolDispatcher.DefaultToolSchemas;
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
        _history.Add(new ChatMessage("user", userMessage));
        for (int trip = 0; trip < Constants.MaxToolRoundTrips; trip++)
        {
            var prompt = Lfm3ChatTemplate.Render(_history, _systemPrompt, _toolSchemas);
            var res = await Task.Run(
                () => _engine.Generate(prompt, Constants.AiMaxNewTokens, SamplingConfig.Default, onToken, ct),
                ct);
            var calls = ToolCallParser.Parse(res.Text);
            if (calls.Length == 0)
            {
                _history.Add(new ChatMessage("assistant", res.Text));
                return res.Text;
            }
            _history.Add(new ChatMessage("assistant", res.Text, calls));
            foreach (var call in calls)
            {
                ToolCallObserved?.Invoke(call);
                _history.Add(new ChatMessage("tool", _dispatcher.Execute(call)));
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
        _history.Add(new ChatMessage("assistant", final.Text));
        return final.Text;
    }
}
