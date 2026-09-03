namespace QuickProxy.Proxy.Runtime;

public interface IHostTemplateValueProvider
{
    IReadOnlyDictionary<string, string> TemplateValues { get; }
    string ReplacePlaceholders(string input);
    string ReplaceKvPlaceholders(string input);
}