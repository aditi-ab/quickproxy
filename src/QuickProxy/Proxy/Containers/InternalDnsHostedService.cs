using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace QuickProxy.Proxy.Containers;

public sealed class InternalDnsHostedService(
    IOptions<ContainerRuntimeSettings> options,
    ILogger<InternalDnsHostedService> logger) : BackgroundService, IInternalDnsService
{
    private const int DnsPort = 53;
    private readonly ContainerRuntimeSettings _settings = options.Value;
    private readonly Lock _statusLock = new();

    private InternalDnsStatus _status = new(
        false,
        false,
        string.Empty,
        null,
        null,
        [],
        []);

    public InternalDnsStatus GetStatus()
    {
        lock (_statusLock)
        {
            return _status with
            {
                Names = _status.Names.ToArray(),
                UpstreamServers = _status.UpstreamServers.ToArray()
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredNames = (_settings.InternalDns.Names ?? [])
            .Select(NormalizeHostName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!_settings.Enabled || !_settings.InternalDns.Enabled || configuredNames.Length == 0)
        {
            UpdateStatus(new InternalDnsStatus(
                _settings.Enabled && _settings.InternalDns.Enabled,
                false,
                _settings.InternalDns.BindAddress ?? string.Empty,
                null,
                null,
                configuredNames,
                []));
            logger.LogInformation("Container internal DNS is disabled.");
            return;
        }

        var bindAddressText = string.IsNullOrWhiteSpace(_settings.InternalDns.BindAddress)
            ? "0.0.0.0"
            : _settings.InternalDns.BindAddress.Trim();
        if (!IPAddress.TryParse(bindAddressText, out var bindAddress))
        {
            logger.LogWarning(
                "Container internal DNS bind address '{BindAddress}' is invalid. DNS injection will be skipped.",
                bindAddressText);
            UpdateStatus(new InternalDnsStatus(true, false, bindAddressText, null, null, configuredNames, []));
            return;
        }

        var advertisedDnsServerIp = TryResolveAdvertisedDnsServerIp();
        var answerIp = TryResolveAnswerIp();
        var upstreamServers = GetSystemDnsServers()
            .Where(ip => !string.Equals(ip, advertisedDnsServerIp, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (string.IsNullOrWhiteSpace(advertisedDnsServerIp) || string.IsNullOrWhiteSpace(answerIp) ||
            upstreamServers.Length == 0)
        {
            logger.LogWarning(
                "Container internal DNS is not available. AdvertisedDnsServerIp='{AdvertisedDnsServerIp}', AnswerIp='{AnswerIp}', UpstreamCount={UpstreamCount}.",
                advertisedDnsServerIp ?? "<none>",
                answerIp ?? "<none>",
                upstreamServers.Length);
            UpdateStatus(new InternalDnsStatus(true, false, bindAddressText, advertisedDnsServerIp, answerIp,
                configuredNames, upstreamServers));
            return;
        }

        UdpClient? udpClient = null;
        TcpListener? tcpListener = null;

        try
        {
            udpClient = new UdpClient(new IPEndPoint(bindAddress, DnsPort));
            tcpListener = new TcpListener(bindAddress, DnsPort);
            tcpListener.Start();

            UpdateStatus(new InternalDnsStatus(true, true, bindAddressText, advertisedDnsServerIp, answerIp,
                configuredNames, upstreamServers));
            logger.LogInformation(
                "Container internal DNS listening on {BindAddress}:{Port}, advertisedDnsServerIp={AdvertisedDnsServerIp}, answerIp={AnswerIp}, names={Names}.",
                bindAddressText,
                DnsPort,
                advertisedDnsServerIp,
                answerIp,
                string.Join(", ", configuredNames));

            var udpTask = RunUdpLoopAsync(udpClient, configuredNames, answerIp, upstreamServers, stoppingToken);
            var tcpTask = RunTcpLoopAsync(tcpListener, configuredNames, answerIp, upstreamServers, stoppingToken);
            await Task.WhenAll(udpTask, tcpTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Container internal DNS failed to start or stopped unexpectedly.");
            UpdateStatus(new InternalDnsStatus(true, false, bindAddressText, advertisedDnsServerIp, answerIp,
                configuredNames, upstreamServers));
        }
        finally
        {
            try
            {
                udpClient?.Dispose();
            }
            catch
            {
            }

            try
            {
                tcpListener?.Stop();
            }
            catch
            {
            }
        }
    }

    private async Task RunUdpLoopAsync(
        UdpClient udpClient,
        IReadOnlyCollection<string> names,
        string answerIp,
        IReadOnlyList<string> upstreamServers,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udpClient.ReceiveAsync(cancellationToken);
                logger.LogDebug(
                    "Container internal DNS received UDP query from {RemoteEndPoint}. {Query}",
                    received.RemoteEndPoint,
                    DescribeDnsQuery(received.Buffer));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var response = await ResolveMessageAsync(received.Buffer, names, answerIp, upstreamServers, false,
                cancellationToken);
            if (response.Length == 0) continue;

            try
            {
                await udpClient.SendAsync(response, response.Length, received.RemoteEndPoint);
                logger.LogDebug(
                    "Container internal DNS sent UDP response to {RemoteEndPoint}. Bytes={ResponseLength}.",
                    received.RemoteEndPoint,
                    response.Length);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Container internal DNS failed to send UDP response.");
            }
        }
    }

    private async Task RunTcpLoopAsync(
        TcpListener listener,
        IReadOnlyCollection<string> names,
        string answerIp,
        IReadOnlyList<string> upstreamServers,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
                logger.LogDebug(
                    "Container internal DNS accepted TCP query connection from {RemoteEndPoint}.",
                    client.Client.RemoteEndPoint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(async () =>
            {
                using var tcpClient = client;
                try
                {
                    await using var stream = tcpClient.GetStream();
                    var lengthBuffer = new byte[2];
                    await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
                    var messageLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
                    var messageBuffer = new byte[messageLength];
                    await stream.ReadExactlyAsync(messageBuffer, cancellationToken);
                    logger.LogDebug(
                        "Container internal DNS received TCP query from {RemoteEndPoint}. {Query}",
                        tcpClient.Client.RemoteEndPoint,
                        DescribeDnsQuery(messageBuffer));

                    var response = await ResolveMessageAsync(messageBuffer, names, answerIp, upstreamServers, true,
                        cancellationToken);
                    if (response.Length == 0)
                    {
                        logger.LogDebug(
                            "Container internal DNS produced no TCP response for {RemoteEndPoint}.",
                            tcpClient.Client.RemoteEndPoint);
                        return;
                    }

                    var responseLengthBuffer = new byte[2];
                    BinaryPrimitives.WriteUInt16BigEndian(responseLengthBuffer, (ushort)response.Length);
                    await stream.WriteAsync(responseLengthBuffer, cancellationToken);
                    await stream.WriteAsync(response, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    logger.LogDebug(
                        "Container internal DNS sent TCP response to {RemoteEndPoint}. Bytes={ResponseLength}.",
                        tcpClient.Client.RemoteEndPoint,
                        response.Length);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogDebug(ex, "Container internal DNS failed to serve TCP request.");
                }
            }, cancellationToken);
        }
    }

    private async Task<byte[]> ResolveMessageAsync(
        byte[] message,
        IReadOnlyCollection<string> names,
        string answerIp,
        IReadOnlyList<string> upstreamServers,
        bool useTcpForwarding,
        CancellationToken cancellationToken)
    {
        if (TryBuildLocalResponse(message, names, answerIp, out var localResponse, out var localQuery))
        {
            logger.LogDebug(
                "Container internal DNS answered locally. {Query} -> {AnswerIp}",
                DescribeDnsQuery(localQuery),
                answerIp);
            return localResponse;
        }

        logger.LogDebug(
            "Container internal DNS forwarding query to upstream resolvers. {Query} Upstreams={Upstreams} Transport={Transport}",
            DescribeDnsQuery(message),
            string.Join(", ", upstreamServers),
            useTcpForwarding ? "TCP" : "UDP");
        return await ForwardMessageAsync(message, upstreamServers, useTcpForwarding, cancellationToken);
    }

    private bool TryBuildLocalResponse(
        byte[] message,
        IReadOnlyCollection<string> names,
        string answerIp,
        out byte[] response,
        out ParsedDnsQuery? query)
    {
        response = [];
        query = null;
        if (message.Length < 12) return false;

        query = DnsQueryParser.TryParse(message);
        if (query is null) return false;

        if (query.QuestionCount != 1
            || query.QuestionType != 1
            || query.QuestionClass != 1
            || !MatchesConfiguredName(query.QuestionName, names)
            || !IPAddress.TryParse(answerIp, out var answerAddress))
            return false;

        response = DnsMessageBuilder.BuildARecordResponse(message, query, answerAddress);
        return true;
    }

    private async Task<byte[]> ForwardMessageAsync(
        byte[] message,
        IReadOnlyList<string> upstreamServers,
        bool useTcpForwarding,
        CancellationToken cancellationToken)
    {
        foreach (var upstream in upstreamServers)
            try
            {
                if (useTcpForwarding)
                {
                    logger.LogDebug("Container internal DNS forwarding query to upstream '{Upstream}' over TCP.",
                        upstream);
                    var response = await ForwardTcpAsync(upstream, message, cancellationToken);
                    if (response.Length > 0)
                    {
                        logger.LogDebug(
                            "Container internal DNS received TCP upstream response from '{Upstream}'. Bytes={ResponseLength}.",
                            upstream,
                            response.Length);
                        return response;
                    }
                }
                else
                {
                    logger.LogDebug("Container internal DNS forwarding query to upstream '{Upstream}' over UDP.",
                        upstream);
                    var response = await ForwardUdpAsync(upstream, message, cancellationToken);
                    if (response.Length > 0)
                    {
                        logger.LogDebug(
                            "Container internal DNS received UDP upstream response from '{Upstream}'. Bytes={ResponseLength}.",
                            upstream,
                            response.Length);
                        return response;
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Container internal DNS forwarding to upstream '{Upstream}' failed.", upstream);
            }

        logger.LogWarning(
            "Container internal DNS could not resolve or forward a DNS query through configured upstream resolvers.");
        return [];
    }

    private static async Task<byte[]> ForwardUdpAsync(string upstream, byte[] message,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient();
        await udpClient.SendAsync(message, message.Length, upstream, DnsPort);
        var response = await udpClient.ReceiveAsync(cancellationToken);
        return response.Buffer;
    }

    private static async Task<byte[]> ForwardTcpAsync(string upstream, byte[] message,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(upstream, DnsPort, cancellationToken);
        await using var stream = client.GetStream();
        var lengthBuffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthBuffer, (ushort)message.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(message, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthBuffer);
        var response = new byte[responseLength];
        await stream.ReadExactlyAsync(response, cancellationToken);
        return response;
    }

    private string? TryResolveAdvertisedDnsServerIp()
    {
        return TryResolvePrimaryIPv4();
    }

    private string? TryResolveAnswerIp()
    {
        var configuredAnswerIp = (_settings.InternalDns.AnswerIp ?? string.Empty).Trim();
        if (IPAddress.TryParse(configuredAnswerIp, out var configuredAddress)
            && configuredAddress.AddressFamily == AddressFamily.InterNetwork)
            return configuredAddress.ToString();

        if (IsRunningInContainer()) return TryResolveHostDockerInternalIp();

        return TryResolvePrimaryIPv4();
    }

    private static bool IsRunningInContainer()
    {
        if (!OperatingSystem.IsWindows() && File.Exists("/.dockerenv")) return true;

        var containerEnv = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (bool.TryParse(containerEnv, out var parsed) && parsed) return true;

        var containerValue = Environment.GetEnvironmentVariable("CONTAINER");
        if (!string.IsNullOrWhiteSpace(containerValue)) return true;

        var serverAppPath = Environment.GetEnvironmentVariable("APP_HOME");
        return !string.IsNullOrWhiteSpace(serverAppPath) && !OperatingSystem.IsWindows();
    }

    private static string? TryResolveHostDockerInternalIp()
    {
        try
        {
            return Dns.GetHostAddresses("host.docker.internal")
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolvePrimaryIPv4()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x =>
                    x.OperationalStatus == OperationalStatus.Up
                    && x.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && x.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;

            return Dns.GetHostAddresses(Dns.GetHostName())
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetSystemDnsServers()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up)
                .SelectMany(x => x.GetIPProperties().DnsAddresses)
                .Where(IsUsableIpv4Address)
                .Select(x => x.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private void UpdateStatus(InternalDnsStatus status)
    {
        lock (_statusLock)
        {
            _status = status;
        }
    }

    private static string DescribeDnsQuery(byte[] message)
    {
        var query = DnsQueryParser.TryParse(message);
        return DescribeDnsQuery(query);
    }

    private static string DescribeDnsQuery(ParsedDnsQuery? query)
    {
        if (query is null) return "Unparseable DNS query";

        return
            $"Name={query.QuestionName}, Type={query.QuestionType}, Class={query.QuestionClass}, Questions={query.QuestionCount}, TxId=0x{query.TransactionId:x4}";
    }

    private static bool MatchesConfiguredName(string questionName, IReadOnlyCollection<string> names)
    {
        foreach (var configuredName in names)
        {
            if (configuredName.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = configuredName[1..];
                if (questionName.Length > suffix.Length
                    && questionName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;

                continue;
            }

            if (string.Equals(questionName, configuredName, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static string NormalizeHostName(string? value)
    {
        return (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static bool IsUsableIpv4Address(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
               && !(bytes[0] == 169 && bytes[1] == 254)
               && !address.Equals(IPAddress.Any);
    }

    private sealed record ParsedDnsQuery(
        ushort TransactionId,
        ushort Flags,
        ushort QuestionCount,
        string QuestionName,
        ushort QuestionType,
        ushort QuestionClass,
        int QuestionEndOffset);

    private static class DnsQueryParser
    {
        public static ParsedDnsQuery? TryParse(byte[] message)
        {
            if (message.Length < 12) return null;

            var transactionId = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(0, 2));
            var flags = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(2, 2));
            var questionCount = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(4, 2));
            if (questionCount == 0) return null;

            var offset = 12;
            var labels = new List<string>();
            while (offset < message.Length)
            {
                var labelLength = message[offset];
                offset += 1;
                if (labelLength == 0) break;

                if ((labelLength & 0xC0) != 0 || offset + labelLength > message.Length) return null;

                labels.Add(Encoding.ASCII.GetString(message, offset, labelLength));
                offset += labelLength;
            }

            if (offset + 4 > message.Length) return null;

            var questionType = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset, 2));
            var questionClass = BinaryPrimitives.ReadUInt16BigEndian(message.AsSpan(offset + 2, 2));
            offset += 4;

            return new ParsedDnsQuery(
                transactionId,
                flags,
                questionCount,
                string.Join('.', labels).ToLowerInvariant(),
                questionType,
                questionClass,
                offset);
        }
    }

    private static class DnsMessageBuilder
    {
        public static byte[] BuildARecordResponse(byte[] request, ParsedDnsQuery query, IPAddress answerIp)
        {
            var answerBytes = answerIp.GetAddressBytes();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            WriteUInt16(writer, query.TransactionId);
            WriteUInt16(writer, 0x8180);
            WriteUInt16(writer, 1);
            WriteUInt16(writer, 1);
            WriteUInt16(writer, 0);
            WriteUInt16(writer, 0);

            writer.Write(request, 12, query.QuestionEndOffset - 12);
            WriteUInt16(writer, 0xC00C);
            WriteUInt16(writer, 1);
            WriteUInt16(writer, 1);
            writer.Write(new byte[] { 0, 0, 0, 30 });
            WriteUInt16(writer, (ushort)answerBytes.Length);
            writer.Write(answerBytes);

            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteUInt16(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)(value >> 8));
            writer.Write((byte)(value & 0xFF));
        }
    }
}