# Public API and SDK

QuickProxy exposes a public read API on the internal listener and ships a .NET SDK for consuming it.

## Availability

- all public API endpoints are served on the internal listener only
- base path: `/api`

## Public config API

The public config API exposes Key/Value data for runtime consumers.

Endpoints:

- `GET /api/config`
- `GET /api/config?prefix=app/settings`
- `GET /api/config/{key}`
- `GET /api/config/{key}?raw`
- `GET /api/config/{path}?recurse`
- `GET /api/config-export`
- `GET /api/config-export?prefix=app/settings`
- `GET /api/config-export/{key}`
- `GET /api/config-export/{path}?recurse`

Behavior:

- `/api/config` returns metadata-oriented JSON
- `/api/config-export` returns full value/export JSON
- `?raw` returns plain text for text entries and raw bytes for binary entries
- `?recurse` returns matching descendants for a path prefix
- `?decrypt` is a presence flag
- `?template` applies `{kv.some/path}` replacement for raw text reads
- `?raw` and `?recurse` cannot be combined

Examples:

```text
/api/config/app/settings.json
/api/config/app/settings.json?raw
/api/config/templates/nginx.conf?raw&template
/api/config/certificates/client-cert.pfx?raw&decrypt
/api/config-export/app/settings.json
```

## Public certificate API

QuickProxy exposes its generated, database-backed development certificate.

Endpoint:

- `GET /api/certificates/development`

Behavior:

- returns the generated certificate as `application/x-pkcs12`
- the certificate password is `dev`

## QuickProxy SDK

`QuickProxy.Sdk` is a .NET client for the public API.

It supports:

- listing config metadata
- reading raw text and binary config values
- reading config export payloads
- fetching the fallback development certificate

Example:

```csharp
using QuickProxy.Sdk;

var client = new QuickProxyClient(new Uri("https://quickproxy.example.com/"));

var metadata = await client.GetMetadataAsync("shared/base-domain");
var textValue = await client.GetRawTextAsync("shared/base-domain");
var secretValue = await client.GetRawTextAsync("shared/db-password", decrypt: true);
var certificateBytes = await client.GetRawBytesAsync("certificates/client-cert.pfx", decrypt: true);
var developmentCertificateBytes = await client.GetDevelopmentCertificateAsync();
```

Common SDK methods:

- `ListMetadataAsync`
- `GetMetadataAsync`
- `RecurseMetadataAsync`
- `ListEntriesAsync`
- `GetEntryAsync`
- `RecurseEntriesAsync`
- `GetRawTextAsync`
- `GetRawBytesAsync`
- `GetDevelopmentCertificateAsync`
