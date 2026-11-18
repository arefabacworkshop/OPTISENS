using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class CoachOutputSanitizerTests
{
    [Fact]
    public void SanitizeFinal_StripsToolCallThinkAndMarkdown()
    {
        const string raw =
            "<think>internal reasoning</think>\n" +
            "<answer>Your **recommended** sensitivity is `34.5` cm/360.</answer>\n" +
            "<tool_call>\n<function=adjust_sensitivity>\n<parameter=direction>\nslower\n</parameter>\n</function>\n</tool_call>\n" +
            "</parameter>";

        string clean = CoachOutputSanitizer.SanitizeFinal(raw);
        Assert.Equal("Your recommended sensitivity is 34.5 cm/360.", clean);
    }

    [Fact]
    public void SanitizeFinal_StripsUnclosedTrailingToolCall()
    {
        const string raw = "Raising to 56 cm/360 adds control.<tool_call>\n<function=set_specific_cm360>";
        string clean = CoachOutputSanitizer.SanitizeFinal(raw);
        Assert.Equal("Raising to 56 cm/360 adds control.", clean);
    }

    [Fact]
    public void StreamingFilter_SuppressesSplitTagsAcrossChunks()
    {
        var filter = new CoachOutputSanitizer.StreamingFilter();
        string[] chunks =
        [
            "Try ",
            "56 cm/360.<to",
            "ol_call>\n<function=set_specific_cm360>",
            "\n<parameter=cm360>\n56\n</parameter>\n</tool_call>",
            " Done!"
        ];

        var outText = string.Concat(chunks.Select(filter.Push)) + filter.Flush();
        Assert.Equal("Try 56 cm/360. Done!", outText);
    }

    [Fact]
    public void ToolDispatcher_NormalizesKnownAliasesOnly()
    {
        Assert.True(ToolDispatcher.IsKnownTool("adjust_sensitivity"));
        Assert.True(ToolDispatcher.IsKnownTool("adjust-sensitivity"));
        Assert.True(ToolDispatcher.IsKnownTool("set_specific_cm360"));
        Assert.False(ToolDispatcher.IsKnownTool("interpretation-guide-check"));
        Assert.False(ToolDispatcher.IsKnownTool("set_selected"));
    }
}
