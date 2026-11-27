using OPTISENS.App.ViewModels;
using OPTISENS.Core.Config;
using Xunit;

namespace OPTISENS.App.Tests;

public class SetupViewModelTests
{
    [Fact]
    public void DefaultState_IsAiDisabled()
    {
        var vm = new SetupViewModel(Path.GetTempPath(), new AppSettings());
        Assert.False(vm.IsAiEnabled);
        Assert.True(vm.IsAiDisabled);
    }

    [Fact]
    public void PreselectMode_EnablesAiAndSelectsProvider()
    {
        var vm = new SetupViewModel(
            Path.GetTempPath(),
            new AppSettings(AiMode: AiProviderMode.Disabled),
            isReconfigure: true,
            preselectMode: AiProviderMode.OpenAiCompatible);

        Assert.True(vm.IsAiEnabled);
        Assert.True(vm.IsOpenAiSelected);
        Assert.False(vm.IsLocalSelected);
    }

    [Fact]
    public void ApplyPreset_PopulatesKnownProviders()
    {
        var vm = new SetupViewModel(Path.GetTempPath(), new AppSettings());
        vm.ApplyPresetCommand.Execute("openrouter");
        Assert.Equal("https://openrouter.ai/api/v1", vm.OpenAiBaseUrl);
        Assert.Equal("openai/gpt-4o-mini", vm.OpenAiModel);

        vm.ApplyPresetCommand.Execute("ollama");
        Assert.Equal("http://localhost:11434/v1", vm.OpenAiBaseUrl);
        Assert.Equal("", vm.OpenAiApiKey);
    }
}
