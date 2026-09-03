using System.Security.Cryptography.X509Certificates;
using QuickProxy.Proxy.Storage.Db;

namespace QuickProxy.Proxy.Runtime;

public sealed class DevelopmentCertificateAccessor(
    IConfiguration configuration,
    IApplicationDataStore applicationDataStore)
{
    public const string FileName = "devcert.pfx";
    public const string Password = "dev";

    public byte[] GetFallbackPfxBytes()
    {
        return applicationDataStore.GetOrCreate("development-fallback-certificate",
            () => AdminCertificateAccessor.CreateSelfSignedPfx(configuration, Password));
    }

    public X509Certificate2 LoadCertificate()
    {
        return X509CertificateLoader.LoadPkcs12(GetFallbackPfxBytes(), Password);
    }
}