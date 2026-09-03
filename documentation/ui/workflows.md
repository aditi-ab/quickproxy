# Common Workflows

Page-level action groups use equal-width button tracks based on the longest sibling action. On narrow screens the
buttons stack to preserve readable labels and full-size touch targets.

Standard form and detail dialogs use consistent vertical body padding between their headers and action footers.
Edge-to-edge log and terminal viewers retain their specialized layouts.
Cards retain their standard internal gap and padding. Cards containing a Table or DataTable collapse that gap and
outer vertical padding so the table connects directly to the card header or toolbar.

Separate cards retain their standard vertical spacing. Table cards remove the Card component's internal gap so the
table begins directly below the card header divider, matching the Domain Translations layout.

Proxy route cards use a single centered header row for their title and action, followed by the standard Card gap
before the first field row.

The container inventory toolbar keeps status filters together and groups the project filter with bulk actions. It uses
a single padded row on wide screens and stacks complete control groups on narrow screens.

Domain translation forms place labels above text and select controls. Boolean controls keep the switch before its
associated label and do not wrap controls inside labels.

Issuer forms follow the same field structure: labels sit above inputs. The Enabled switch precedes its label on the
left side of the dialog action row, opposite Cancel and Save.

Certificate forms keep labels above their controls in every certificate mode and use the dialog's standard body
spacing and single footer divider.

Every dialog supplies an accessible description, including descriptions that are visually hidden when the dialog's
purpose is already clear from its visible content.

Monaco-backed YAML and value editors follow QuickProxy's effective light or dark theme, including system-theme
changes, so syntax tokens retain sufficient contrast with the editor surface.

## Publish one app with TLS

1. Create/import certificate in Certificates tab.
2. Create proxy host and fill **Domain Names**.
3. In **SSL/TLS**, set **Certificate Configuration**.
4. Add route `/` to upstream.
5. Enable **Force SSL**.

## Auto TLS with issuer

1. Create issuer in Issuers tab.
2. Set **Match Domains** (for example `example.com`).
3. Configure **CA Source**.
4. Create host matching that domain.
5. QuickProxy issues and binds a certificate automatically.

## Automatic hosts from containers

1. Create a host with **Host Mode** = **Automatic from container labels**.
2. Add **Label Selectors**.
3. Add **Domain Templates** with `{container.name}` / `{server.name}`.
4. Ensure containers carry matching labels.

## Reuse a container default set

1. Create a Defaults set in the Containers page.
2. Add label `quickproxy.defaults=<set-id>` to the target container.
3. Start or restart the container.
4. QuickProxy fills missing labels, env vars, mount bindings, and network aliases from that set.

## Upgrade a container from an image archive

1. Open Containers.
2. Drop a `.tar`, `.tar.gz`, or `.tgz` image archive onto a specific container row, or use the edit dialog.
3. Review detected repo tags and save the update.
4. QuickProxy recreates the container from the archive image.

## Open a container shell

1. Open Containers.
2. Click the shell action on a running container.
3. Use the in-browser terminal to run commands interactively.

