# Provisioning Examples

## 1) LDAP auth provider

```json
{
  "authProviders": [
    {
      "id": "corp-ldap",
      "displayName": "Corporate LDAP",
      "enabled": true,
      "allowAutoAccess": true,
      "type": "ldap",
      "ldap": {
        "server": "ldap.corp.local",
        "port": 636,
        "useSsl": true,
        "bindDn": "CN=svc-quickproxy,OU=Service Accounts,DC=corp,DC=local",
        "bindPassword": "super-secret-password",
        "baseDn": "OU=Users,DC=corp,DC=local",
        "userFilter": "(&(objectClass=user)(mail={email}))",
        "emailAttribute": "mail",
        "fullNameAttribute": "displayName"
      },
      "oidc": {}
    }
  ]
}
```

## 2) OIDC / Entra auth provider

```json
{
  "authProviders": [
    {
      "id": "entra",
      "displayName": "Microsoft Entra ID",
      "enabled": true,
      "allowAutoAccess": true,
      "type": "oidc",
      "ldap": {},
      "oidc": {
        "metadataUrl": "https://login.microsoftonline.com/<tenant-id>/v2.0/.well-known/openid-configuration",
        "clientId": "<client-id>",
        "clientSecret": "<client-secret>",
        "scopes": "openid profile email",
        "emailClaim": "email",
        "nameClaim": "name",
        "subjectClaim": "sub",
        "usePkce": true
      }
    }
  ]
}
```

`metadataUrl` is the OIDC discovery endpoint. In the admin UI this is shown as `Discovery Endpoint`.

## 3) Domain translation

```json
{
  "domainTranslations": [
    {
      "id": "example-to-dev",
      "enabled": true,
      "sourceDomain": "example.com",
      "targetDomain": "dev.localhost",
      "certificateId": "corp-issuer",
      "rewriteHostHeader": true
    }
  ]
}
```

This translates:

- `example.com` -> `dev.localhost`
- `api.example.com` -> `api.dev.localhost`
- `foo.bar.example.com:8443` -> `foo.bar.dev.localhost:8443`

The source-side certificate still belongs to `example.com` / `*.example.com`. If `certificateId` points to an issuer, QuickProxy issues/reuses the certificate on the source side before proxying to the translated backend host.

## 4) Domain translation with provisioning templates

```json
{
  "domainTranslations": [
    {
      "id": "apps-to-dev",
      "enabled": true,
      "sourceDomain": "apps.{server.name}.example.com",
      "targetDomain": "dev.{server.name}.localhost",
      "certificateId": "corp-issuer",
      "rewriteHostHeader": true
    }
  ]
}
```

Provisioning expands templates before the document is imported. For example, if:

```text
{server.name} = stockholm-1
```

then the stored rule becomes:

```text
sourceDomain = apps.stockholm-1.example.com
targetDomain = dev.stockholm-1.localhost
```

This templating support is part of provisioning import. Stored domain translation entries in the admin UI remain literal values after import.

## 5) Manual proxy host

```json
{
  "proxyHosts": [
    {
      "id": "portal",
      "mode": "manual",
      "enabled": true,
      "domainNames": ["portal.example.local"],
      "forceSsl": true,
      "websockets": true,
      "certificateId": "internal-cert",
      "routes": [
        {
          "path": "/",
          "rewriteMode": "preserve",
          "upstreamMode": "manual",
          "upstream": {
            "scheme": "http",
            "host": "portal-app",
            "port": 8080
          },
          "container": {
            "containerName": "",
            "scheme": "http",
            "port": 80,
            "portResolutionMode": "published",
            "networkName": null
          }
        }
      ],
      "tls": { "mode": "none" }
    }
  ]
}
```

## 6) Automatic host template + PFX cert

```json
{
  "automaticTemplates": [
    {
      "id": "auto-web",
      "mode": "automaticContainer",
      "enabled": true,
      "automaticContainer": {
        "labelSelectors": [
          { "key": "app", "valuePatterns": ["web"] }
        ],
        "domainTemplates": ["{label.app}.{server.name}.example.local"]
      },
      "routes": [
        {
          "path": "/",
          "rewriteMode": "preserve",
          "upstreamMode": "container",
          "upstream": { "scheme": "http", "host": "", "port": 80 },
          "container": {
            "containerName": "",
            "scheme": "http",
            "port": 8080,
            "portResolutionMode": "published",
            "networkName": null
          }
        }
      ],
      "tls": { "mode": "none" }
    }
  ],
  "certificates": [
    {
      "id": "internal-cert",
      "mode": "pfx",
      "pfxPassword": "changeit",
      "files": { "pfxBase64": "<base64-pfx-bytes>" }
    }
  ]
}
```

