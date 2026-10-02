using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

using Microsoft.Extensions.Options;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record DriveFileInfo(string Name, long? Size, string? MimeType)
{
    /// <summary>Native Google Docs/Sheets/etc. have no byte content and can't be uploaded as-is.</summary>
    public bool IsGoogleNative => MimeType?.StartsWith("application/vnd.google-apps", StringComparison.Ordinal) == true;
}

public sealed class DriveDownloadService(
    GoogleCredentialFactory factory, IApiUsageService usage, IOptions<YouTubeUploadsOptions> options)
{
    public DriveService BuildService(string clientId, string clientSecret, string refreshToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = factory.CreateUserCredential(clientId, clientSecret, refreshToken),
            ApplicationName = "YouTubeUploads",
        });

    /// <summary>files.get metadata. <paramref name="oauthClientId"/> meters the call against that project's
    /// daily Drive usage (one query).</summary>
    public async Task<DriveFileInfo> GetInfoAsync(DriveService service, string fileId, Guid oauthClientId, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.Fields = "name,size,mimeType";
        req.SupportsAllDrives = true;
        var file = await req.ExecuteAsync(ct);
        await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveQueries, 1);
        return new DriveFileInfo(file.Name, file.Size, file.MimeType);
    }

    /// <summary>Downloads the file's content to <paramref name="path"/>, reporting bytes downloaded. A known
    /// <paramref name="size"/> uses parallel ranged requests (a single Drive stream can crawl at &lt;1 MiB/s).
    /// Honors cancellation. Meters every request and the bytes received against <paramref name="oauthClientId"/>.</summary>
    public async Task<RangedDownloadStats> DownloadAsync(
        DriveService service, string fileId, string path, long? size, Action<long> onBytes,
        Guid oauthClientId, CancellationToken ct)
    {
        if (size is not > 0)
            return await DownloadSingleStreamAsync(service, fileId, path, onBytes, oauthClientId, ct);

        var o = options.Value;
        var downloader = new RangedFileDownloader(
            service.HttpClient, o.DriveDownloadStreams, o.DriveDownloadPartBytes, o.DriveStallTimeout);
        var url = new Uri($"{service.BaseUri}files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true");
        try
        {
            return await downloader.DownloadAsync(url, path, size.Value, onBytes, ct);
        }
        finally
        {
            // Meter actual spend on every outcome (success, failure, or user cancel).
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveQueries, downloader.RequestCount);
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveBytes, downloader.BytesDone);
        }
    }

    private async Task<RangedDownloadStats> DownloadSingleStreamAsync(
        DriveService service, string fileId, string path, Action<long> onBytes, Guid oauthClientId, CancellationToken ct)
    {
        var req = service.Files.Get(fileId);
        req.SupportsAllDrives = true;
        long lastBytes = 0;
        req.MediaDownloader.ProgressChanged += p =>
        {
            if (p.Status is DownloadStatus.Downloading or DownloadStatus.Completed)
            {
                lastBytes = p.BytesDownloaded;
                onBytes(p.BytesDownloaded);
            }
        };

        try
        {
            await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var result = await req.DownloadAsync(destination, ct);
            if (result.Status == DownloadStatus.Failed)
                throw new InvalidOperationException("Drive download failed.", result.Exception);
            return new RangedDownloadStats(lastBytes, 1, 0, 1);
        }
        finally
        {
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveQueries, 1);
            await usage.IncrementAsync(oauthClientId.ToString(), ApiMetrics.DriveBytes, lastBytes);
        }
    }
}
