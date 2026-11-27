using System.Diagnostics;
using System.Net.Http;
using OPTISENS.Core.Config;

namespace OPTISENS.App.Services;

public sealed record AssetDownloadProgress(
    string CurrentFile,
    int FileIndex,
    int TotalFiles,
    long FileBytesDownloaded,
    long? FileTotalBytes,
    double OverallFraction,
    double SpeedMBps,
    TimeSpan? Eta);

public sealed class AssetBootstrapper
{
    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async Task DownloadMissingAsync(
        string targetDirectory,
        IReadOnlyList<AssetFileSpec> missingFiles,
        IProgress<AssetDownloadProgress> progress,
        Action<string, bool>? onFileCompleted = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(targetDirectory);
        long totalExpectedBytes = missingFiles.Sum(f => Math.Max(1L, f.MinBytes));
        long completedBytes = 0;

        for (int i = 0; i < missingFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var spec = missingFiles[i];
            string destPath = Path.Combine(targetDirectory, spec.RelativePath);
            string? parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            string partPath = destPath + ".part";
            using var resp = await Http.GetAsync(spec.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long? contentLen = resp.Content.Headers.ContentLength;
            await using (var netStream = await resp.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                var sw = Stopwatch.StartNew();
                long lastReportMs = 0;

                while (true)
                {
                    int read = await netStream.ReadAsync(buffer, ct);
                    if (read == 0) break;
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    long elapsedMs = sw.ElapsedMilliseconds;
                    if (elapsedMs - lastReportMs >= 100 || (contentLen.HasValue && downloaded >= contentLen.Value))
                    {
                        lastReportMs = elapsedMs;
                        double sec = Math.Max(0.05, elapsedMs / 1000.0);
                        double speedBps = downloaded / sec;
                        double speedMBps = speedBps / (1024.0 * 1024.0);
                        long fileExpected = contentLen is > 0 ? contentLen.Value : Math.Max(1L, spec.MinBytes);
                        long bytesSoFar = completedBytes + downloaded;
                        double overallFrac = Math.Clamp(bytesSoFar / (double)Math.Max(1L, totalExpectedBytes), 0.0, 1.0);
                        TimeSpan? eta = speedBps > 1
                            ? TimeSpan.FromSeconds(Math.Max(0, totalExpectedBytes - bytesSoFar) / speedBps)
                            : null;

                        progress.Report(new AssetDownloadProgress(
                            spec.RelativePath,
                            i + 1,
                            missingFiles.Count,
                            downloaded,
                            contentLen,
                            overallFrac,
                            speedMBps,
                            eta));
                    }
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(partPath, destPath);

            var fi = new FileInfo(destPath);
            bool ok = fi.Length >= spec.MinBytes;
            onFileCompleted?.Invoke(spec.RelativePath, ok);
            if (!ok)
                throw new InvalidDataException($"Downloaded file '{spec.RelativePath}' failed size check ({fi.Length} < {spec.MinBytes} bytes).");
            completedBytes += fi.Length;
        }
    }
}
