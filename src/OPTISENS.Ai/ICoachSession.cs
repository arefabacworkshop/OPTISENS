namespace OPTISENS.Ai;

/// <summary>Shared contract for local (LFM3) and remote (OpenAI-compatible) AI Coach sessions.</summary>
public interface ICoachSession : IDisposable
{
    event Action<ToolCallRecord>? ToolCallObserved;
    IReadOnlyList<ChatMessage> History { get; }
    Task<string> AskAsync(string userMessage, Action<string> onToken, CancellationToken ct);
}
