# Overview

The Overview page gives a quick status view and entry points to major areas. Every routed administration page uses the same centered `page-container` as ApiGateway, with a 1600 pixel maximum width and responsive horizontal padding. This keeps page headings, cards, tables, and forms aligned consistently on wide and narrow screens.

The management console uses a two-row application header. Product pages are available directly from the primary navigation, while configuration, audit, and identity pages are grouped under **Administration**. The language, theme, account, and documentation controls remain available in the top row. Page-level tabs render directly in the content flow instead of inside decorative cards. Cards use native headers and content regions, with section actions aligned separately from their titles. Data grids use the native table anatomy, and dialogs keep headers, scrollable bodies, and actions in separate regions. Menus, dialogs, tooltips, form controls, and table actions support keyboard navigation and visible focus, and data surfaces adapt to narrower screens.

## What to check first

- Runtime version and module availability
- Proxy and config module state
- Quick links to Proxy Hosts, Certificates, Containers, and the KV Store
- Self-update availability and current image status when QuickProxy is running as a container

## Typical use

1. Confirm proxy module is enabled.
2. Open Proxy Hosts to define routing.
3. Open Certificates tab (with Issuers) to prepare TLS.
4. Open Containers to inspect runtime apps, logs, shell access, and image updates.
