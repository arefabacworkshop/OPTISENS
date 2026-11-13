using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class ChatTemplateTests
{
    [Fact]
    public void ChatTemplate_Golden()
    {
        var messages = new List<ChatMessage>
        {
            new("user", "Here is my telemetry."),
            new("assistant", "", [new ToolCallRecord("adjust_sensitivity", [("direction", "slower"), ("percentage", "5")])]),
            new("tool", """{"cm360":31.5,"note":"Adjusted 5.0% slower."}"""),
            new("assistant", "Adjusted to 31.5 cm/360.")
        };

        string rendered = Lfm3ChatTemplate.Render(
            messages,
            "You are OPTISENS Coach.",
            ToolDispatcher.DefaultToolSchemas);

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "expected-prompt.txt");
        string expected = File.ReadAllText(fixturePath).Replace("\r\n", "\n");
        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void ChatTemplate_NoTools()
    {
        var messages = new List<ChatMessage> { new("user", "Hello") };
        string rendered = Lfm3ChatTemplate.Render(messages, "System.", Array.Empty<string>());
        Assert.DoesNotContain("<tools>", rendered);
        Assert.DoesNotContain("</tools>", rendered);
    }
}
