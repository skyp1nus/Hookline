using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

using Hookline.Modules.YouTubeUploads.Infrastructure;

namespace Hookline.Modules.YouTubeUploads.Tests;

public sealed class RangedFileDownloaderTests : IDisposable
{
    private const int Part = 1024 * 1024;
    private static readonly Uri Url = new("https://drive.test/files/f?alt=media");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ranged-{Guid.NewGuid():N}.tmp");
    private readonly byte[] _data = RandomBytes(10 * Part + 12_345);

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task Downloads_every_byte_across_parallel_streams()
    {
        var server = new RangeServer(_data);
        var progress = new List<long>();

        var stats = await Downloader(server).DownloadAsync(Url, _path, _data.Length, b => progress.Add(b), CancellationToken.None);

        Assert.Equal(_data, await File.ReadAllBytesAsync(_path));
        Assert.Equal(_data.Length, stats.Bytes);
        Assert.Equal(11, stats.Requests);
        Assert.Equal(0, stats.Retries);
        Assert.Equal(4, stats.Streams);
        Assert.Equal(_data.Length, progress[^1]);
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public async Task Retries_a_part_after_a_server_error()
    {
        var server = new RangeServer(_data) { Fault = (from, hit) => from == 2 * Part && hit == 1 ? Status(HttpStatusCode.ServiceUnavailable) : null };

        var stats = await Downloader(server).DownloadAsync(Url, _path, _data.Length, _ => { }, CancellationToken.None);

        Assert.Equal(_data, await File.ReadAllBytesAsync(_path));
        Assert.Equal(1, stats.Retries);
    }

    [Fact]
    public async Task Resumes_a_stalled_stream_from_the_last_written_offset()
    {
        const int servedBeforeStall = 300_000;
        var server = new RangeServer(_data)
        {
            Fault = (from, hit) => from == Part && hit == 1 ? StallAfter(_data, from, servedBeforeStall) : null,
        };

        var stats = await Downloader(server, stall: TimeSpan.FromMilliseconds(200))
            .DownloadAsync(Url, _path, _data.Length, _ => { }, CancellationToken.None);

        Assert.Equal(_data, await File.ReadAllBytesAsync(_path));
        Assert.Equal(1, stats.Retries);
        Assert.Contains(Part + servedBeforeStall, server.RangeStarts);
    }

    [Fact]
    public async Task Fails_without_retrying_when_the_file_is_gone()
    {
        var server = new RangeServer(_data) { Fault = (_, _) => Status(HttpStatusCode.NotFound) };

        var ex = await Assert.ThrowsAsync<DriveRangeException>(() =>
            Downloader(server).DownloadAsync(Url, _path, _data.Length, _ => { }, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.True(server.Requests <= 4, $"expected no retries, saw {server.Requests} requests");
    }

    [Fact]
    public async Task Gives_up_on_a_part_after_max_attempts()
    {
        var server = new RangeServer(_data) { Fault = (from, _) => from == 0 ? Status(HttpStatusCode.BadGateway) : null };

        var ex = await Assert.ThrowsAsync<DriveRangeException>(() =>
            Downloader(server).DownloadAsync(Url, _path, _data.Length, _ => { }, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(3, server.RangeStarts.Count(s => s == 0));
    }

    [Fact]
    public async Task A_part_that_keeps_stalling_fails_with_a_timeout_not_a_bare_cancellation()
    {
        var server = new RangeServer(_data) { Fault = (from, _) => from == 0 ? StallAfter(_data, 0, 0) : null };

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            Downloader(server, stall: TimeSpan.FromMilliseconds(100))
                .DownloadAsync(Url, _path, _data.Length, _ => { }, CancellationToken.None));

        Assert.Contains("stopped sending data", ex.Message);
    }

    private static RangedFileDownloader Downloader(RangeServer server, TimeSpan? stall = null) =>
        new(new HttpClient(server), streams: 4, partBytes: Part, stall ?? TimeSpan.FromSeconds(5),
            maxAttempts: 3, backoff: _ => TimeSpan.Zero);

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        new Random(42).NextBytes(b);
        return b;
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new ByteArrayContent([]) };

    private static HttpResponseMessage Partial(byte[] data, long from, long to, Stream body)
    {
        var res = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(body) };
        res.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
        return res;
    }

    private static HttpResponseMessage StallAfter(byte[] data, long from, int served) =>
        Partial(data, from, from + Part - 1, new StallingStream(data.AsMemory((int)from, served)));

    private sealed class RangeServer(byte[] data) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<long, int> _hits = new();
        private int _requests;

        public Func<long, int, HttpResponseMessage?>? Fault { get; init; }
        public ConcurrentBag<long> RangeStarts { get; } = [];
        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _requests);
            var range = request.Headers.Range!.Ranges.Single();
            var from = range.From!.Value;
            var to = range.To!.Value;
            RangeStarts.Add(from);

            var hit = _hits.AddOrUpdate(from, 1, (_, n) => n + 1);
            var res = Fault?.Invoke(from, hit)
                ?? Partial(data, from, to, new MemoryStream(data, (int)from, (int)(to - from + 1)));
            return Task.FromResult(res);
        }
    }

    /// <summary>Serves a prefix, then never returns another byte until cancelled.</summary>
    private sealed class StallingStream(ReadOnlyMemory<byte> prefix) : Stream
    {
        private int _pos;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _pos);
                prefix.Slice(_pos, n).CopyTo(buffer);
                _pos += n;
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
