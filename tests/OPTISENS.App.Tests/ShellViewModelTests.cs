using System.Threading.Channels;
using OPTISENS.App.ViewModels;
using OPTISENS.Core.Input;
using Xunit;

namespace OPTISENS.App.Tests;

public class ShellViewModelTests
{
    private sealed class StubRawInputService : IRawInputService
    {
        private readonly Channel<RawMotionSample> _ch = Channel.CreateUnbounded<RawMotionSample>();
        public MouseButtons CurrentButtons => MouseButtons.None;
        public bool IsReceiving => true;
        public ChannelReader<RawMotionSample> Samples => _ch.Reader;
        public long DroppedSamples => 0;
#pragma warning disable CS0067
        public event Action<int>? DeviceCountChanged;
#pragma warning restore CS0067
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    [Fact]
    public void Startup_OpensDirectlyOnHardwareProfileViewModel()
    {
        using var input = new StubRawInputService();
        using var shell = new ShellViewModel(input, Path.GetTempPath(), devMode: false, skipAi: false);

        Assert.IsType<HardwareProfileViewModel>(shell.CurrentPage);
    }

    [Fact]
    public void SkipToSampleResults_And_ReconfigureCancel_PreservesResults()
    {
        using var input = new StubRawInputService();
        using var shell = new ShellViewModel(input, Path.GetTempPath(), devMode: false, skipAi: true);

        shell.SkipToSampleResults();
        var results = Assert.IsType<ResultsViewModel>(shell.CurrentPage);

        results.Coach.ConfigureAiCommand.Execute(null);
        var setup = Assert.IsType<SetupViewModel>(shell.CurrentPage);

        setup.CancelReconfigureCommand.Execute(null);
        Assert.Same(results, shell.CurrentPage);
    }
}
