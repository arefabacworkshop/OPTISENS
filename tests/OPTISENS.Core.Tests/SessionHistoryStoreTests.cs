using OPTISENS.Core.Config;
using Xunit;

namespace OPTISENS.Core.Tests;

public class SessionHistoryStoreTests
{
    private static CalibrationHistoryEntry MakeEntry(string id, double cm = 32.0) =>
        new(id, "2026-11-27T00:00:00Z", 800, 45.0, "Palm", cm, cm - 2, cm + 2, 0.85, 0.03, false, "{}", [], []);

    [Fact]
    public void Append_Load_Delete_And_CapAt50Entries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "optisens-hist-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "history.json");
        try
        {
            for (int i = 0; i < 55; i++)
                SessionHistoryStore.Append(MakeEntry($"id-{i}", 25.0 + i * 0.1), path);

            var all = SessionHistoryStore.LoadAll(path);
            Assert.Equal(SessionHistoryStore.MaxEntries, all.Count);
            Assert.Equal("id-54", all[0].Id);

            SessionHistoryStore.Delete("id-54", path);
            var afterDel = SessionHistoryStore.LoadAll(path);
            Assert.Equal(SessionHistoryStore.MaxEntries - 1, afterDel.Count);
            Assert.DoesNotContain(afterDel, e => e.Id == "id-54");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SecretProtector_RoundTripsApiKey()
    {
        const string raw = "sk-test-secret-123456";
        string? protectedVal = SecretProtector.Protect(raw);
        Assert.NotNull(protectedVal);
        if (OperatingSystem.IsWindows())
            Assert.StartsWith("dpapi:", protectedVal);
        string? roundTrip = SecretProtector.Unprotect(protectedVal);
        Assert.Equal(raw, roundTrip);
    }
}
