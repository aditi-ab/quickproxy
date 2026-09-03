# Containers

The Containers page shows Docker containers and allows management actions.

It also includes a **Projects** tab for QuickProxy-managed Docker Compose projects.

## Common actions

- refresh inventory
- view details and edit container config
- start/stop/restart/remove containers
- open live logs stream
- open an interactive shell for running containers
- re-pull and restart a container from its configured image reference
- upgrade a container from a dropped Docker image archive
- run bulk actions from the **Actions** menu for selected containers
- filter the inventory by running state, system-container visibility, or Compose project
- the filter toolbar groups a full-width project selector with the visibility switches, keeps bulk actions right-aligned, and uses comfortable vertical padding; the project selector retains an accessible name without a redundant visible label
- expanded image metadata wraps long local and remote digests within their grid columns instead of widening the table
- manage **Defaults** sets (`id + labels + env vars + mount bindings + host mappings + network aliases`) in the Defaults tab
- manage QuickProxy-owned Docker Compose projects from the **Projects** tab
- container create/edit fields use aligned label and control rows, with one divider above the dialog actions

## Logs

- the live logs dialog displays new container log entries as they arrive
- container and Compose log dialogs use a large bounded viewer, one shared close control, and a compact auto-scroll control without a label background
- stderr entries are visually highlighted
- ANSI color sequences are preserved if the container log output already contains them
- QuickProxy does not fake a TTY for log streaming, so applications that only emit colors for interactive terminals may still produce plain text logs

## Compose projects

- Managed compose projects and companion files persist under `Data/Containers/Projects/`, which must be included in backups.
- The project dialog separates Compose YAML from companion managed files, and the dark editor uses the card palette with higher-contrast syntax colors.
- Docker Compose must be available to QuickProxy for project actions.
- Each QuickProxy compose project represents one managed compose deployment. To run multiple instances from the same compose definition, create multiple QuickProxy projects with different ids / compose project names.
- QuickProxy only manages projects created in this tab; unrelated external compose projects are not imported automatically.
- Supported operations are validate, deploy, start, stop, restart, pull, down, delete, and live logs.
- The compose editor accepts raw YAML. Compose features such as `secrets`, `configs`, `include`, `extends`, and `profiles` are not supported and are rejected.
- In the create dialog, `Project Id` auto-derives from `Display Name` in lowercase kebab-case until you edit the id manually.

## Compose YAML reuse

You can reuse common environment variables or labels across services with YAML anchors and merge syntax.

Example:

```yaml
x-common-env: &common-env
  APP_MODE: production
  LOG_LEVEL: info

x-common-labels: &common-labels
  com.example.team: platform
  com.example.managed-by: quickproxy

services:
  api:
    image: nginx:alpine
    environment:
      <<: *common-env
      API_PORT: "8080"
    labels:
      <<: *common-labels
      com.example.service: api

  worker:
    image: alpine:latest
    command: ["sh", "-c", "while true; do echo working; sleep 10; done"]
    environment:
      <<: *common-env
      WORKER_CONCURRENCY: "4"
    labels:
      <<: *common-labels
      com.example.service: worker
```

## Automatic templates integration

When using automatic container proxy host templates, running containers that match label selectors generate effective runtime hosts.

## Container defaults integration

- Create a Defaults set with a unique Set Id in the first field of the dialog.
- Add label `quickproxy.defaults=<set-id>` to a container.
- On container start, QuickProxy applies matching Defaults set values.
- Existing container values win; defaults only fill missing labels/env vars/mount bindings/host mappings/network aliases.

## Templates in values

For label, environment, mount, host mapping, and network alias values, you can use placeholders from `TemplateValues` (for example `{server.name}` and `{server.ip}`).

This also supports `{kv.some/path}` values from the Key/Values store.

`{kv.*}` rules:

- only text payloads can be expanded
- secret text payloads are decrypted before substitution
- binary payloads are not substituted

Examples:

```text
Key: shared/api-base-url
Value: https://api.internal.example
```

Container label value example:

```text
myapp.api-url={kv.shared/api-base-url}
```

Container environment value example:

```text
API_BASE_URL={kv.shared/api-base-url}
```

Secret example:

```text
Key: shared/db-password
Entry Type: Secret
Payload Kind: Text
Value: super-secret-password
```

```text
DB_PASSWORD={kv.shared/db-password}
```

Special cases:

- `SERVERNAME` environment variable is mapped to `{server.name}`.
- `SERVERIP` environment variable is mapped to `{server.ip}`.

Host mapping example:

```text
Hostname: my.internal
Address: {server.ip}
```

## Tips

- Use consistent labels to simplify template matching.
- Keep a clear convention for container names and exposed ports.
- The Images toolbar keeps the image count and visibility filter together, with the prune action aligned opposite them.
- Archive-based images can be created and updated from `.tar`, `.tar.gz`, or `.tgz` image archives.
- Archive-loaded images cannot be re-pulled from a registry until the container is switched back to a registry image.

## Interactive shell

- The shell action is available for running containers.
- QuickProxy tries a small fallback shell list: `cmd.exe`, `powershell.exe`, `pwsh.exe` for Windows containers and `/bin/sh`, `/bin/bash`, `sh`, `bash`, `/busybox/sh` for Linux containers.
- Shell traffic is streamed live through the admin UI over WebSocket.
- The shell session is a real interactive terminal stream, so ANSI colors and terminal control sequences behave more like a normal console than the log viewer.

## Remote project ingestion

QuickProxy also exposes a public compose project upsert endpoint for external applications:

`PUT /api/containers/projects/{id}`

and a public deploy action:

`POST /api/containers/projects/{id}/deploy`

and a public down action:

`POST /api/containers/projects/{id}/down`

This uses the same payload as the admin compose project upsert route and creates or updates a QuickProxy-managed compose project without going through the admin UI.

Example payload:

```json
{
  "id": "my-project",
  "displayName": "My Project",
  "slug": "my-project",
  "status": "",
  "composeYaml": "services:\n  app:\n    image: nginx:alpine\n",
  "managedFiles": [
    {
      "path": ".env",
      "content": "FOO=bar\n"
    }
  ]
}
```

Notes:

- This endpoint creates or updates a managed compose project and its companion files.
- It does not deploy automatically by itself; call `POST /api/containers/projects/{id}/deploy` after ingesting if you want the equivalent of `docker compose up -d`.
- Call `POST /api/containers/projects/{id}/down` to stop and remove the project containers later, equivalent to `docker compose down`.

## Self-update

- QuickProxy can self-update when it is itself running as a managed container from a registry image.
- Archive-loaded QuickProxy images are not eligible for self-update.
