namespace QuickProxy.Proxy.Containers;

public interface IInternalDnsService
{
    InternalDnsStatus GetStatus();
}

public sealed record InternalDnsStatus(
    bool Enabled,
    bool Healthy,
    string BindAddress,
    string? AdvertisedDnsServerIp,
    string? AnswerIp,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> UpstreamServers)
{
    public bool CanInject => Enabled
                             && Healthy
                             && !string.IsNullOrWhiteSpace(AdvertisedDnsServerIp)
                             && Names.Count > 0
                             && UpstreamServers.Count > 0;

    public string BuildFingerprint()
    {
        if (!Enabled || Names.Count == 0) return "dns:disabled";

        if (!Healthy || string.IsNullOrWhiteSpace(AdvertisedDnsServerIp) || string.IsNullOrWhiteSpace(AnswerIp))
            return "dns:unavailable";

        return $"dns:{AdvertisedDnsServerIp}|{AnswerIp}|{string.Join(',', Names)}";
    }
}