namespace OPTISENS.Ai;

public sealed record ChatMessage(string Role, string Content, ToolCallRecord[]? ToolCalls = null);
public sealed record ToolCallRecord(string Name, (string Key, string Value)[] Arguments);

public static class Lfm3ChatTemplate
{
    public const string Bos        = "\u003c|startoftext|\u003e";   // model bos_token
    public const string TurnStart  = "\u003c|im_start|\u003e";
    public const string TurnEnd    = "\u003c|im_end|\u003e\n";
    public const string ThinkOpen  = "\u003cthink\u003e";
    public const string ThinkClose = "\u003c/think\u003e";
    public const string EmptyThink = ThinkOpen + ThinkClose;        // thinking disabled (D17)

    // tool-call grammar (superset of qwen3_coder)
    public const string ToolCallOpen  = "\u003ctool_call\u003e\n";
    public const string ToolCallClose = "\u003c/tool_call\u003e";
    public const string FuncOpenFmt   = "\u003cfunction={0}\u003e\n";   // name
    public const string FuncClose     = "\u003c/function\u003e";
    public const string ParamOpenFmt  = "\u003cparameter={0}\u003e\n";  // arg name
    public const string ParamClose    = "\u003c/parameter\u003e";
    // tool-result wrapper (render_tool_response in chat_template.jinja)
    public const string ToolRespOpen  = "\u003ctool_response\u003e\n";
    public const string ToolRespClose = "\n\u003c/tool_response\u003e";
    // tools block inside the system message
    public const string ToolsOpen     = "\u003ctools\u003e\n";
    public const string ToolsClose    = "\n\u003c/tools\u003e";

    /// <summary>Render the full conversation + generation prompt. Deterministic.</summary>
    public static string Render(
        IReadOnlyList<ChatMessage> messages,
        string systemPrompt,
        IReadOnlyList<string> toolSchemaJsonLines)
    {
        var sb = new System.Text.StringBuilder(Bos);
        var body = systemPrompt;
        if (toolSchemaJsonLines.Count > 0)
            body += "\n\n" + ToolsOpen + string.Join("\n", toolSchemaJsonLines) + ToolsClose;
        sb.Append(TurnStart).Append("system\n").Append(body).Append(TurnEnd);

        foreach (var m in messages)
        {
            sb.Append(TurnStart).Append(m.Role).Append('\n');
            switch (m.Role)
            {
                case "assistant":
                    sb.Append(EmptyThink); // thinking disabled
                    sb.Append(m.Content);
                    if (m.ToolCalls is { Length: > 0 } calls)
                    {
                        for (int i = 0; i < calls.Length; i++)
                        {
                            if (i > 0) sb.Append("\n\n");
                            sb.Append(ToolCallOpen);
                            sb.AppendFormat(FuncOpenFmt, calls[i].Name);
                            foreach (var (k, v) in calls[i].Arguments)
                            {
                                sb.AppendFormat(ParamOpenFmt, k);
                                sb.Append(v).Append('\n');
                                sb.Append(ParamClose).Append('\n');
                            }
                            sb.Append(FuncClose).Append('\n');
                            sb.Append(ToolCallClose);
                        }
                    }
                    break;
                case "tool":
                    sb.Append(ToolRespOpen).Append(m.Content).Append(ToolRespClose);
                    break;
                default:
                    sb.Append(m.Content);
                    break;
            }
            sb.Append(TurnEnd);
        }

        sb.Append(TurnStart).Append("assistant\n").Append(EmptyThink); // generation prompt
        return sb.ToString();
    }
}
