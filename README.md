# QuickProxy Configuration Reference

This document describes runtime configuration in `src/QuickProxy/appsettings.json` and database storage options.

## Development

QuickProxy follows the same repository structure as Secure Package Gateway:

- `.config/` contains repository-local .NET tool manifests.
- `src/` contains the server, shared library, admin UI, and public SDK.
- `documentation/` contains the VitePress site.

Clone the repository together with its Aditify dependency:

```sh
git clone --recurse-submodules https://github.com/aditi-ab/quickproxy.git
```

For an existing clone, run `git submodule update --init --recursive` before installing dependencies. Supply `Config__Secrets__EncryptionKey` through an environment variable or another local secret store before using encrypted configuration.

Build the solution from the QuickProxy directory:

```powershell
yarn build
dotnet build .\QuickProxy.slnx
```

`yarn build` builds the administration application and documentation into the QuickProxy host's `wwwroot` directory. The .NET build does not invoke Yarn.

Build the documentation independently:

```powershell
Set-Location .\documentation
yarn docs:build
```

## Docker

Build the Linux image from the QuickProxy directory:

```bash
docker build -t quickproxy:local --build-arg APP_VERSION=0.9.0-local --build-arg APP_ASSEMBLY_VERSION=0.9.0.0 .
```

GitHub Actions publishes the Linux image as `ghcr.io/aditi-ab/quickproxy` when the workflow is dispatched manually. Each workflow run calculates `0.9.<run-number>` and stamps it into the .NET assembly and OCI image metadata. Ordinary pushes and pull requests build and validate without publishing. A manual run publishes the numeric version, `master`, commit SHA, and `latest` tags.

Publish a Windows image directly with the .NET SDK container support when a Windows image is required:

```powershell
dotnet publish src/QuickProxy/QuickProxy.csproj -c Release -r win-x64 /t:PublishContainer -p:ContainerRuntimeIdentifier=win-x64 -p:ContainerRepository=quickproxy
```

Run the Linux container with persistent storage and Docker socket access:

```bash
docker run \
  --pull=always \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  -p 18080:80 \
  -p 18443:443 \
  -p 9000:9000 \
  -e Config__Secrets__EncryptionKey="$QUICKPROXY_CONFIG_ENCRYPTION_KEY" \
  -v quickproxy-data:/app/Data \
  -v /var/run/docker.sock:/var/run/docker.sock \
  quickproxy:local
```

Run the published Linux image on Windows with a bind mount for `Data/`:

```powershell
docker run -d `
  --pull=always `
  --name quickproxy `
  -p 9000:9000 `
  -p 18080:80 `
  -p 18443:443 `
  -e "TemplateValues__ComputerName=$env:COMPUTERNAME" `
  -v "D:\ContainerData\QuickProxy\Data:/app/Data" `
  -v "/var/run/docker.sock:/var/run/docker.sock" `
  ghcr.io/aditi-ab/quickproxy:latest
```

Run the published Windows image with a bind mount for `Data/`:

```powershell
docker run -d `
  --pull=always `
  --name quickproxy `
  -p 9000:9000 `
  -p 18080:80 `
  -p 18443:443 `
  -e "TemplateValues__ComputerName=$env:COMPUTERNAME" `
  -v "D:\ContainerData\QuickProxy\Data:C:\app\Data" `
  -v "\\.\pipe\docker_engine:\\.\pipe\docker_engine" `
  aditiab/quickproxy:windows-latest
```

Set `TemplateValues:ComputerName` from PowerShell via environment variable:

```powershell
-e "TemplateValues__ComputerName=$env:COMPUTERNAME"
```

ASP.NET configuration maps double underscores to nested settings, so this overrides:

```json
"TemplateValues": {
  "ComputerName": ""
}
```

Linux container defaults:

- `Listen__HttpPort=80`
- `Listen__HttpsPort=443`
- `Listen__InternalPort=9000`
- `Listen__AdminAccess=any`
- `Containers__Endpoint=unix:///var/run/docker.sock`
- proxy, configuration, and audit storage use SQLite below `/app/Data`
- `/app/Data` is declared as a persistent volume

Windows container defaults:

- `Listen__HttpPort=80`
- `Listen__HttpsPort=443`
- `Listen__InternalPort=9000`
- `Listen__AdminAccess=any`
- `Containers__Endpoint=npipe://./pipe/docker_engine`
- `C:\app\Data` is declared as a volume for SQLite databases

The Linux Docker build:

