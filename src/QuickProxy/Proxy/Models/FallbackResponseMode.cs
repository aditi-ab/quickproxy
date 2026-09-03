namespace QuickProxy.Proxy.Models;

public enum FallbackResponseMode
{
    Default = 0,
    StatusCode = 1,
    HtmlFile = 2,
    Redirect = 3
}