## 7) Issuer from PEM file paths

```json
{
  "certificates": [
    {
      "id": "corp-ca",
      "mode": "issuer",
      "issuerEnabled": true,
      "issuerMatchDomains": ["example.com"],
      "issuerCaSource": "pathPem",
      "caCertificatePath": "Data/Certificates/CA/{server.name}/ca-certificate.pem",
      "caPrivateKeyPath": "Data/Certificates/CA/{server.name}/ca-key.pem",
      "files": {}
    }
  ]
}
```

## 8) Issuer from PFX file path

```json
{
  "certificates": [
    {
      "id": "corp-pfx-ca",
      "mode": "issuer",
      "issuerEnabled": true,
      "issuerMatchDomains": ["example.com"],
      "issuerCaSource": "pathPfx",
      "caPfxPath": "Data/Certificates/CA/{server.name}/ca.pfx",
      "caPfxPasswordEnvVar": "QUICKPROXY_CA_PFX_PASSWORD",
      "files": {}
    }
  ]
}
```

## 9) Issuer from Windows certificate store thumbprint

```json
{
  "certificates": [
    {
      "id": "corp-store-ca",
      "mode": "issuer",
      "issuerEnabled": true,
      "issuerMatchDomains": ["corp.local"],
      "issuerCaSource": "storeThumbprint",
      "caStoreThumbprint": "AABBCCDDEEFF11223344556677889900AABBCCDD",
      "caStoreName": "Root",
      "caStoreLocation": "LocalMachine",
      "files": {}
    }
  ]
}
```

## 10) Issuer from uploaded base64 PEM

```json
{
  "certificates": [
    {
      "id": "upload-ca",
      "mode": "issuer",
      "issuerEnabled": true,
      "issuerMatchDomains": ["internal.example"],
      "issuerCaSource": "uploadPem",
      "files": {
        "caCertificatePemBase64": "<base64-ca-cert-pem>",
        "caKeyPemBase64": "<base64-ca-key-pem>"
      }
    }
  ]
}
```

## 11) Issuer from uploaded base64 PFX

```json
{
  "certificates": [
    {
      "id": "upload-pfx-ca",
      "mode": "issuer",
      "issuerEnabled": true,
      "issuerMatchDomains": ["internal.example"],
      "issuerCaSource": "uploadPfx",
      "caPfxPassword": "changeit",
      "files": {
        "caPfxBase64": "<base64-ca-pfx>"
      }
    }
  ]
}
```

## 12) Provision container default sets

```json
{
  "containerDefaultSets": [
    {
      "id": "web-base",
      "labels": [
        { "key": "traefik.enable", "value": "true" },
        { "key": "ops.owner", "value": "platform" }
      ],
      "envVars": [
        { "key": "ASPNETCORE_ENVIRONMENT", "value": "Production" },
        { "key": "TZ", "value": "UTC" },
        { "key": "INSTANCE_NAME", "value": "{server.name}" }
      ],
      "mountBindings": [
        { "hostPath": "C:\\Logs", "containerPath": "C:\\Logs", "readOnly": false }
      ],
      "networkAliases": [
        { "network": "nat", "alias": "web-base" }
      ]
    }
  ]
}
```

Then set this on your container (or compose service):

```yaml
labels:
  quickproxy.defaults: web-base
```

When that container starts, QuickProxy merges missing label/env/mount values from `web-base`.
Missing network aliases are merged too, and duplicate aliases for the same network are de-duplicated.

## 11) Automatic template with forwarded HTTPS traffic

```json
{
  "automaticTemplates": [
    {
      "id": "public-web",
      "mode": "automaticContainer",
      "enabled": true,
      "forceSsl": true,
      "websockets": true,
      "automaticContainer": {
        "labelSelectors": [
          { "key": "public", "valuePatterns": ["true"] }
        ],
        "domainTemplates": ["{container.name}.{server.name}.example.com"]
      },
      "routes": [
        {
          "path": "/",
          "rewriteMode": "preserve",
          "upstreamMode": "container",
          "upstream": { "scheme": "http", "host": "", "port": 80 },
          "container": {
            "containerName": "",
            "scheme": "http",
            "port": 8080,
            "portResolutionMode": "container",
            "networkName": "nat"
          }
        }
      ],
      "tls": { "mode": "none" }
    }
  ]
}
```

QuickProxy preserves the original `Host` header and explicitly sets `X-Forwarded-*` headers for proxy hosts, so upstream apps can generate correct absolute redirects when they honor forwarded headers.


