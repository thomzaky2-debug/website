# Security review — 20 November 2026

This review covers the application source, authentication, authorization, checkout ownership, NilePay settlement/webhooks, wallet transitions, uploads, browser rendering, configuration and dependencies. It is a code review and local regression exercise, not a guarantee that no vulnerabilities remain or a penetration test of the hosted service.

## Findings addressed

| Priority | Finding | Change |
| --- | --- | --- |
| Critical | SQL/admin credentials in tracked settings | Removed from tracked configuration; retained required local settings only in the ignored private configuration. Existing Git history and any deployed copies still require provider-side rotation. |
| High | Public exception handler disclosed stack traces and database errors | Replaced with a generic message and reference ID; full diagnostics stay in server logs, and audit records omit exception messages. |
| High | Startup reset the existing admin password from bootstrap configuration | Removed automatic resets. Bootstrap credentials only provision a new account. |
| High | Unsupported runtime/dependency baseline | Upgraded application, EF/Identity packages, tests and EF tooling to .NET 10 LTS. Production requires the matching supported hosting runtime. |
| High | Explicit underfunded CONFIRMED result could settle an order | Reject inconsistent detected amounts and bind status responses to the requested session. |
| High | Late overpayment could credit a refunded/otherwise conflicting order | Restrict wallet credit to a valid initial transition or matching idempotent overpayment; conflicting events require review. |
| Medium | Admin login had no request throttling | Added an Identity-area POST rate limiter alongside account lockouts and the existing customer limiter. Added checkout/payment request limits and Retry-After responses. |
| Medium | Dynamic HTML helper did not encode quotation marks in attributes | Escape all HTML delimiters, including both quotation marks; regression check uses an attribute-injection payload. |
| Medium | No enforced content security policy | Added per-response nonce-based scripts, blocked inline event handlers, framing, plugins and base-tag injection; retained HTTPS checkout redirects. |
| Medium | Cookies could become insecure when a proxy reported HTTP | Authentication, session and antiforgery cookies now always require HTTPS. |
| Medium | Wildcard host acceptance allowed untrusted origins in generated links | Local host allowlist by default; production requires an explicit host list. |
| Medium | Checkout capabilities could survive switching accounts | Clear private checkout/session references on sign-in, signup and logout; verify the owner on idempotent checkout retries. |
| Medium | Webhook body limit depended on hosting integration | Enforce a 64 KiB streaming limit in addition to endpoint metadata; cap normal request/form and outbound gateway-response sizes. |
| Medium | Production SQL/SMTP could use unencrypted transport | Require certificate-validated SQL encryption (`Encrypt=True;TrustServerCertificate=False;`) in production (enforced at startup in `Program.cs`) and TLS for enabled SMTP delivery. |
| Medium | Production startup always ran migrations despite its setting | Respect ApplyMigrationsOnStartup; production defaults to explicit, reviewed migration deployment. |

Existing controls reviewed include role authorization on both admin controllers, antiforgery on state-changing browser actions, ownership scoping of customer orders/wallet/reviews/saved items, server-side pricing and stock checks, cryptographic private tracking tokens, signed webhook timestamp/event replay controls, database transaction/idempotency constraints, and upload count/size/signature/random-filename restrictions. Uploads remain admin-only JPEG/PNG/WebP assets; no SVG or executable uploads are accepted. Product/review JSON-LD uses the default safe JSON serializer.

## Required operational follow-up

1. Rotate the database password and any administrator password previously exposed in Git, then update secure hosting/local settings. Removing current literals does not revoke exposed credentials. Review old artifacts and access logs; do not paste new credentials into issues or source control.
2. Supply the actual production `AllowedHosts`, encrypted SQL connection and first-run admin email through secure hosting configuration. Remove the bootstrap password after provisioning. This review does not contact the production SQL server or rotate credentials at external providers.
3. Install the supported .NET 10 hosting runtime and apply reviewed migrations (`dotnet ef database update`) using a deployment identity **before** starting the updated application when `Database:ApplyMigrationsOnStartup=false` (including the `AddAccountEmailQueueAndRateLimits` migration for `EmailRateLimits` and `AccountEmailQueues`). Keep the runtime patched. Test SQL Server migrations against a backed-up staging database before production deployment.
4. Configure trusted reverse proxies explicitly, persistent protected Data Protection keys outside the webroot, least-privilege database/file permissions, backups and TLS certificates. For multiple application instances, use shared sessions/keys and distributed/edge rate limiting.
5. Enable and enroll admin MFA using the account provider's supported flow before exposing administration publicly. No account enrollment or credential rotation was performed in this source review.

CSP intentionally permits inline styles because the existing theme uses them, and HTTPS form redirects because NilePay uses hosted checkout. It blocks inline scripts without the server nonce and all inline event handlers. Uploaded images are bounded and signature checked; they are not antivirus-scanned or re-encoded. Infrastructure/DNS/WAF/TLS-provider configuration and live gateway/SMTP/SQL behavior need separate deployment verification.

## Verification

Debug build, Release publish, all 16 security HTTP checks, the HTML-escaping check, relational checks and the storefront/payment/customer/language/category/hero/subdomain suites passed. The dependency audit including transitive packages reported zero known advisory matches. EF reported no pending model changes; the new baseline migration has no schema operations. Release-package scans found no local configuration or configured secret values. Private server file probes returned 404. Detailed local regression results are recorded in VERIFICATION.md. Security-specific checks are in `tests/security_http.py`, `tests/security_js.cjs` and the relational checks in `tests/Program.cs`. Tests use disposable SQLite databases and mocked payments; they do not make real payments or alter real customer records.

## Reference guidance

- [Microsoft: production exception handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).
- [Microsoft: .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
- [OWASP: File Upload Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html).