- runs `yarn install --frozen-lockfile` inside the Docker build
- builds the admin UI inside the Docker build
- publishes `Quick.Proxy` for the target runtime (`linux-x64` or `win-x64`)
- stamps the assembly/image version from `APP_VERSION`

## QuickProxy SDK NuGet package

The repo also contains a packable .NET SDK project at `src/QuickProxy.Sdk/QuickProxy.Sdk.csproj`.

Default package versioning is automatic per build:

- package version: `0.9.0-YYYYMMDDHHmm`
- assembly/file version: `0.9.0.0`

Build a package locally:

```powershell
dotnet pack .\src\QuickProxy.Sdk\QuickProxy.Sdk.csproj -c Release -o .\.artifacts\nuget
```

Push the latest built package to NuGet.org:

```powershell
$env:NUGET_API_KEY = "<your-api-key>"
.\push-quickproxy-sdk.ps1
```

Maintainers publish the SDK through the repository workflow. Registry identities and publishing policy belong in protected repository and registry settings.

Override the package version explicitly when needed:

```powershell
.\push-quickproxy-sdk.ps1 -Version "0.9.0-preview.1"
```

Troubleshooting:

- If the footer shows `v0.1.0-dev`, the running image was built without `APP_VERSION` or you are running an older image that predates the pipeline version stamping. Pull the latest tag explicitly before running:

```bash
docker pull aditiab/quickproxy:linux-latest
```

- If `Containers` shows `Connection failed` inside the Linux image, mount `/var/run/docker.sock` into the container. Do not mount the Windows named pipe into the Linux image. The named pipe mount is only for the Windows container image.

## License

QuickProxy is licensed under the [Apache License 2.0](LICENSE).

## Container labels used by QuickProxy

QuickProxy supports a namespaced container label for marking infrastructure/system containers:

```text
quickproxy.role=system
```

Behavior:

- system containers are hidden by default on the `Containers` page
- the UI exposes a `Show system containers` toggle to reveal them
- use this for containers such as the `quickproxy` container itself or other infrastructure components you do not want mixed into normal workload views

## Provisioning

QuickProxy can provision automatic proxy host templates and certificates from one external JSON document at startup.

Configure exactly one source in `src/QuickProxy/appsettings.json`:

```json
"Provisioning": {
  "Enabled": true,
  "FilePath": "Data/provisioning.json",
  "Url": "",
  "TimeoutSeconds": 30
}
```

or:

```json
"Provisioning": {
  "Enabled": true,
  "FilePath": "",
  "Url": "https://config.example.local/quickproxy/provisioning",
  "TimeoutSeconds": 30
}
```

Behavior:

- provisioning runs once after the app has started listening
- existing IDs are preserved; provisioning only creates missing entries
- imported entries become normal editable stored records
- provisioning failures are logged but do not stop app startup
- automatic container domain templates support `{container.name}`, `{computer.name}`, and `{label.some-label-key}`
- label placeholder values are inserted as-is, so they should already be valid hostname fragments
- provisioning string values support `{computer.name}` template expansion before import

Provisioning JSON shape:

```json
{
  "automaticTemplates": [
    {
      "id": "auto-webdesk",
      "mode": "automaticContainer",
      "enabled": true,
      "domainNames": [],
      "automaticContainer": {
        "labelSelectors": [
          {
            "key": "app",
            "valuePatterns": ["webdesk"]
          }
        ],
        "domainTemplates": ["{label.app}.{computer.name}.example.local"]
      },
      "forceSsl": false,
      "cacheAssets": false,
      "websockets": true,
      "certificateId": "internal-cert",
      "routes": [
        {
          "path": "/",
          "rewriteMode": "preserve",
          "upstreamMode": "container",
          "upstream": {
            "scheme": "http",
            "host": "",
            "port": 80
          },
          "container": {
            "containerName": "",
            "scheme": "http",
            "port": 8080,
            "portResolutionMode": "published",
            "networkName": null
          }
        }
      ],
      "tls": {
        "mode": "none"
      }
    }
  ],
  "certificates": [
    {
      "id": "internal-cert",
      "mode": "pfx",
      "pfxPassword": "changeit",
      "pfxPasswordEnvVar": "",
      "thumbprint": "",
      "storeName": "My",
      "storeLocation": "LocalMachine",
      "files": {
        "pfxBase64": "<base64-pfx-bytes>"
      }
    }
  ]
}
```

Supported certificate file payload fields:

- `certificatePemBase64`
- `keyPemBase64`
- `intermediatePemBase64`
- `pfxBase64`
- `caCertificatePemBase64`
- `caKeyPemBase64`
- `caPfxBase64`

