using System.Net;
using System.Text;

namespace Ecomads.WebApplication.Tests.Wb;

// Ordered HTTP responses: unexpected retries fail the test instead of returning an endless fixture.
internal sealed class WbHttpScenario : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> steps = new();
    public List<string> Requests { get; } = [];
    public WbHttpScenario Reply(string body, HttpStatusCode status = HttpStatusCode.OK, int? retryAfter = null)
    {
        steps.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter.HasValue) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfter.Value));
            return Task.FromResult(response);
        });
        return this;
    }
    public WbHttpScenario Fail(Exception error)
    { steps.Enqueue(_ => Task.FromException<HttpResponseMessage>(error)); return this; }
    public WbHttpScenario Interrupt(byte[] prefix)
    {
        steps.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(new InterruptedStream(prefix)) }));
        return this;
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!.PathAndQuery);
        if (steps.Count == 0) throw new InvalidOperationException("Неожиданный дополнительный запрос WB.");
        return steps.Dequeue()(ct);
    }

    private sealed class InterruptedStream(byte[] prefix) : Stream
    {
        private int position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            if (position == prefix.Length) throw new IOException("Simulated interrupted HTTP body.");
            var take = Math.Min(count, prefix.Length - position);
            prefix.AsSpan(position, take).CopyTo(buffer.AsSpan(offset, take)); position += take; return take;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length == 0) return ValueTask.FromResult(0);
            if (position == prefix.Length) return ValueTask.FromException<int>(new IOException("Simulated interrupted HTTP body."));
            var take = Math.Min(buffer.Length, prefix.Length - position);
            prefix.AsMemory(position, take).CopyTo(buffer); position += take; return ValueTask.FromResult(take);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class WbTestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
