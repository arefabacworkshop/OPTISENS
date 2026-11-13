using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class ToolCallParserTests
{
    [Fact]
    public void ToolCallParser_TwoCalls()
    {
        string text =
            "\u003ctool_call\u003e\n" +
            "\u003cfunction=adjust_sensitivity\u003e\n" +
            "\u003cparameter=direction\u003e\nfaster\n\u003c/parameter\u003e\n" +
            "\u003cparameter=percentage\u003e\n10\n\u003c/parameter\u003e\n" +
            "\u003c/function\u003e\n" +
            "\u003c/tool_call\u003e\n\n" +
            "\u003ctool_call\u003e\n" +
            "\u003cfunction=set_specific_cm360\u003e\n" +
            "\u003cparameter=cm360\u003e\n32.5\n\u003c/parameter\u003e\n" +
            "\u003c/function\u003e\n" +
            "\u003c/tool_call\u003e";

        var calls = ToolCallParser.Parse(text);
        Assert.Equal(2, calls.Length);
        Assert.Equal("adjust_sensitivity", calls[0].Name);
        Assert.Equal(("direction", "faster"), calls[0].Arguments[0]);
        Assert.Equal(("percentage", "10"), calls[0].Arguments[1]);
        Assert.Equal("set_specific_cm360", calls[1].Name);
        Assert.Equal(("cm360", "32.5"), calls[1].Arguments[0]);
    }

    [Fact]
    public void ToolCallParser_Malformed()
    {
        string truncated = "\u003ctool_call\u003e\n\u003cfunction=adjust_sensitivity\u003e\n\u003cparameter=direction\u003e\nfaster";
        var calls = ToolCallParser.Parse(truncated);
        Assert.Empty(calls);
    }
}
