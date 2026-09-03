# Provisioning Schema

## Root

```json
{
  "authProviders": [],
  "domainTranslations": [],
  "proxyHosts": [],
  "automaticTemplates": [],
  "containerDefaultSets": [],
  "certificates": []
}
```

## `authProviders[]`

Each entry:

- `id`
- `displayName`
- `enabled`
- `allowAutoAccess`
- `type`
- `ldap` or `oidc` settings

Supported `type` values:

- `ldap`
- `oidc`

LDAP fields:

- `server`
- `port`
- `useSsl`
- `bindDn`
- `bindPassword`
- `baseDn`
- `userFilter`
- `emailAttribute`
- `fullNameAttribute`

OIDC fields:

- `metadataUrl`
- `clientId`
- `clientSecret`
- `scopes`
- `emailClaim`
- `nameClaim`
- `subjectClaim`
- `usePkce`

Notes:

- startup provisioning skips existing IDs
- dashboard `Re-provision` overwrites existing IDs
- plaintext provisioning secrets are encrypted before storage
- `id` must be lowercase kebab-case
- OIDC providers appear as login buttons
- LDAP providers participate in password login
- for OIDC, `metadataUrl` is the discovery endpoint, usually ending in `/.well-known/openid-configuration`

## `domainTranslations[]`

Each entry:

- `id`
- `enabled`
- `sourceDomain`
- `targetDomain`
- `certificateId` optional
- `rewriteHostHeader`

Notes:

- one rule matches the apex domain and all subdomains under `sourceDomain`
- the target host is built by replacing the matched source suffix with `targetDomain`
- path, query string, and incoming port are preserved
- startup provisioning skips existing IDs
- dashboard `Re-provision` overwrites existing IDs
- enabled rules are checked for duplicate `sourceDomain` values against existing stored rules and other provisioned rules in the same run
- `certificateId`, when set, uses the normal certificate system
- if `certificateId` points to an issuer config, the issuer flow applies to the source domain, not the translated target domain

Example:

- `sourceDomain = example.com`
- `targetDomain = dev.localhost`

matches:

- `example.com`
- `api.example.com`
- `foo.bar.example.com`

and translates them to:

- `dev.localhost`
- `api.dev.localhost`
- `foo.bar.dev.localhost`

## `proxyHosts[]`

Uses the proxy-host fields described in [Proxy Hosts](/ui/proxy-hosts), with this requirement:

- `mode` must be `manual`

Notes:

- the same validation rules apply as in the admin UI
- enabled manual hosts are checked for duplicate `domainNames` against existing stored manual hosts and other provisioned manual hosts in the same run
- on startup provisioning, existing IDs are skipped
- on dashboard `Re-provision`, existing IDs are overwritten

## `automaticTemplates[]`

Uses the proxy-host fields described in [Proxy Hosts](/ui/proxy-hosts), with this requirement:

- `mode` must be `automaticContainer`

Notes:

- startup provisioning skips existing IDs
- dashboard `Re-provision` overwrites existing IDs

## `certificates[]`

Base fields:

- `id`
- `mode`
- mode-specific fields

Supported `mode` values:

- `files`
- `pfx`
- `thumbprint`
- `issuer`

## Certificate file payload keys

- `certificatePemBase64`
- `keyPemBase64`
- `intermediatePemBase64`
- `pfxBase64`
- `caCertificatePemBase64`
- `caKeyPemBase64`
- `caPfxBase64`

## Issuer mode fields

Required/important:

- `issuerMatchDomains`
- `issuerEnabled`
- `issuerCaSource`

`issuerCaSource` values:

- `uploadPem`
- `uploadPfx`
- `pathPem`
- `pathPfx`
- `storeThumbprint`

Mode-specific:

- `pathPem`: `caCertificatePath`, `caPrivateKeyPath`
- `pathPfx`: `caPfxPath`, optional `caPfxPassword`, `caPfxPasswordEnvVar`
- `storeThumbprint`: `caStoreThumbprint`, optional `caStoreName`, `caStoreLocation`
- `uploadPem`: `files.caCertificatePemBase64`, `files.caKeyPemBase64`
- `uploadPfx`: `files.caPfxBase64`, optional PFX password fields

Notes:

- For path-based sources, relative paths resolve from the QuickProxy application directory.
- For PFX sources, password can be provided directly (`caPfxPassword`) or via environment variable (`caPfxPasswordEnvVar`).
- Keep `issuerCaSource` explicit to avoid ambiguous validation when multiple source hints are present.

## `containerDefaultSets[]`

Each entry:

- `id` (required, non-empty)
- `labels[]` (key/value list, optional)
- `envVars[]` (key/value list, optional)
- `mountBindings[]` (list, optional)
- `networkAliases[]` (list, optional)
- `hostMappings[]` (list, optional)
- each mount binding: `hostPath`, `containerPath`, `readOnly`
- each host mapping: `hostname`, `address`
- each network alias: `network`, `alias`

Validation:

- `labels` and `envVars` keys must be non-empty
- mount `hostPath` and `containerPath` must be non-empty
- network alias `network` and `alias` must be non-empty
- host mapping `hostname` and `address` must be non-empty
- duplicate mount `containerPath` values are not allowed (case-insensitive)
- duplicate keys are not allowed (case-insensitive)
- duplicate host mapping hostnames are not allowed (case-insensitive)
- duplicate network/alias pairs are not allowed (case-insensitive)
- `labels` keys starting with `quickproxy.internal.` are reserved and rejected

Usage:

- defaults are applied when a container has label `quickproxy.defaults=<id>`
- defaults only fill missing keys/bindings/aliases (existing container label/env/mount/host-mapping/network-alias values win)
- placeholders from `TemplateValues` are supported in string values (for example `{server.name}` and `{server.ip}`)
- special cases: `SERVERNAME` maps to `{server.name}`, `SERVERIP` maps to `{server.ip}`
