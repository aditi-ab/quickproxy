# Key/Values

Key/Values provides hierarchical config storage and editor tooling.

## Features

- folder list and breadcrumb navigation
- entry create/edit/delete
- move and rename operations
- drag-drop file import
- text and binary payload support
- `data` and `secret` entry types
- labels on each entry
- local revision history with inspect + restore
- code editor with language detection for JSON, YAML, and text
- public config reads, including raw text/binary access
- `{kv.some/path}` template support for text entries

## Entry model

Each key stores:

- `entryType`: `data` or `secret`
- `payloadKind`: `text` or `binary`
- `value`: text payload
- `binaryBase64`: binary payload in base64 form
- `mediaType`: optional content type metadata
- `labels[]`: ordered key/value label pairs

Existing plain key/value entries continue to work as `data + text`.

## Secrets

Secret entries encrypt the payload and labels at rest.

- secret text entries decrypt only when you explicitly reveal them in the admin UI
- secret binary entries can be downloaded after reveal in the admin UI
- the encryption key comes from `Config:Secrets:EncryptionKey`
- the `Config__Secrets__EncryptionKey` environment variable overrides the corresponding configuration-file value

`Config:Secrets:EncryptionKey` can be:

- a base64-encoded 32-byte key
- any arbitrary string, which QuickProxy derives into a stable 32-byte key

Example:

```json
{
  "Config": {
    "Secrets": {
      "EncryptionKey": "my-dev-secret-key"
    }
  }
}
```

Container example:

```text
Config__Secrets__EncryptionKey=my-production-secret-key
```

## Payload kinds

### Text

Text entries open in the code editor and are intended for config values, JSON, YAML, templates, and similar content.

Example text key:

```text
Key: app/settings.json
Entry Type: Data
Payload Kind: Text
Value:
{
  "baseUrl": "https://api.example.com",
  "featureX": true
}
```

### Binary

Binary entries are intended for files such as:

- `.pfx` / PKCS#12 certificates
- license files
- binary application assets

In the admin UI:

- switching between `Text` and `Binary` converts the current content instead of discarding it
- dropping files asks whether to import them as text or binary
- binary downloads use the key name as the file name

Example binary key:

```text
Key: certificates/client-cert.pfx
Entry Type: Secret
Payload Kind: Binary
Media Type: application/x-pkcs12
```

## Labels

Each key can have labels as key/value pairs.

- labels are available on both text and binary entries
- labels are encrypted together with the payload when the entry type is `secret`
- the list view shows label count in the details column

Example labels:

```text
team=payments
environment=prod
format=pfx
```

## Admin UI behavior

- folder rows navigate into a folder, the row with a folder icon and `..` returns up one level, and the current-location breadcrumb can jump directly to an ancestor
- the current-location breadcrumb and entries heading share one compact panel without an empty spacer section between them
- the entries heading shows the item count and reveals bulk actions only after one or more entries are selected
- entry and revision dialogs use a single footer divider, and label rows use a compact trash action for removal
- import and restore confirmations use compact non-scrolling dialogs with confirmation checkboxes before their labels
- alerts communicate their purpose visually: completed actions use a green check, browsing guidance uses an information icon, replacement warnings use amber, and failures use the destructive treatment
- the Type column identifies each entry as Data or Secret and Text or Binary
- selecting a folder row navigates into that folder, while selecting a key opens its editor in a large dialog without replacing the entries table
- use the bulk actions to move or copy selected folders and entries
- folders and keys share the same hierarchical namespace
- folders can be selected for bulk move/delete actions
- secret values stay hidden until you press **Reveal**
- saving an edited entry exits edit mode
- the entries list is lightweight and does not preload full payloads for every entry

## Revision history

QuickProxy keeps immutable revision snapshots for local entries and local overrides.

- revisions are local-only; remote master entries do not have revision history
- each revision stores a full snapshot, not a diff
- updating a local key captures the previous local value as a revision before replacement
- creating a local override starts revision tracking on the local side
- rename and move operations preserve revision history under the new key
- restore/import operations capture the previous local dataset before replacement
- explicitly deleting a key removes its revision history

In the admin UI:

- open a local key or local override
- click **Revisions**
- inspect an older snapshot
- reveal secret revisions explicitly when needed
- restore a revision to make it the current local value again

Restoring a revision only affects the local entry. It does not modify remote source data.

## Public config API

Public config reads support both structured and raw access.

- text entries can be returned as normal config values
- binary entries are returned as base64 in JSON responses
- `?raw` returns plain text for text entries
- `?raw` returns bytes for binary entries
- `?decrypt` is a presence flag, so `?decrypt` or `&decrypt` is enough
- secret text entries are decrypted for reads when `decrypt` is requested
- secret binary entries can be downloaded through `?raw&decrypt`

Examples:

```text
/api/config/app/settings.json
/api/config/app/settings.json?raw
/api/config/certificates/client-cert.pfx?raw&decrypt
/api/config/templates/nginx.conf?raw&template
```

See also: [Public API and SDK](/integrations/public-api-sdk)

## `{kv.*}` templating

QuickProxy supports `{kv.some/path}` placeholders backed by the Key/Values store.

- only text entries can be used as `{kv.*}` sources
- if the referenced entry is a secret text entry, it is decrypted before substitution
- binary entries are not substituted
- references can resolve against local or remote-backed config entries

`{kv.*}` currently applies in:

- proxy host templating
- container value templating
- public config raw responses when `?raw&template` is used

Example source key:

```text
Key: shared/base-domain
Entry Type: Data
Payload Kind: Text
Value: example.internal
```

Example placeholder usage:

```text
{kv.shared/base-domain}
```

## Typical use

1. Select a folder.
2. Choose `Data` or `Secret`.
3. Choose `Text` or `Binary`.
4. Add labels if needed.
5. Save values for app/runtime consumption.

Example workflow:

1. Create `shared/base-domain` as a text entry with value `example.internal`.
2. Create `certificates/client-cert.pfx` as a secret binary entry by uploading a `.pfx` file.
3. Add labels like `environment=prod` and `team=payments`.
4. Reference `{kv.shared/base-domain}` from a proxy template or container value.
