# Users

The Users page manages access to the administration UI and API.

## Capabilities

- manage local QuickProxy users
- manage multiple named LDAP, OIDC, and Microsoft Entra providers
- enable/disable accounts and providers
- assign QuickProxy roles and provider role mappings
- reset local passwords
- test LDAP connectivity and OIDC discovery from the admin UI

## Roles

- `Reader` can view enabled modules.
- `Operator` can manage proxy, container, key/value, certificate, configuration, audit, shell, log, and update workflows.
- `Administrator` has Operator access and can also manage users, providers, mappings, and identity policy.

Permissions are enforced by the server, including for API and WebSocket operations.

## Local users

Use this tab for local admin accounts:

- create a local admin user
- edit display name, enabled state, and roles
- reset a password and copy the one-time temporary password
- review linked external identities and effective role sources

### Auth Providers

Use this tab for external authentication setup:

- `LDAP` providers use search + bind
- `OIDC` providers use browser sign-in through authorization code flow with PKCE
- `Microsoft Entra` providers use the same secure browser flow with Entra-specific defaults

Each provider stores:

- `id`
- `displayName`
- `enabled`
- automatic provisioning and default roles
- group or claim role mappings
- type-specific config
- encrypted secrets such as LDAP bind password or OIDC client secret

## Login behavior

- local username/password login remains available
- enabled LDAP providers participate in password login
- enabled OIDC providers appear as login buttons on the sign-in page
- if automatic provisioning is enabled, successful external sign-in can create or update a linked local user
- disabled users cannot log in, even if the external provider authenticates successfully
- changes to users, passwords, roles, or relevant providers end affected sessions

## Examples

### LDAP example

- `Id`: `corp-ldap`
- `Display Name`: `Corporate LDAP`
- `Server`: `ldap.corp.local`
- `Port`: `636`
- `Use SSL`: `true`
- `Bind DN`: `CN=svc-quickproxy,OU=Service Accounts,DC=corp,DC=local`
- `Base DN`: `OU=Users,DC=corp,DC=local`
- `User Filter`: `(&(objectClass=user)(mail={email}))`

### OIDC / Entra example

- `Id`: `entra`
- `Display Name`: `Microsoft Entra ID`
- `Authority`: `https://login.microsoftonline.com/<tenant-id>/v2.0`
- `Client Id`: `<app-registration-client-id>`
- `Scopes`: `openid profile email`
- `Use PKCE`: `true`
