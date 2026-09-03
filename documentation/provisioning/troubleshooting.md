# Provisioning Troubleshooting

## Provisioning runs but nothing imported

Check:

- `Provisioning.Enabled` is `true`
- exactly one source is set (`FilePath` or `Url`)
- startup logs for validation warnings

## File paths not found

- relative paths resolve from the QuickProxy application directory
- verify mounted/bind volumes for container deployments

## Issuer does not auto-bind

Check:

- host domain matches issuer suffix rules
- issuer is enabled
- issuer CA source is valid and readable
- issuer CA certificate has private key
- host has no already-existing `issued-*` certificate ID (issuer flow is create-if-missing)

## Path PFX issuer not working

- verify `issuerCaSource` is `pathPfx`
- verify `caPfxPath` exists relative to the QuickProxy application directory
- if using env var password, verify the variable exists in QuickProxy process environment
- verify the PFX actually contains a CA cert + private key

## Store thumbprint issuer not working

- verify thumbprint exists in selected store
- verify store location/name (for Computer Certificates: `LocalMachine`)
- remove spaces/casing concerns (spaces are normalized)
- verify private key is present and accessible to app identity

## Ambiguous issuer source in provisioning

If multiple CA source hints are set and `issuerCaSource` is omitted, import may fail validation.

Set `issuerCaSource` explicitly.

## Container default set did not apply

Check:

- the set exists in `containerDefaultSets` with the exact same `id`
- container label `quickproxy.defaults=<id>` is present on the container
- label/env keys in the set are valid (non-empty, no duplicates)
- mount bindings and network aliases are valid (non-empty, no duplicates)
- set labels do not use reserved `quickproxy.internal.*` keys

Notes:

- provisioning does not overwrite existing default set IDs
- existing container label/env/mount/network-alias values are preserved; defaults only fill missing values

## Proxy app redirects to internal host or `http`

Check:

- the upstream app trusts forwarded headers
- the upstream app uses `X-Forwarded-Host` / `X-Forwarded-Proto` or preserved `Host`

Notes:

- QuickProxy proxy hosts preserve the original `Host` header
- QuickProxy also explicitly sets `X-Forwarded-*` headers on proxy routes
