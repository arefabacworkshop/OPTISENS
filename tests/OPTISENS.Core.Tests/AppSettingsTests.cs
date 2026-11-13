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