Issuer certificate provisioning (`mode: "issuer"`) supports:

- `issuerMatchDomains` (apex + subdomain matching)
- `issuerEnabled`
- `issuerCaSource`: `uploadPem` | `uploadPfx` | `pathPem` | `pathPfx` | `storeThumbprint`
- For `pathPem`: `caCertificatePath`, `caPrivateKeyPath`
- For `pathPfx`: `caPfxPath`, `caPfxPassword`, `caPfxPasswordEnvVar`
- For `storeThumbprint`: `caStoreThumbprint`, optional `caStoreName` (default `My`) and `caStoreLocation` (default `LocalMachine`)

## App Settings

`src/QuickProxy/appsettings.json` currently contains these sections:

```json
{
  "Logging": { ... },
  "Listen": { ... },
  "Proxy": { ... },
  "Config": { ... },
  "Audit": { ... },
  "Containers": { ... },
  "AllowedHosts": "*"
}
```

## Logging

`Logging.LogLevel` controls ASP.NET/Core logging levels.

- `Default`: global minimum level.
- `Microsoft.AspNetCore`: framework category level.

## Listen

Controls proxy and internal listener behavior.

- `HttpPort` (`int`): HTTP proxy listener port.
- `HttpsPort` (`int`): HTTPS proxy listener port.
- `InternalPort` (`int`): Internal listener port for all `/api/...` endpoints and the admin UI.
- `AdminAccess` (`string`):
  - `localhost`: admin UI and admin API are accessible only from localhost.
  - `any`: admin UI and admin API are accessible from any interface.

Notes:

- `HttpPort`, `HttpsPort`, and `InternalPort` must be unique.
- Proxy traffic is served only on `HttpPort` and `HttpsPort`.
- All `/api/...` endpoints are served only on `InternalPort`.
- Admin UI is served only on `InternalPort`.

## Storage

Selects the database used by each module.

- `Provider` (`string`):
  - `sqlite` (default)
  - `sqlserver`
- `ConnectionString` (`string`): connection string for the selected database provider.

Behavior:

- Proxy, Config, and Audit each have independent database settings.
- SQLite stores each module in its configured database file. SQL Server stores it in the configured database.
- Proxy database records include proxy configuration, certificate payloads, administrator identities, generated fallback certificates, and data-protection keys.
- config values are persisted in encoded form in storage.
- admin APIs return decoded plain-text config values.

## Public Config API

The public config API is exposed on the internal listener only under `/api/config`.

- `GET /api/config`
- `GET /api/config?prefix=app/settings`
- `GET /api/config/{key}`
- `GET /api/config/{key}?raw`
- `GET /api/config/{path}?recurse`

Behavior:

- default responses return JSON entries with `value` encoded as base64
- `?raw` returns only the decoded value as a plain text response body
- `?recurse` returns all entries under the requested path, similar to Consul KV recursion
- `?raw` and `?recurse` cannot be combined

## Public Certificate API

The database-backed development certificate is exposed on the internal listener under `/api/certificates/development`.

- `GET /api/certificates/development`

Behavior:

- returns the generated database-backed certificate as `application/x-pkcs12`
- the bundled fallback password remains `dev`

## First administrator

When the identity store is empty, the administration sign-in page displays a first-use form. Enter a username and a password containing at least 12 characters with upper-case, lower-case, number, and symbol characters. QuickProxy creates and signs in the first local administrator, then permanently disables the bootstrap endpoint for that identity store.

## AuthProviders

External credential verification options used at login.

Login flow for an existing enabled local user:

1. Verify local password hash.
2. If local hash fails, try enabled external providers.

### AuthProviders.Ldap

- `Enabled` (`bool`)
- `Server` (`string`): LDAP host.
- `Port` (`int`): default `389`.
- `UseSsl` (`bool`): LDAPS toggle.
- `Domain` (`string`): used with username fallback.
- `BindIdentityPattern` (`string`): optional pattern with placeholders:
  - `{email}`
  - `{username}`

Bind identity resolution:

1. `BindIdentityPattern` if provided.
2. otherwise `username@Domain` if `Domain` is set.
3. otherwise raw email.

### AuthProviders.Entra

- `Enabled` (`bool`)
- `TenantId` (`string`)
- `ClientId` (`string`)
- `ClientSecret` (`string`, optional depending on app config)
- `Scope` (`string`): default `openid profile email`.

Note: Entra validation uses OAuth token endpoint with password grant style credentials. Tenant/app policy must allow this flow.

## AllowedHosts

