using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using HttpClient = System.Net.Http.HttpClient;
using HttpClientHandler = System.Net.Http.HttpClientHandler;
using HttpResponseMessage = System.Net.Http.HttpResponseMessage;

namespace QuickProxy.Sdk;

public sealed class QuickProxyClient : IDisposable
{
    private const string QuickProxyFallbackCertificateOid = "1.3.6.1.4.1.55555.1.1";
    private const string LegacyQuickProxyFallbackSubject = "CN=QuickProxy SSL";
    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string EnhancedKeyUsageOid = "2.5.29.37";
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly bool _disposeHttpClient;
    private readonly HttpClient _httpClient;

    public QuickProxyClient(Uri baseUri)
        : this(CreateDefaultHttpClient(baseUri), true)
    {
    }

    public QuickProxyClient(HttpClient httpClient)
        : this(httpClient, false)
    {
    }

    private QuickProxyClient(HttpClient httpClient, bool disposeHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _httpClient.BaseAddress ??= NormalizeBaseUri(_httpClient.BaseAddress ??
                                                     throw new InvalidOperationException(
                                                         "HttpClient.BaseAddress must be set."));
        _disposeHttpClient = disposeHttpClient;
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
            _httpClient.Dispose();
    }

    public Task<IReadOnlyList<QuickProxyConfigMetadata>> ListMetadataAsync(string? prefix = null, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<List<QuickProxyConfigMetadata>>(BuildConfigUri(prefix, decrypt), cancellationToken)
            .ContinueWith<IReadOnlyList<QuickProxyConfigMetadata>>(static task => task.Result ?? [], cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task<QuickProxyConfigMetadata> GetMetadataAsync(string key, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<QuickProxyConfigMetadata>(BuildConfigUri(key, false, false, decrypt),
            cancellationToken);
    }

    public Task<IReadOnlyList<QuickProxyConfigMetadata>> RecurseMetadataAsync(string key, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<List<QuickProxyConfigMetadata>>(BuildConfigUri(key, false, true, decrypt),
                cancellationToken)
            .ContinueWith<IReadOnlyList<QuickProxyConfigMetadata>>(static task => task.Result ?? [], cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task<IReadOnlyList<QuickProxyConfigEntry>> ListEntriesAsync(string? prefix = null, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<List<QuickProxyConfigEntry>>(BuildConfigExportUri(prefix, decrypt), cancellationToken)
            .ContinueWith<IReadOnlyList<QuickProxyConfigEntry>>(static task => task.Result ?? [], cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task<QuickProxyConfigEntry> GetEntryAsync(string key, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<QuickProxyConfigEntry>(BuildConfigExportUri(key, false, decrypt),
            cancellationToken);
    }

    public Task<IReadOnlyList<QuickProxyConfigEntry>> RecurseEntriesAsync(string key, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        return GetRequiredAsync<List<QuickProxyConfigEntry>>(BuildConfigExportUri(key, true, decrypt),
                cancellationToken)
            .ContinueWith<IReadOnlyList<QuickProxyConfigEntry>>(static task => task.Result ?? [], cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<string> GetRawTextAsync(string key, bool decrypt = false, bool template = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(BuildConfigUri(key, true, false, decrypt, template), cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<byte[]> GetRawBytesAsync(string key, bool decrypt = false,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(BuildConfigUri(key, true, false, decrypt), cancellationToken)
            .ConfigureAwait(false);

        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public async Task<byte[]> GetDevelopmentCertificateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(BuildDevelopmentCertificateUri(), cancellationToken).ConfigureAwait(false);

        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private static HttpClient CreateDefaultHttpClient(Uri baseUri)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = static (_, certificate, _, sslPolicyErrors) =>
                IsTrustedQuickProxyFallbackCertificate(certificate ?? TryConvertCertificate(certificate),
                    sslPolicyErrors)
        };

        return new HttpClient(handler)
        {
            BaseAddress = NormalizeBaseUri(baseUri)
        };
    }

    private async Task<T> GetRequiredAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(uri, cancellationToken).ConfigureAwait(false);

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);

        return payload ??
               throw new QuickProxyClientException(response.StatusCode, "QuickProxy returned an empty JSON response.");
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return response;

        var error = await TryReadErrorAsync(response, cancellationToken).ConfigureAwait(false);

        response.Dispose();

        throw new QuickProxyClientException(response.StatusCode, error);
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<QuickProxyApiError>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (payload is null)
                return $"QuickProxy request failed with status {(int)response.StatusCode}.";

            if (payload.Details is { Count: > 0 })
                return $"{payload.Message ?? "QuickProxy request failed."} {string.Join("; ", payload.Details)}";

            return payload.Message ?? $"QuickProxy request failed with status {(int)response.StatusCode}.";
        }
        catch
        {
            return $"QuickProxy request failed with status {(int)response.StatusCode}.";
        }
    }

    private static Uri BuildConfigUri(string? prefix, bool decrypt)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(prefix))
            query.Add($"prefix={Uri.EscapeDataString(prefix)}");

        if (decrypt)
            query.Add("decrypt");

        return CreateRelativeUri($"/api/config{ToQueryString(query)}");
    }

    private static Uri BuildConfigUri(string key, bool raw, bool recurse, bool decrypt, bool template = false)
    {
        var query = new List<string>();

        if (raw)
            query.Add("raw");

        if (recurse)
            query.Add("recurse");

        if (decrypt)
            query.Add("decrypt");

        if (template)
            query.Add("template");

        return CreateRelativeUri($"/api/config/{EncodePathKey(key)}{ToQueryString(query)}");
    }

    private static Uri BuildConfigExportUri(string? prefix, bool decrypt)
    {
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(prefix))
            query.Add($"prefix={Uri.EscapeDataString(prefix)}");

        if (decrypt)
            query.Add("decrypt");

        return CreateRelativeUri($"/api/config-export{ToQueryString(query)}");
    }

    private static Uri BuildConfigExportUri(string key, bool recurse, bool decrypt)
    {
        var query = new List<string>();

        if (recurse)
            query.Add("recurse");

        if (decrypt)
            query.Add("decrypt");

        return CreateRelativeUri($"/api/config-export/{EncodePathKey(key)}{ToQueryString(query)}");
    }

    private static Uri BuildDevelopmentCertificateUri()
    {
        return CreateRelativeUri("/api/certificates/development");
    }

    private static Uri NormalizeBaseUri(Uri baseUri)
    {
        var builder = new UriBuilder(baseUri);
        var path = builder.Path ?? "/";

        if (!path.EndsWith("/", StringComparison.Ordinal))
            builder.Path = $"{path}/";

        return builder.Uri;
    }

    private static Uri CreateRelativeUri(string relative)
    {
        return new Uri(relative, UriKind.Relative);
    }

    private static string EncodePathKey(string key)
    {
        return string.Join("/",
            key.Trim('/')
                .Split(['/'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static segment => segment.Trim())
                .Where(static segment => segment.Length > 0)
                .Select(Uri.EscapeDataString));
    }


    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }

    private static bool IsTrustedQuickProxyFallbackCertificate(X509Certificate2? certificate,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
            return false;

        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        const SslPolicyErrors allowedErrors = SslPolicyErrors.RemoteCertificateChainErrors |
                                              SslPolicyErrors.RemoteCertificateNameMismatch;

        if ((sslPolicyErrors & ~allowedErrors) != 0)
            return false;

        var now = DateTimeOffset.UtcNow;

        if (now < certificate.NotBefore || now > certificate.NotAfter)
            return false;

        if (!string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase))
            return false;

        if (certificate.Extensions.OfType<X509Extension>().Any(static extension =>
                string.Equals(extension.Oid?.Value, QuickProxyFallbackCertificateOid, StringComparison.Ordinal)))
            return true;

        return IsLegacyQuickProxyFallbackCertificate(certificate);
    }

    private static bool IsLegacyQuickProxyFallbackCertificate(X509Certificate2 certificate)
    {
        if (!string.Equals(certificate.Subject, LegacyQuickProxyFallbackSubject, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!HasServerAuthenticationUsage(certificate))
            return false;

        return HasSubjectAlternativeName(certificate, "localhost");
    }

    private static bool HasServerAuthenticationUsage(X509Certificate2 certificate)
    {
        return certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .Where(static extension =>
                string.Equals(extension.Oid?.Value, EnhancedKeyUsageOid, StringComparison.Ordinal))
            .SelectMany(static extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(static oid => string.Equals(oid.Value, ServerAuthenticationOid, StringComparison.Ordinal));
    }

    private static bool HasSubjectAlternativeName(X509Certificate2 certificate, string expectedDnsName)
    {
        return certificate.Extensions
            .OfType<X509Extension>()
            .Where(static extension =>
                string.Equals(extension.Oid?.Value, SubjectAlternativeNameOid, StringComparison.Ordinal))
            .Select(static extension => extension.Format(true))
            .Any(formatted =>
                formatted.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(entry =>
                    {
                        const string dnsPrefix = "DNS Name=";
                        var trimmedEntry = entry.Trim();
                        return trimmedEntry.StartsWith(dnsPrefix, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(trimmedEntry.Substring(dnsPrefix.Length), expectedDnsName,
                                   StringComparison.OrdinalIgnoreCase);
                    }));
    }

    private static X509Certificate2? TryConvertCertificate(X509Certificate? certificate)
    {
        try
        {
            return certificate is null ? null : new X509Certificate2(certificate);
        }
        catch
        {
            return null;
        }
    }

    private sealed class QuickProxyApiError
    {
        public string? Message { get; set; }
        public List<string>? Details { get; set; }
    }
}
