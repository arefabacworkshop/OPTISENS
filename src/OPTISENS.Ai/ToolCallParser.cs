using System.Text.RegularExpressions;

namespace OPTISENS.Ai;

public static partial class ToolCallParser
{
    [GeneratedRegex(@"\u003ctool_call\u003e\s*(.*?)\s*\u003c/tool_call\u003e", RegexOptions.Singleline)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(@"\u003cfunction=([^\u003e\n]+)\u003e\s*(.*?)\s*\u003c/function\u003e", RegexOptions.Singleline)]
    private static partial Regex FunctionRegex();

    [GeneratedRegex(@"\u003cparameter=([^\u003e\n]+)\u003e\s*(.*?)\s*\u003c/parameter\u003e", RegexOptions.Singleline)]
    private static partial Regex ParameterRegex();

    public static ToolCallRecord[] Parse(string assistantText)
    {
        if (string.IsNullOrEmpty(assistantText)) return Array.Empty<ToolCallRecord>();

        var calls = new List<ToolCallRecord>();
        foreach (Match block in ToolCallBlockRegex().Matches(assistantText))
        {
            try
            {
                var fnMatch = FunctionRegex().Match(block.Groups[1].Value);
                if (!fnMatch.Success) continue;

                string fnName = fnMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(fnName)) continue;

                var args = new List<(string Key, string Value)>();
                foreach (Match p in ParameterRegex().Matches(fnMatch.Groups[2].Value))
                {
                    string key = p.Groups[1].Value.Trim();
                    string val = p.Groups[2].Value.Trim();
                    args.Add((key, val));
                }
                calls.Add(new ToolCallRecord(fnName, args.ToArray()));
            }
            catch
            {
                // Malformed region -> skip; never throw into UI thread
            }
        }
        return calls.ToArray();
    }
}
