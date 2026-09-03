# Audit

QuickProxy can record admin write activity in a dedicated Audit module. Audit storage is configured separately from Proxy and Key/Values storage, so it can use its own database connection.

## What it records

- Authenticated admin write actions made through the admin API
- Outcome, actor, target, status code, correlation id, and a redacted request summary
- Provisioning runs as system-generated audit events

## What it does not store

- Secret plaintext
- Passwords, client secrets, private keys, decrypted key/value secrets, shell content, or raw log output
- Full large payload archives by default

## UI

The admin UI exposes Audit as its own top-level page. It follows the same layout conventions as the other admin pages:

- top card with filters
- main table with newest-first events
- detail dialog for event metadata and redacted change summary

Filter labels sit above aligned controls, the outcome selector uses an explicit non-empty **All outcomes** value, and
the events table follows its header without an empty card spacer.

## Storage

Audit uses its own module settings:

```json
{
  "Audit": {
    "Enabled": true,
    "Storage": {
      "Provider": "sqlite",
      "ConnectionString": "Data Source=Data/quickaudit.db"
    }
  }
}
```

Configure the Audit database independently from the Proxy and Config databases. Both SQLite and SQL Server are supported.

## Admin API

- `GET /api/admin/audit`
- `GET /api/admin/audit/{id}`

Supported list filters:

- `module`
- `action`
- `actor`
- `target`
- `outcome`
- `fromUtc`
- `toUtc`
- `limit`
- `offset`
