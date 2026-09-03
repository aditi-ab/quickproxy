# Settings

Settings includes fallback and runtime-related behavior controls.

The tab bar remains separate from the form surfaces. Each tab opens a structured settings card with a descriptive
header, aligned controls, and its own Save footer.

## Unknown host fallback

Configure behavior for unmatched domains:

- disabled
- status-code response
- HTML file response
- redirect

## Proxy debug logging

Settings also includes a global **Proxy debug logging** toggle.

When enabled, QuickProxy writes structured debug entries for matched proxied requests to the application log. This is useful when diagnosing:

- upstream redirects
- `Host` header preservation vs rewrite behavior
- `X-Forwarded-*` behavior
- upstream status codes and selected response headers
- CORS and preflight handling

The debug payload includes:

- matched host and route
- incoming request method, host, path, and selected headers
- configured upstream target
- outbound proxy request destination and selected headers
- final response status and selected upstream response headers

Use this temporarily for troubleshooting rather than leaving it enabled permanently.

## Guidance

- Use fallback pages for safer default UX.
- Keep content-type aligned with payload mode.
- Enable proxy debug logging only while diagnosing a routing or upstream behavior issue.
