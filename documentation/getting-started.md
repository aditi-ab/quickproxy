# Setup

## Container deployment

Pull an immutable version tag from the QuickProxy container package. Authenticate first when the package is private:

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u USERNAME --password-stdin
docker pull ghcr.io/aditi-ab/quickproxy:0.9.42
docker volume create quickproxy-data
docker run -d --name quickproxy --restart unless-stopped \
  --read-only --tmpfs /tmp:rw,noexec,nosuid,size=256m \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  -p 18080:80 \
  -p 18443:443 \
  -p 9000:9000 \
  -e Config__Secrets__EncryptionKey="$QUICKPROXY_CONFIG_ENCRYPTION_KEY" \
  -v quickproxy-data:/app/Data \
  -v /var/run/docker.sock:/var/run/docker.sock \
  ghcr.io/aditi-ab/quickproxy:0.9.42
```

The container defaults to three SQLite databases under `/app/Data`: `quickproxy.db` for proxy configuration, certificates, administrator identities, generated certificates, and data-protection keys; `quickconfig.db` for Key/Values; and `quickaudit.db` for audit events. Configure `Storage.Provider` as `sqlserver` and set the matching `Storage.ConnectionString` on each module to use SQL Server instead. The Docker socket mount and matching supplemental group enable container management and should only be granted on a trusted Linux host.

Use a fixed `0.9.<build>` tag for controlled deployments. The `latest` tag is available for evaluation but should not be used where repeatable rollbacks are required.

### Windows Server Core LTSC 2025

Windows container images use the `-windowsservercore-ltsc2025` tag suffix. Use an immutable tag such as `0.9.42-windowsservercore-ltsc2025` for controlled deployments. The rolling `windowsservercore-ltsc2025` tag is available for evaluation. These images are `windows/amd64`, use `mcr.microsoft.com/windows/servercore:ltsc2025`, and require a compatible Windows container host.

```powershell
docker pull ghcr.io/aditi-ab/quickproxy:0.9.42-windowsservercore-ltsc2025
docker volume create quickproxy-data
docker run -d --name quickproxy --restart unless-stopped `
  -p 18080:80 `
  -p 18443:443 `
  -p 9000:9000 `
  -e Config__Secrets__EncryptionKey=$env:QUICKPROXY_CONFIG_ENCRYPTION_KEY `
  --mount type=volume,src=quickproxy-data,dst=C:\app\Data `
  ghcr.io/aditi-ab/quickproxy:0.9.42-windowsservercore-ltsc2025
```

The Windows image stores its SQLite databases below `C:\app\Data`, runs as the restricted built-in Network Service identity, and includes a self-contained .NET runtime. Its Docker endpoint defaults to `npipe://./pipe/docker_engine`. Mount the host Docker named pipe only on trusted hosts when QuickProxy needs container-management access.

## 1. Run QuickProxy

QuickProxy admin UI is served over HTTP on the internal listener port (default `9000`). Set `Listen:AdminUseHttps` to `true` and configure `Listen:AdminCertificate` to serve the administration interface over HTTPS.

Typical local URL:

```text
http://localhost:9000/admin/
```

## 2. Sign in

On the first visit, enter a username and password to create the first local administrator. The password must contain at least 12 characters with upper-case, lower-case, number, and symbol characters. This first-use setup is disabled permanently after the administrator is stored.

For later visits, sign in with the local administrator username and password. The sign-in page adapts to narrow screens without horizontal scrolling. Existing email-based accounts are migrated to username-based accounts without changing their password.

After first sign-in, external authentication is managed from **Administration > Users and providers** inside the admin UI.

- local users stay available
- LDAP providers participate in password login
- OIDC and Microsoft Entra providers appear as sign-in buttons on the login page
- a temporary or reset password must be changed after sign-in

## 3. Verify modules

The dashboard and navigation adapt to enabled modules:

- `Proxy.Enabled` controls proxy hosts, containers, certificates, settings.
- `Config.Enabled` controls key/value page.

## 4. Optional secret configuration

If you plan to use secret entries in Key/Values or encrypted auth-provider secrets, set:

```text
Config:Secrets:EncryptionKey
```

This can be provided through:

- `appsettings.json`
- environment variable `Config__Secrets__EncryptionKey`

The value can be either:

- a base64-encoded 32-byte key
- an arbitrary string

## 5. Open the documentation
Use the top toolbar **Docs** button in the admin UI to open the built-in documentation at `/docs/`. Documentation pages use clean URLs without an `.html` suffix. If a reverse proxy replaces QuickProxy's Content Security Policy, allow the generated VitePress inline bootstrap scripts under `/docs/` or configure equivalent script hashes.