Standard ASP.NET host filtering setting.

---

## Data Storage

Default SQLite paths are `Data/quickproxy.db`, `Data/quickconfig.db`, and `Data/quickaudit.db`.

The Proxy database includes:

- `ProxyHosts`
- `DomainTranslations`
- `FallbackSettings`
- `ContainerDefaultsSettings`
- `ComposeProjectsSettings`
- `CertificateConfigs`
- `CertificateFiles`
- `Users`
- `AuthProviderConfigs`
- `AdminIdentityUsers`
- `AdminIdentityProviders`
- `ApplicationData`
- `DataProtectionKeys`

The Config database includes `ConfigEntries` and `ConfigEntryRevisions`. The Audit database includes `AuditEvents`.

---

## Configuration Structures

## Proxy Host

```json
{
  "id": "docs-host",
  "enabled": true,
  "domainNames": ["docs.example.com"],
  "forceSsl": true,
  "cacheAssets": true,
  "websockets": true,
  "certificateId": "docs-cert",
  "upstream": {
    "scheme": "http",
    "host": "127.0.0.1",
    "port": 8080
  },
  "locations": [
    {
      "path": "/api",
      "upstream": {
        "scheme": "http",
        "host": "127.0.0.1",
        "port": 9000
      }
    }
  ],
  "tls": {
    "mode": "none",
    "pfxPath": null,
    "pfxPassword": null,
    "pfxPasswordEnvVar": null,
    "thumbprint": null,
    "storeName": "My",
    "storeLocation": "LocalMachine"
  }
}
```

Field meaning:

- `id`: host identifier.
- `enabled`: include/exclude host from active routing.
- `domainNames`: exact host matches.
- `forceSsl`: HTTP requests redirected to HTTPS.
- `cacheAssets`: apply long cache headers for common static asset extensions.
- `websockets`: allow/deny websocket upgrade requests.
- `certificateId`: preferred certificate config reference.
- `upstream`: default target when no location override matches.
- `locations`: path-prefix specific upstream overrides.
- `tls`: legacy per-host TLS settings (`none|pfx|thumbprint`); `certificateId` is preferred.

## Fallback Settings

```json
{
  "enabled": true,
  "statusCode": 404,
  "mode": "htmlFile",
  "htmlFilePath": "ClientFallback/not-found.html",
  "redirectUrl": "",
  "contentType": "text/html; charset=utf-8"
}
```

Field meaning:

- `enabled`: enable unknown-host fallback behavior.
- `statusCode`: response status.
- `mode`:
  - `statusCode`
  - `htmlFile`
  - `redirect`
- `htmlFilePath`: required for `htmlFile` mode.
- `redirectUrl`: required absolute URL for `redirect` mode.
- `contentType`: content type used for response body modes.

## Certificate Config

```json
{
  "id": "docs-cert",
  "mode": "files",
  "pfxPassword": "",
  "pfxPasswordEnvVar": "",
  "thumbprint": "",
  "storeName": "My",
  "storeLocation": "LocalMachine",
  "hasCertificateFile": false,
  "hasKeyFile": false,
  "hasIntermediateFile": false,
  "hasPfxFile": false,
  "domainNames": [],
  "provider": "",
  "expiresAtUtc": null,
  "inUse": false,
  "inUseCount": 0
}
```

Field meaning:

- `id`: certificate identifier.
- `mode`:
  - `files` (PEM/CRT + key files)
  - `pfx`
  - `thumbprint` (Windows cert store)
- `pfxPassword` / `pfxPasswordEnvVar`: used for `pfx` mode.
- `thumbprint` / `storeName` / `storeLocation`: used for thumbprint mode.
- `has*`, `domainNames`, `provider`, `expiresAtUtc`, `inUse`, `inUseCount` are metadata maintained/returned by API.

## Users

```json
{
  "users": [
    {
      "email": "admin@example.com",
      "fullName": "Administrator",
      "enabled": true,
      "passwordHash": "pbkdf2-sha256$100000$...$..."
    }
  ]
}
```

Field meaning:

- `email`: unique user key.
- `fullName`: optional display name.
- `enabled`: login allowed/blocked.
- `passwordHash`: PBKDF2-SHA256 stored hash, never plain password.

---

## Defaults Summary

- Storage provider defaults to SQLite.
- Internal port defaults to `9000`.
- Admin access defaults to `localhost`.
- Unknown-host fallback defaults to HTML file mode with `ClientFallback/not-found.html`.
- An empty identity store requires interactive first-administrator setup in the administration UI.
