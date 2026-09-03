# QuickProxy SDK

`QuickProxy.Sdk` is a .NET client for the QuickProxy public config API.

When you construct the client with `new QuickProxyClient(Uri)`, it automatically accepts QuickProxy-generated fallback admin certificates used by other QuickProxy instances. It does not disable TLS validation for unrelated certificates. If you pass your own `HttpClient`, certificate handling stays entirely under your control.

## Features

- list metadata from `/api/config`
- fetch metadata for a single key
- fetch raw text or binary values
- fetch export payloads from `/api/config-export`
- fetch the fallback development certificate from `/api/certificates/development`
- optional `decrypt` support
- optional raw text `{kv.*}` templating support

## Example

```csharp
using QuickProxy.Sdk;

var client = new QuickProxyClient(new Uri("https://quickproxy.example.com/"));

var metadata = await client.GetMetadataAsync("shared/base-domain");
var textValue = await client.GetRawTextAsync("shared/base-domain");
var secretValue = await client.GetRawTextAsync("shared/db-password", decrypt: true);
var certificateBytes = await client.GetRawBytesAsync("certificates/client-cert.pfx", decrypt: true);
var developmentCertificateBytes = await client.GetDevelopmentCertificateAsync();
```
