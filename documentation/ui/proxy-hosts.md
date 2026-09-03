# Proxy Hosts

Proxy Hosts map incoming domains and paths to upstream targets.

The create and edit dialog keeps the **General**, **Routes**, and **SSL/TLS** navigation fixed while longer tab content scrolls. Its scroll body has consistent vertical padding between the fixed navigation and actions. Route-level forwarding switches are grouped beneath the target fields with their explanations.

The page also includes a separate **Domain Translation** card. Domain translation rules are not normal proxy hosts. They are suffix-based reverse-proxy rules that translate one reachable frontend domain to another backend domain.

## Host modes

- **Manual**: explicit host config
- **Automatic from container labels**: generate runtime hosts from container labels

## Core fields

- **Host ID**: unique name for this host rule. Keep it stable after creation so references and future updates continue to identify the same host.
- **Domain Names**: hostnames QuickProxy should match from incoming `Host` headers (for example `app.example.com`). If the request hostname is not in this list, this host is not used.
- **Certificate Configuration**: the certificate profile used for HTTPS on this host. If set, TLS termination uses this certificate.
- **Routes**: path-based forwarding rules evaluated when a domain match is found. This decides where traffic goes next.
- **Enabled**: turns the host on/off without deleting it. Disabled hosts do not receive traffic.
- **Force SSL**: redirects HTTP requests to HTTPS for matching domains.
- **Cache assets**: enables static-asset caching behavior for this host.
- **WebSockets support**: enables websocket upgrade handling for this host.

## Forwarded headers and host preservation

By default, QuickProxy proxy hosts:

- preserve the original incoming `Host` header when forwarding
- explicitly set `X-Forwarded-*` headers on the proxied request

This is important for upstream apps that generate absolute redirects, OpenAPI links, callback URLs, or GraphQL/Nitro endpoints based on request scheme and host.

## General tab inputs

- **Host Mode**
  - **Manual**: you define fixed domains and upstream targets.
  - **Automatic from container labels**: this host becomes a template; runtime hosts are generated from matching containers.
- **Domain Names** (Manual mode): exact domains to listen for. This is the primary host match key.
- **Domain Templates** (Automatic mode): pattern used to generate hostnames for matched containers (for example `{label.app}.{server.name}.example.local`).
- **Label Selectors** (Automatic mode): filter that decides which containers generate hosts.
- **Label Key**: required label name that must exist on the container. Choose from label keys discovered on the current container inventory.
- **Value Regexes**: optional regex filters for the selected label value. If empty, any value for that label is accepted.

## Routes

Each route uses these labels:

- **Path**: URL prefix this route handles (`/` catches all, `/api` catches API prefix).
- **Path Rewrite**:
  - **Preserve path**: forwards original path unchanged.
  - **Strip matched prefix**: removes the matched route prefix before forwarding.
  - **Replace matched prefix**: swaps matched prefix with **Target Path**.
- **Target Type**:
  - **Manual**: forward to fixed host/port.
  - **Container**: forward to discovered container target.
- **Scheme**: upstream protocol (`http` or `https`) used when calling target.
- **Target Host**: destination hostname/IP (manual) or selected container identity.
- **Target Port**: destination port to call on target.
- **Preserve original Host header**: when enabled, upstream sees the original frontend host. When disabled, the upstream destination host is used instead.
- **Send X-Forwarded headers**: when enabled, QuickProxy sends `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto`.
- **Ignore invalid upstream HTTPS certificates**: available for `https` routes. This disables upstream certificate trust and hostname validation for that route.
- **Port Resolution** (container target):
  - **Container port**: route to container-internal port over container network.
  - **Published host port**: route to published Docker host port.
- **Preferred Network**: optional container network override when multiple networks exist.

Runtime note:

- container-target routes can proxy either to the container's internal port on a Docker network or to a published host port
- preserving the original host header does not change route matching; it only affects the request seen by the upstream app
- disabling forwarded headers is useful when an upstream app or IIS rewrite rule is deriving canonical URLs from `X-Forwarded-Host`
- ignoring invalid upstream HTTPS certificates should only be used for internal or test upstreams

## Automatic container templates

Template hosts use:

- **Domain Templates**
- **Label Selectors**
- **Label Key**
- **Value Regexes**

Supported placeholders in domain templates:

- `{container.name}`
- `{server.name}`
- `{label.some-label-key}`
- `{kv.some/path}`

`{kv.*}` rules:

- only text payloads can be expanded
- secret text payloads are decrypted before substitution
- binary payloads are ignored and are not substituted

Example:

```text
Key: shared/base-domain
Value: apps.example.internal
```

```text
Domain Template:
{label.app}.{kv.shared/base-domain}
```

If a container has label `app=orders`, the generated domain becomes:

```text
orders.apps.example.internal
```

## SSL/TLS tab input

- **Certificate Configuration**: selects a certificate from the Certificates page. QuickProxy uses this when terminating HTTPS for the matched domains.

## Domain Translation

Domain translation is designed for cases where one domain is reachable from clients, but the real upstream domain is only reachable inside your network.

Example:

- clients can reach `example.com`
- the real app is only reachable as `dev.localhost`

A rule:

- `sourceDomain = example.com`
- `targetDomain = dev.localhost`

causes QuickProxy to translate:

- `example.com` -> `dev.localhost`
- `api.example.com` -> `api.dev.localhost`
- `foo.bar.example.com` -> `foo.bar.dev.localhost`

Behavior:

- the apex domain and all subdomains are matched by one rule
- path and query string are preserved
- the incoming port is preserved
- normal proxy hosts win first; domain translation is only used when no normal host matches

Example request:

```text
https://api.example.com:8443/v1/users?active=true
```

with:

```text
sourceDomain: example.com
targetDomain: dev.localhost
```

becomes an upstream request to:

```text
https://api.dev.localhost:8443/v1/users?active=true
```

### Fields

- **Rule ID**: stable management ID for the translation rule.
- **Enabled**: turns the rule on or off without deleting it.
- **Source Domain**: the frontend domain suffix to match.
- **Target Domain**: the backend domain suffix to translate to.
- **Certificate Configuration**: optional source-side TLS certificate or issuer-backed certificate configuration.
- **Rewrite upstream Host header**: when enabled, QuickProxy sends the translated host upstream; when disabled, it preserves the original incoming host header.

### TLS behavior

QuickProxy terminates TLS on the source domain before translation happens.

That means:

- the certificate applies to `example.com` / `*.example.com`
- not to `dev.localhost`

If **Certificate Configuration** points to an **Issuer** entry, QuickProxy uses the existing issuer system for the source domain and can issue/reuse certificates for:

- `example.com`
- `*.example.com`

Example:

```text
sourceDomain: example.com
targetDomain: dev.localhost
certificateId: corp-issuer
```

In that case, HTTPS for `app.example.com` uses an issued certificate for the source side, and the request is then translated to `app.dev.localhost`.

### Host header behavior

When **Rewrite upstream Host header** is:

- **Off**: upstream sees the original host, for example `api.example.com`
- **On**: upstream sees the translated host, for example `api.dev.localhost`

Choose **Off** when the upstream application expects the public host. Choose **On** when the upstream server routes by its internal/backend hostname or requires matching SNI/Host values.

