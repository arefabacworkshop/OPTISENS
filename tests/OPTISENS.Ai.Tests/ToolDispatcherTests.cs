using OPTISENS.Ai;
using Xunit;

namespace OPTISENS.Ai.Tests;

public class ToolDispatcherTests
{
    [Fact]
    public void ToolDispatcher_Faster()
    {
        var d = new ToolDispatcher(30.0, 800, 10.0, 60.0);
        d.Execute(new ToolCallRecord("adjust_sensitivity", [("direction", "faster"), ("percentage", "10")]));
        Assert.Equal(27.0, d.CurrentCm360, 6);
    }

    [Fact]
    public void ToolDispatcher_Slower()
    {
        var d = new ToolDispatcher(30.0, 800, 10.0, 60.0);
        d.Execute(new ToolCallRecord("adjust_sensitivity", [("direction", "slower"), ("percentage", "10")]));
        Assert.Equal(33.0, d.CurrentCm360, 6);
    }

    [Fact]
    public void ToolDispatcher_Clamp()
    {
        var d = new ToolDispatcher(30.0, 800, 10.0, 60.0);
        d.Execute(new ToolCallRecord("set_specific_cm360", [("cm360", "999")]));
        Assert.Equal(60.0, d.CurrentCm360, 6);
    }

    [Fact]
    public void GetGameSensitivity_FindsApexAndFortnite()
    {
        var d = new ToolDispatcher(30.0, 800, 10.0, 60.0);
        string apexJson = d.Execute(new ToolCallRecord("get_game_sensitivity", [("game", "apex")]));
        Assert.Contains("Apex Legends", apexJson);
        Assert.Contains("1.7318", apexJson);

        string fnJson = d.Execute(new ToolCallRecord("get_game_sensitivity", [("game", "fortnite")]));
        Assert.Contains("Fortnite", fnJson);
    }
}
