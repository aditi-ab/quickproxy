namespace QuickProxy.Proxy.Models;

public enum IssuerCaSourceMode
{
    UploadPem = 0,
    UploadPfx = 1,
    PathPem = 2,
    PathPfx = 3,
    StoreThumbprint = 4
}