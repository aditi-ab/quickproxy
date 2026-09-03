# Certificates and Issuers

The Certificates page contains two tabs:

- **Certificates**
- **Issuers**

Each tab uses a single card header for its title, description, and create action, followed directly by the table.

## Certificates tab

Used for directly managed TLS configs.

Main fields:

- **Certificate ID**: unique name used when selecting this certificate in Proxy Hosts under **Certificate Configuration**.
- **Mode**: where QuickProxy should load certificate material from.

Mode options:

- certificate + key files (`.pem` / `.crt` + key file)
  - upload certificate and private key files directly in UI.
  - used when you manage PEM files yourself.
- PFX file (`.pfx`)
  - upload one bundled certificate package.
  - optional password fields allow decryption.
- Windows store thumbprint
  - reference an already-installed certificate by thumbprint.
  - useful when cert/private key is managed by Windows certificate store.

### Certificate mode fields

- **Certificate File (.pem/.crt)**: public certificate.
- **Key File (.key/.pem)**: private key matching certificate.
- **Intermediate File (optional)**: chain/intermediate bundle sent to clients.
- **PFX File (.pfx)**: bundled cert + private key container.
- **PFX Password (optional)**: direct password value.
- **PFX Password env var (optional)**: read password from environment variable instead of plain text.
- **Store Name** and **Store Location**: Windows store to search.
- **Thumbprint**: exact certificate thumbprint to load from selected store.

### Built-in localhost development certificate fallback

On Windows, QuickProxy also probes the local certificate store for the existing ASP.NET Core HTTPS development certificate when a host does not have a usable explicit certificate configured.

- This fallback is automatic and does not appear as a certificate entry or issuer.
- It only applies when the dev certificate already exists and has a SAN that matches the requested hostname.
- Explicit host certificate configuration still takes precedence over the built-in fallback.
- Typical covered names include `localhost` and any other SANs already present on your local development certificate, such as `*.dev.localhost`.

## Issuers tab

Issuers define a CA source and domain matching rules.

When a host matches issuer rules, QuickProxy can auto-create and auto-bind an issued certificate.

Issuer fields:

- **Issuer ID**: unique issuer name for management and troubleshooting.
- **Enabled**: controls whether this issuer participates in domain matching.
- **Match Domains**: suffix match rules (apex + subdomains). Example: `example.com` matches `example.com` and `my-app.example.com`.
- **CA Source**: where issuer CA certificate/private key comes from.

Issuer source options:

- **Upload PEM (cert + key)**
- **Upload PFX**
- **Path PEM (cert + key)**
- **Path PFX**
- **Windows Store Thumbprint**

### Windows Store Thumbprint usage

- thumbprint from Windows certificate store
- optional store name/location
- expected defaults:
  - store name: `My`
  - store location: `LocalMachine`

## Issuer behavior

- On host create/update, if host domains match an enabled issuer and no issued cert exists yet, QuickProxy creates one and binds it.
- Issuer match precedence is longest domain suffix first.
- Issued certificates are persisted and reused (create-if-missing, no automatic reissue in current version).
