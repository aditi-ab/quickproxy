namespace QuickProxy.Proxy.Containers;

public sealed class ComposeProjectValidationResult
{
    public bool Valid { get; set; }
    public string Output { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
}