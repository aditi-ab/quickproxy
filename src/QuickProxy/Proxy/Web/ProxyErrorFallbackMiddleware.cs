using QuickProxy.Proxy.Runtime;
using QuickProxy.Shared.Web;

namespace QuickProxy.Proxy.Web;

public sealed class ProxyErrorFallbackMiddleware(RequestDelegate next)
{
    public async Task Invoke(
        HttpContext context,
        IProxyHostRuntime runtime,
        IFallbackSettingsCache settingsCache,
        FallbackPageResponder responder)
    {
        if (InternalApiPaths.IsInternalApi(context.Request.Path) ||
            context.WebSockets.IsWebSocketRequest ||
            runtime.MatchHost(context.Request.Host.Value) is null)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var countingStream = new CountingWriteStream(originalBody);
        context.Response.Body = countingStream;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (countingStream.BytesWritten > 0) return;

        var settings = settingsCache.Get();
        if (context.Response.StatusCode == StatusCodes.Status502BadGateway)
            await responder.WriteBadGatewayAsync(context, settings);
        else if (context.Response.StatusCode == StatusCodes.Status504GatewayTimeout)
            await responder.WriteGatewayTimeoutAsync(context, settings);
    }

    private sealed class CountingWriteStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
            inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BytesWritten += count;
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BytesWritten += buffer.Length;
            inner.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }
    }
}