namespace QuickProxy.Proxy.Validation;

public sealed class ProxyHostValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
}