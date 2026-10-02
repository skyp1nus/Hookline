using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;

using Microsoft.Win32.SafeHandles;

namespace Hookline.Modules.YouTubeUploads.Infrastructure;

public sealed record RangedDownloadStats(long Bytes, int Requests, int Retries, int Streams);

/// <summary>Downloads a sized resource with parallel HTTP Range requests straight into a file. A stream that
/// stops delivering bytes for <c>stallTimeout</c> is dropped and resumed from its last written offset.</summary>
internal sealed class RangedFileDownloader(
    HttpClient http,
    int streams,
    int partBytes,
    TimeSpan stallTimeout,
    int maxAttempts = 5,
    Func<int, TimeSpan>? backoff = null)
{
    private const int BufferBytes = 1024 * 1024;
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(500);

    private readonly Func<int, TimeSpan> _backoff = backoff ?? (attempt => TimeSpan.FromSeconds(Math.Min(16, 1 << attempt)));
    private Run? _run;

    /// <summary>Bytes written so far — readable after a failure too, for metering.</summary>
    public long BytesDone => _run?.Done ?? 0;

    /// <summary>HTTP requests issued so far, retries included.</summary>
    public int RequestCount => _run?.Requests ?? 0;

    public async Task<RangedDownloadStats> DownloadAsync(
        Uri url, string path, long size, Action<long> onBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var partCount = (int)((size + partBytes - 1) / partBytes);
        var workers = Math.Clamp(streams, 1, partCount);
        var run = _run = new Run(onBytes);

        using (var file = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous, size))
        using (var abort = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var nextPart = -1;
            async Task WorkerAsync()
            {
                try
                {
                    int part;
                    while ((part = Interlocked.Increment(ref nextPart)) < partCount)
                    {
                        var start = (long)part * partBytes;
                        var end = Math.Min(start + partBytes, size) - 1;
                        await DownloadPartAsync(url, file, start, end, run, abort.Token);
                    }
                }
                catch
                {
                    await abort.CancelAsync(); // one dead part fails the file — stop the siblings
                    throw;
                }
            }

            var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(WorkerAsync, CancellationToken.None)).ToArray();
            try
            {
                await Task.WhenAll(tasks);
            }
            catch when (!ct.IsCancellationRequested)
            {
                // Surface the root failure, not a sibling's cancellation.
                var root = tasks.Where(t => t.IsFaulted)
                    .Select(t => t.Exception!.InnerException!)
                    .FirstOrDefault(e => e is not OperationCanceledException);
                if (root is not null) ExceptionDispatchInfo.Throw(root);
                throw;
            }
        }

        if (run.Done != size)
            throw new IOException($"Drive download incomplete: got {run.Done} of {size} bytes.");
        run.Report(force: true);
        return new RangedDownloadStats(run.Done, run.Requests, run.Retries, workers);
    }

    private async Task DownloadPartAsync(
        Uri url, SafeFileHandle file, long start, long end, Run run, CancellationToken ct)
    {
        var cursor = new Cursor { Offset = start };
        var attempt = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
        try
        {
            while (cursor.Offset <= end)
            {
                var before = cursor.Offset;
                try
                {
                    await FetchAsync(url, file, cursor, end, buffer, run, ct);
                    if (cursor.Offset <= end) throw new IOException("Drive closed the stream before the range ended.");
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransient(ex))
                {
                    // A stream that made progress before dying starts a fresh retry budget.
                    if (cursor.Offset > before) attempt = 0;
                    if (++attempt >= maxAttempts)
                    {
                        if (ex is OperationCanceledException)
                            throw new TimeoutException($"Drive stopped sending data ({maxAttempts} stalls of {stallTimeout.TotalSeconds:F0}s).", ex);
                        throw;
                    }
                    Interlocked.Increment(ref run.Retries);
                    await Task.Delay(_backoff(attempt), ct);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>One ranged GET from the cursor to <paramref name="end"/>; advances the cursor per write.</summary>
    private async Task FetchAsync(
        Uri url, SafeFileHandle file, Cursor cursor, long end, byte[] buffer, Run run, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(stallTimeout);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(cursor.Offset, end);
        Interlocked.Increment(ref run.Requests);

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        if (res.StatusCode != HttpStatusCode.PartialContent)
            throw new DriveRangeException(res.StatusCode);
        if (res.Content.Headers.ContentRange is { From: { } from } && from != cursor.Offset)
            throw new IOException($"Drive returned range from {from}, expected {cursor.Offset}.");

        await using var body = await res.Content.ReadAsStreamAsync(stall.Token);
        while (cursor.Offset <= end)
        {
            stall.CancelAfter(stallTimeout);
            var want = (int)Math.Min(buffer.Length, end - cursor.Offset + 1);
            var n = await body.ReadAsync(buffer.AsMemory(0, want), stall.Token);
            if (n == 0) return;
            await RandomAccess.WriteAsync(file, buffer.AsMemory(0, n), cursor.Offset, ct);
            cursor.Offset += n;
            run.Add(n);
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        DriveRangeException d => d.Retryable,
        OperationCanceledException => true, // stall timeout (caller already excluded real cancellation)
        HttpRequestException or IOException => true,
        _ => false,
    };

    private sealed class Cursor
    {
        public long Offset;
    }

    private sealed class Run(Action<long> onBytes)
    {
        private readonly Lock _gate = new();
        private long _done;
        private long _reported;
        private long _lastReportTicks;
        public int Requests;
        public int Retries;

        public long Done => Interlocked.Read(ref _done);

        public void Add(int n)
        {
            Interlocked.Add(ref _done, n);
            Report(force: false);
        }

        // Monotonic and throttled: many workers, one ordered progress feed.
        public void Report(bool force)
        {
            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                if (!force && Stopwatch.GetElapsedTime(_lastReportTicks, now) < ReportEvery) return;
                var done = Done;
                if (!force && done <= _reported) return;
                _reported = done;
                _lastReportTicks = now;
                onBytes(done);
            }
        }
    }
}

public sealed class DriveRangeException(HttpStatusCode status)
    : HttpRequestException($"Drive download request failed: {(int)status} {status}.", null, status)
{
    // 403 here is a rate/download limit (permissions already passed files.get); 404/401/416 won't heal.
    public bool Retryable => StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout
        or HttpStatusCode.TooManyRequests || StatusCode is { } s && (int)s >= 500;
}
