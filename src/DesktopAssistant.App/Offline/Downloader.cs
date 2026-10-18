using System.IO.Compression;
using System.Net.Http.Headers;
using DesktopAssistant.Core;

namespace DesktopAssistant.Offline;

public sealed record DownloadProgress(string File, long Done, long Total, double BytesPerSecond);

/// <summary>Resumable downloads (a dropped connection continues where it stopped) with size verification.</summary>
public static class Downloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopAssistant/2 (+https://github.com/yeled-tov/desktop-personal-assistant)");
        return client;
    }

    public static async Task DownloadAsync(OfflineFile file, string destination, IProgress<DownloadProgress> progress, long alreadyDoneBefore, long grandTotal, CancellationToken cancellationToken)
    {
        if (File.Exists(destination) && (file.Size <= 0 || new FileInfo(destination).Length == file.Size)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var part = destination + ".part";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadOnceAsync(file, part, progress, alreadyDoneBefore, grandTotal, cancellationToken);
                break;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < 6)
            {
                AppLog.Warn($"Download {file.FileName} attempt {attempt} failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }
        var length = new FileInfo(part).Length;
        if (file.Size > 0 && length != file.Size)
        {
            File.Delete(part);
            throw new InvalidOperationException($"הקובץ {file.FileName} הורד חלקית ({OfflineCatalog.Size(length)} מתוך {OfflineCatalog.Size(file.Size)}). נסו שוב.");
        }
        File.Move(part, destination, true);
    }

    private static async Task DownloadOnceAsync(OfflineFile file, string part, IProgress<DownloadProgress> progress, long before, long grandTotal, CancellationToken cancellationToken)
    {
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (file.Size > 0 && existing >= file.Size) return;
        using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
        if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existing > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) existing = 0; // server ignored the range: start over
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(part, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        var buffer = new byte[1 << 20];
        var done = existing;
        var started = DateTime.UtcNow;
        var startBytes = existing;
        var lastReport = DateTime.MinValue;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            done += read;
            if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
            {
                lastReport = DateTime.UtcNow;
                var seconds = Math.Max(0.5, (DateTime.UtcNow - started).TotalSeconds);
                progress.Report(new DownloadProgress(file.FileName, before + done, grandTotal, (done - startBytes) / seconds));
            }
        }
    }

    /// <summary>Extracts a zip, flattening a single top-level folder (whisper.cpp ships its files in "Release/").</summary>
    public static void ExtractFlat(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var path = Path.GetFullPath(Path.Combine(destination, entry.Name));
            if (!path.StartsWith(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
            entry.ExtractToFile(path, overwrite: true);
        }
    }
}
