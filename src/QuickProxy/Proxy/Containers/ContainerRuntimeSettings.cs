namespace QuickProxy.Proxy.Containers;

public sealed class ContainerRuntimeSettings
{
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = string.Empty;
    public int RefreshIntervalSeconds { get; set; } = 15;
    public int ResyncIntervalSeconds { get; set; } = 300;
    public ContainerStatsSettings Stats { get; set; } = new();
    public ContainerImageUpdateSettings ImageUpdates { get; set; } = new();
    public InternalDnsSettings InternalDns { get; set; } = new();
}

public sealed class ContainerStatsSettings
{
    public bool Enabled { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 15;
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class ContainerImageUpdateSettings
{
    public bool Enabled { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 20;
    public string HarborUrl { get; set; } = string.Empty;
    public string HarborRepositoryPrefix { get; set; } = string.Empty;
}

public sealed class InternalDnsSettings
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "0.0.0.0";
    public string? AnswerIp { get; set; }
    public List<string> Names { get; set; } = [];
}