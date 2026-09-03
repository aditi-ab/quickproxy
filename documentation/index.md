# QuickProxy Documentation

QuickProxy provides an admin UI and runtime provisioning for:

- Reverse proxy host management
- Domain translation proxy rules
- Container discovery and operations
- Certificate and issuer management
- Key/Value configuration management
- Secret and binary config payload storage
- Fallback behavior and system settings
- User administration

The administration UI supports light and dark themes across its application surfaces and scrollbars.

Use this documentation to:

- operate the admin UI end-to-end
- configure provisioning for domain translations, manual proxy hosts, automatic templates, container default sets, and certificates
- use issuers to auto-generate host certificates
- store secrets, binary files, and PKCS#12/PFX payloads in Key/Values
- use `{kv.some/path}` placeholders in supported template surfaces
- understand container image archive upgrades, interactive shell access, and self-update behavior

## Documentation Map

- Start here: [Setup](/getting-started)
- UI details: [Dashboard](/ui/dashboard)
- Admin activity: [Audit](/ui/audit)
- Provisioning: [Overview](/provisioning/overview)
- Runtime consumers: [Public API and SDK](/integrations/public-api-sdk)

## Key Concepts

- **Proxy Host**: A routing config for one or more domains and route rules.
- **Domain Translation Rule**: A suffix-based reverse-proxy rule that translates one reachable source domain to another backend domain while preserving path, query, and port.
- **Certificate**: TLS material bound by `certificateId` on a proxy host.
- **Issuer**: A CA source that can issue per-host certificates automatically.
- **Automatic Container Template**: A host template expanded from container labels.
- **Container Default Set**: Reusable labels, env vars, mount bindings, and network aliases applied to matching containers.
- **Key/Value Entry**: A hierarchical config item with `data|secret`, `text|binary`, labels, and optional raw/public read access.
