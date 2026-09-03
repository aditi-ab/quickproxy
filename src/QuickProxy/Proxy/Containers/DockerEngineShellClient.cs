using System.Globalization;
using System.IO.Pipes;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace QuickProxy.Proxy.Containers;

internal sealed class DockerEngineShellClient(Uri endpoint)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Uri _endpoint = endpoint;

    public async Task StreamShellAsync(
        string containerId,
        string shellCommand,
        ChannelReader<ContainerShellClientMessage> input,
        Func<ContainerShellServerMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var execId = await CreateExecAsync(containerId, shellCommand, cancellationToken);
        await using var session = await StartExecAsync(execId, cancellationToken);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var outputTask = PumpOutputAsync(session.Stream, onMessage, linkedCts.Token);
        var inputTask = PumpInputAsync(execId, session.Stream, input, linkedCts.Token);

        try
        {
            await Task.WhenAny(outputTask, inputTask);
        }
        finally
        {
            linkedCts.Cancel();
            try
            {
                await session.Stream.DisposeAsync();
            }
            catch
            {
            }

            try
            {
                await Task.WhenAll(outputTask, inputTask);
            }
            catch (OperationCanceledException)
            {
            }

            var execState = await InspectExecAsync(execId, cancellationToken);
            await onMessage(new ContainerShellServerMessage(
                "exit",
                Message: execState.ExitCode is >= 0
                    ? $"Shell exited with code {execState.ExitCode.Value}."
                    : "Shell session ended."), cancellationToken);
        }
    }

    private async Task<string> CreateExecAsync(string containerId, string shellCommand,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<ExecCreateResponse>(
            HttpMethod.Post,
            $"/containers/{Uri.EscapeDataString(containerId)}/exec",
            new ExecCreateRequest
            {
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                Cmd = [shellCommand]
            },
            cancellationToken);

        return string.IsNullOrWhiteSpace(response.Id)
            ? throw new InvalidOperationException("Docker did not return an exec id.")
            : response.Id;
    }

    private Task ResizeExecAsync(string execId, int cols, int rows, CancellationToken cancellationToken)
    {
        return SendJsonAsync<object>(
            HttpMethod.Post,
            $"/exec/{Uri.EscapeDataString(execId)}/resize?w={cols}&h={rows}",
            null,
            cancellationToken);
    }

    private Task<ExecInspectResponse> InspectExecAsync(string execId, CancellationToken cancellationToken)
    {
        return SendJsonAsync<ExecInspectResponse>(
            HttpMethod.Get,
            $"/exec/{Uri.EscapeDataString(execId)}/json",
            null,
            cancellationToken);
    }

    private async Task<DockerHijackedSession> StartExecAsync(string execId, CancellationToken cancellationToken)
    {
        var stream = await ConnectAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new ExecStartRequest
            {
                Detach = false,
                Tty = true
            }, JsonOptions);

            await WriteRequestAsync(
                stream,
                HttpMethod.Post,
                $"/exec/{Uri.EscapeDataString(execId)}/start",
                payload,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Connection"] = "Upgrade",
                    ["Upgrade"] = "tcp"
                },
                cancellationToken);

            var response = await ReadResponseHeaderAsync(stream, cancellationToken);
            if (response.StatusCode is not (101 or 200))
            {
                var body = await ReadResponseBodyAsync(stream, response.Headers, cancellationToken);
                throw CreateDockerApiException(response.StatusCode, body);
            }

            return new DockerHijackedSession(stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body,
        CancellationToken cancellationToken)
    {
        await using var stream = await ConnectAsync(cancellationToken);
        var payload = body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        await WriteRequestAsync(stream, method, path, payload, null, cancellationToken);

        var response = await ReadResponseHeaderAsync(stream, cancellationToken);
        var responseBody = await ReadResponseBodyAsync(stream, response.Headers, cancellationToken);
        if (response.StatusCode < 200 || response.StatusCode >= 300)
            throw CreateDockerApiException(response.StatusCode, responseBody);

        if (typeof(T) == typeof(object) || responseBody.Length == 0) return default!;

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
               ?? throw new InvalidOperationException("Docker returned an unexpected empty response.");
    }

    private async Task PumpInputAsync(
        string execId,
        Stream stream,
        ChannelReader<ContainerShellClientMessage> input,
        CancellationToken cancellationToken)
    {
        await foreach (var message in input.ReadAllAsync(cancellationToken))
        {
            if (string.Equals(message.Type, "input", StringComparison.OrdinalIgnoreCase))
            {
                var data = message.Data ?? string.Empty;
                if (data.Length == 0) continue;

                var bytes = Encoding.UTF8.GetBytes(data);
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                continue;
            }

            if (string.Equals(message.Type, "resize", StringComparison.OrdinalIgnoreCase)
                && message.Cols is > 0
                && message.Rows is > 0)
                await ResizeExecAsync(execId, message.Cols.Value, message.Rows.Value, cancellationToken);
        }
    }

    private static async Task PumpOutputAsync(
        Stream stream,
        Func<ContainerShellServerMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var byteBuffer = new byte[4096];
        var charBuffer = new char[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(byteBuffer, cancellationToken);
            if (read <= 0) break;

            var chars = decoder.GetChars(byteBuffer, 0, read, charBuffer, 0, false);
            if (chars > 0)
                await onMessage(new ContainerShellServerMessage("output", new string(charBuffer, 0, chars)),
                    cancellationToken);
        }
    }

    private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(_endpoint.Scheme, "npipe", StringComparison.OrdinalIgnoreCase))
        {
            var pipeName = Path.GetFileName(_endpoint.AbsolutePath);
            var serverName = string.IsNullOrWhiteSpace(_endpoint.Host) ? "." : _endpoint.Host;
            var pipe = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cancellationToken);
            return pipe;
        }

        if (string.Equals(_endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase))
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_endpoint.AbsolutePath), cancellationToken);
                return new NetworkStream(socket, true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        if (string.Equals(_endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            var port = _endpoint.IsDefaultPort
                ? string.Equals(_endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
                : _endpoint.Port;

            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(_endpoint.Host, port, cancellationToken);
                Stream stream = tcpClient.GetStream();
                if (string.Equals(_endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    var sslStream = new SslStream(stream, false, static (_, _, _, _) => true);
                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = _endpoint.Host,
                        RemoteCertificateValidationCallback = static (_, _, _, _) => true
                    }, cancellationToken);
                    stream = sslStream;
                }

                return new OwnedClientStream(stream, tcpClient);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Docker shell transport does not support endpoint scheme '{_endpoint.Scheme}'.");
    }

    private async Task WriteRequestAsync(
        Stream stream,
        HttpMethod method,
        string path,
        byte[]? body,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken cancellationToken)
    {
        var requestPath = NormalizeRequestPath(path);
        var host = string.IsNullOrWhiteSpace(_endpoint.Host) ? "docker" : _endpoint.Host;
        var builder = new StringBuilder()
            .Append(method.Method).Append(' ').Append(requestPath).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(host).Append("\r\n")
            .Append("User-Agent: QuickProxy-DockerShell/1.0\r\n");

        if (body is not null)
            builder
                .Append("Content-Type: application/json\r\n")
                .Append("Content-Length: ").Append(body.Length).Append("\r\n");
        else
            builder.Append("Content-Length: 0\r\n");

        if (extraHeaders is not null)
            foreach (var header in extraHeaders)
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        else
            builder.Append("Connection: close\r\n");

        builder.Append("\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken);
        if (body is not null) await stream.WriteAsync(body, cancellationToken);

        await stream.FlushAsync(cancellationToken);
    }

    private string NormalizeRequestPath(string path)
    {
        var endpointPath = _endpoint.AbsolutePath;
        if (string.Equals(_endpoint.Scheme, "npipe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_endpoint.Scheme, "unix", StringComparison.OrdinalIgnoreCase))
            endpointPath = string.Empty;

        endpointPath = endpointPath.TrimEnd('/');
        return $"{endpointPath}{path}";
    }

    private static async Task<HttpResponseHeader> ReadResponseHeaderAsync(Stream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(512);
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0) throw new IOException("Docker closed the connection before sending a full HTTP response.");

            bytes.Add(buffer[0]);
            var count = bytes.Count;
            if (count >= 4
                && bytes[count - 4] == '\r'
                && bytes[count - 3] == '\n'
                && bytes[count - 2] == '\r'
                && bytes[count - 1] == '\n')
                break;
        }

        var headerText = Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(bytes));
        var lines = headerText.Split("\r\n");
        if (lines.Length == 0) throw new InvalidOperationException("Docker returned an invalid HTTP response.");

        var statusLine = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (statusLine.Length < 2 || !int.TryParse(statusLine[1], out var statusCode))
            throw new InvalidOperationException($"Docker returned an invalid status line: '{lines[0]}'.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) continue;

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0) continue;

            headers[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        return new HttpResponseHeader(statusCode, headers);
    }

    private static async Task<byte[]> ReadResponseBodyAsync(
        Stream stream,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            return await ReadChunkedBodyAsync(stream, cancellationToken);

        if (headers.TryGetValue("Content-Length", out var contentLengthValue)
            && long.TryParse(contentLengthValue, out var contentLength)
            && contentLength > 0)
        {
            var body = new byte[contentLength];
            await stream.ReadExactlyAsync(body, cancellationToken);
            return body;
        }

        return [];
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        while (true)
        {
            var line = await ReadAsciiLineAsync(stream, cancellationToken);
            if (!int.TryParse(line, NumberStyles.HexNumber, null, out var chunkSize))
                throw new InvalidOperationException("Docker returned an invalid chunked response.");

            if (chunkSize == 0)
            {
                await ReadAsciiLineAsync(stream, cancellationToken);
                break;
            }

            var chunk = new byte[chunkSize];
            await stream.ReadExactlyAsync(chunk, cancellationToken);
            await output.WriteAsync(chunk, cancellationToken);
            await ReadAsciiLineAsync(stream, cancellationToken);
        }

        return output.ToArray();
    }

    private static async Task<string> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(64);
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0) throw new IOException("Docker closed the connection while reading a chunked response.");

            bytes.Add(buffer[0]);
            var count = bytes.Count;
            if (count >= 2 && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                return Encoding.ASCII.GetString(CollectionsMarshal.AsSpan(bytes)[..^2]);
        }
    }

    private static Exception CreateDockerApiException(int statusCode, byte[] responseBody)
    {
        if (responseBody.Length > 0)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<DockerErrorResponse>(responseBody, JsonOptions);
                if (!string.IsNullOrWhiteSpace(payload?.Message)) return new InvalidOperationException(payload.Message);
            }
            catch
            {
            }

            return new InvalidOperationException(Encoding.UTF8.GetString(responseBody));
        }

        return new InvalidOperationException($"Docker API request failed with status code {statusCode}.");
    }

    private sealed record ExecCreateRequest
    {
        public bool AttachStdin { get; init; }
        public bool AttachStdout { get; init; }
        public bool AttachStderr { get; init; }
        public bool Tty { get; init; }
        public string[] Cmd { get; init; } = [];
    }

    private sealed record ExecCreateResponse
    {
        public string Id { get; } = string.Empty;
    }

    private sealed record ExecStartRequest
    {
        public bool Detach { get; init; }
        public bool Tty { get; init; }
    }

    private sealed record ExecInspectResponse
    {
        public int? ExitCode { get; init; }
    }

    private sealed record DockerErrorResponse
    {
        public string? Message { get; init; }
    }

    private sealed record HttpResponseHeader(int StatusCode, IReadOnlyDictionary<string, string> Headers);

    private sealed class DockerHijackedSession(Stream stream) : IAsyncDisposable
    {
        public Stream Stream { get; } = stream;

        public ValueTask DisposeAsync()
        {
            return Stream.DisposeAsync();
        }
    }

    private sealed class OwnedClientStream(Stream inner, TcpClient client) : Stream
    {
        private readonly TcpClient _client = client;
        private readonly Stream _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            _inner.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return _inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing) _inner.Dispose();
            }
            finally
            {
                _client.Dispose();
                base.Dispose(disposing);
            }
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await _inner.DisposeAsync();
            }
            finally
            {
                _client.Dispose();
                await base.DisposeAsync();
            }
        }
    }
}