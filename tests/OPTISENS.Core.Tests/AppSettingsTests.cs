using OPTISENS.Core.Config;
using Xunit;

namespace OPTISENS.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void AppSettings_SaveAndLoad_RoundTrip()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "optisens-test-" + Guid.NewGuid().ToString("N"));
        string cfgPath = Path.Combine(tempDir, "settings.json");
        try
        {
            var expected = new AppSettings(@"D:\CustomModels\LFM3-1.7B-ONNX");
            AppSettingsStore.Save(expected, cfgPath);
            var actual = AppSettingsStore.Load(cfgPath);
            Assert.Equal(expected.AssetsDirectory, actual.AssetsDirectory);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AppSettings_AiProviderModes_RoundTrip_And_LegacyCompatibility()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "optisens-test-" + Guid.NewGuid().ToString("N"));
        string cfgPath = Path.Combine(tempDir, "settings.json");
        try
        {
            var expected = new AppSettings(
                AssetsDirectory: @"D:\OptiSensData",
                InferenceBackend: "CpuEco",
                HasCompletedSetup: true,
                AiMode: AiProviderMode.OpenAiCompatible,
                OpenAiBaseUrl: "http://localhost:11434/v1",
                OpenAiApiKey: "test-key",
                OpenAiModel: "llama3.2");
            AppSettingsStore.Save(expected, cfgPath);

            var actual = AppSettingsStore.Load(cfgPath);
            Assert.True(actual.HasCompletedSetup);
            Assert.Equal(AiProviderMode.OpenAiCompatible, actual.AiMode);
            Assert.Equal("http://localhost:11434/v1", actual.OpenAiBaseUrl);
            Assert.Equal("test-key", actual.OpenAiApiKey);
            Assert.Equal("llama3.2", actual.OpenAiModel);

            // Legacy JSON without new fields should deserialize with safe defaults
            File.WriteAllText(cfgPath, """{"AssetsDirectory":"C:\\Models"}""");
            var legacy = AppSettingsStore.Load(cfgPath);
            Assert.Equal(@"C:\Models", legacy.AssetsDirectory);
            Assert.False(legacy.HasCompletedSetup);
            Assert.Equal(AiProviderMode.LocalLfm3, legacy.AiMode);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AssetManifest_CheckAssets_DetectsMissingAndValid()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "optisens-assets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var missingRes = AssetManifest.CheckAssets(tempDir);
            Assert.False(missingRes.IsReady);
            Assert.Equal(AssetManifest.RequiredAssets.Length, missingRes.MissingOrInvalid.Count);

            Directory.CreateDirectory(Path.Combine(tempDir, "onnx"));
            foreach (var spec in AssetManifest.RequiredAssets)
            {
                string full = Path.Combine(tempDir, spec.RelativePath);
                if (spec.MinBytes <= 1_000_000)
                {
                    File.WriteAllBytes(full, new byte[spec.MinBytes]);
                }
                else
                {
                    using var fs = new FileStream(full, FileMode.Create, FileAccess.Write);
                    fs.SetLength(spec.MinBytes);
                }
            }

            var readyRes = AssetManifest.CheckAssets(tempDir);
            Assert.True(readyRes.IsReady);
            Assert.Empty(readyRes.MissingOrInvalid);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
