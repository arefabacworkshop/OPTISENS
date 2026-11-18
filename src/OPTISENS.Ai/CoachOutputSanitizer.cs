using System.Text;
using System.Text.RegularExpressions;

namespace OPTISENS.Ai;

/// <summary>
/// Strips internal model tags (&lt;think&gt;, &lt;tool_call&gt;, stray &lt;function&gt;/&lt;parameter&gt;)
/// and raw markdown syntax so the UI only shows clean coach prose.
/// </summary>
public static partial class CoachOutputSanitizer
{
    [GeneratedRegex(@"<think>.*?(?:</think>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    [GeneratedRegex(@"<tool_call>.*?(?:</tool_call>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallBlockRegex();

    [GeneratedRegex(@"</?(?:tool_call|think|function(?:=[^>\n]*)?|parameter(?:=[^>\n]*)?|tool_response|tools)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex StrayTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessNewlinesRegex();

    public static string SanitizeFinal(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string s = ThinkBlockRegex().Replace(raw, "");
        s = ToolCallBlockRegex().Replace(s, "");
        s = StrayTagRegex().Replace(s, "");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        s = ExcessNewlinesRegex().Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>
    /// Stateful streaming filter that suppresses &lt;think&gt; and &lt;tool_call&gt; blocks
    /// (including partial opening tags split across token chunks).
    /// </summary>
    public sealed class StreamingFilter
    {
        private static readonly string[] OpenTags = ["<tool_call>", "<think>", "<function=", "<parameter="];
        private static readonly string[] CloseTags = ["</tool_call>", "</think>", "</function>", "</parameter>"];

        private readonly StringBuilder _buf = new();
        private string? _insideCloseTag;

        public string Push(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return "";
            _buf.Append(delta);
            return Drain(flushAll: false);
        }

        public string Flush() => Drain(flushAll: true);

        private string Drain(bool flushAll)
        {
            var outSb = new StringBuilder();
            while (_buf.Length > 0)
            {
                string current = _buf.ToString();
                if (_insideCloseTag is not null)
                {
                    int closeIdx = current.IndexOf(_insideCloseTag, StringComparison.OrdinalIgnoreCase);
                    if (closeIdx >= 0)
                    {
                        _buf.Remove(0, closeIdx + _insideCloseTag.Length);
                        _insideCloseTag = null;
                        continue;
                    }
                    // Keep only enough tail to match a split closing tag
                    int keep = Math.Max(0, _insideCloseTag.Length - 1);
                    if (!flushAll && _buf.Length > keep)
                        _buf.Remove(0, _buf.Length - keep);
                    else if (flushAll)
                        _buf.Clear();
                    break;
                }

                // Look for earliest opening tag
                int earliestIdx = -1;
                int earliestTagIdx = -1;
                for (int i = 0; i < OpenTags.Length; i++)
                {
                    int idx = current.IndexOf(OpenTags[i], StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (earliestIdx < 0 || idx < earliestIdx))
                    {
                        earliestIdx = idx;
                        earliestTagIdx = i;
                    }
                }

                if (earliestIdx >= 0)
                {
                    if (earliestIdx > 0)
                        outSb.Append(CleanInline(current[..earliestIdx]));
                    _buf.Remove(0, earliestIdx + OpenTags[earliestTagIdx].Length);
                    _insideCloseTag = CloseTags[earliestTagIdx];
                    continue;
                }

                // If not flushing, hold back a trailing '<...' in case it's a split tag
                if (!flushAll)
                {
                    int lastLt = current.LastIndexOf('<');
                    if (lastLt >= 0 && current.Length - lastLt <= 16)
                    {
                        if (lastLt > 0)
                        {
                            outSb.Append(CleanInline(current[..lastLt]));
                            _buf.Remove(0, lastLt);
                        }
                        break;
                    }
                }

                outSb.Append(CleanInline(current));
                _buf.Clear();
            }
            return outSb.ToString();
        }

        private static string CleanInline(string s)
            => StrayTagRegex().Replace(s, "").Replace("**", "").Replace("`", "");
    }
}
