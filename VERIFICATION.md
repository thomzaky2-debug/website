# Verification

- .NET 8 build: passed, zero warnings and errors.
- Eight relational SQLite checks: server-calculated totals, inventory decrement, idempotent retry, transaction rollback, no order on failure, English fallback, terminal status restrictions, pending cancellation.
- Eight HTTPS smoke checks: rendered seeded storefront, product details, antiforgery rejection, blocked registration, protected admin routes, Arabic culture cookie and RTL, admin login/dashboard, bilingual editor views.
- NilePay integration: local mock HTTPS end-to-end checks cover persisted retry keys/payloads across restart, signatures, timestamps, event deduplication, early/unknown sessions, mismatched totals/currency, nonconfirmed states and authenticated reconciliation. No live payment calls.
- SQL Server initial and NilePay EF migrations and model snapshot generated. Idempotent SQL script: `Data/initial.sql`.

SQL Server 2025 execution, live MonsterASP.NET/IIS deployment, visual browser QA and production load tests have not been performed. Local relational tests use SQLite; they do not establish SQL Server behavior. Real hosting requires the database connection, admin bootstrap settings, domain, HTTPS and persistent Data Protection keys described in README.md.

Run `dotnet run --project tests/CheckoutChecks.csproj` for relational checks. On Linux, `python3 tests/http_smoke.py` runs HTTPS checks using the workspace `.dotnet/dotnet` SDK, randomized test credentials and an isolated development database. It stops its server on completion.

NilePay update: clean build; 25 local mock payment HTTP checks, eight checkout checks, and eight storefront HTTPS checks passed. Updated Release IIS publish output was generated. Live NilePay payments and SQL Server deployment remain unverified.

## Project review — 2026-11-14

Fixed local configuration copying into build/publish artifacts; blank optional SMTP values and disabled credential decryption; controller-level checkout response retries; stale inactive cart entries; catalog and delivery amounts with fractional piastres; gateway category length; refunded/cancelled payment page state; decimal sales aggregation.

Validation: clean .NET build and Release publish; relational checkout checks including one-time cancellation stock release, late payment review, duplicate refunds and confirmation after refund; HTTPS smoke checks including disabled SMTP, invalid product edits, image upload and stale cart pruning; full isolated NilePay HTTP mock suite including checkout response retry and refunded payment-page behavior. The original local configuration remains in the workspace.

Limits: these checks use local SQLite and a mock payment gateway. SQL Server deployment/concurrency, a real SMTP provider, and live bank settlement were not exercised. Browser screenshot access was unavailable for visual layout verification.

## Customer experience upgrade

Added account-owned saved items and order history, purchase-verified reviews with admin moderation, admin customer search and storefront service links. The local preview database was backed up before adding the new schema. Tests cover saved-item idempotency, anonymous login redirects, account/admin separation, guest-order isolation, rejection of unverified reviews, moderation/publication, checkout ownership and the existing NilePay mock flows. EF reports no pending model changes after `CustomerExperience`; SQL Server deployment still needs its migration applied.

## Background updates — 2026-11-19

Routine admin and customer forms now use progressive background submission: products/categories, store appearance/contact/policy/mail/shipping settings, order operations, review moderation/submission, profile/password updates, saved items, cart removal, payment-status checks, filters and pagination. Authentication, language changes, checkout/payment handoffs, exports and normal page navigation retain their intentional navigation behavior.

A shared renderer updates server-rendered content without replacing the document, preserves scroll position and unrelated draft forms (including file selections), shows validation, and blocks overlapping generic submissions. Each content update disposes old event listeners, timers, observers and pending requests before initializing the replacement content. Failed requests are not automatically retried and do not force a page reload.

Validation: clean .NET build and JS syntax checks; expanded HTTPS smoke suite and category visibility suite passed. An isolated browser test verified repeated product activation, saving settings while retaining another form's draft, category filter/sort changes, add-to-cart after filtering, quantity totals, removing the last cart item, duplicate-slug validation and successful category creation staying in its editor. Tests used disposable local data. Live bank payments, real mail delivery, and physical-device browser testing were not performed.

## Hidden product links — 2026-11-19

Hidden products and products in hidden categories now render a bilingual unavailable page at their existing URLs. Recently Viewed entries are preserved and remain visible on that page; there are no purchase controls. Unknown product URLs retain ordinary not-found behavior. Product detail responses are marked no-store, and unavailable pages retain HTTP 404 with noindex metadata.

Validation: clean build, expanded HTTPS smoke suite, and category visibility checks including Arabic rendering passed. An isolated browser test viewed a product, hid it in disposable test data, reopened it from Recently Viewed, and confirmed the unavailable message and retained history entry. Existing visibility checks cover showing products again.

## Dedicated admin workspaces — 2026-11-19

Dashboard now loads summary metrics and links to each workspace. Product management, categories and audit activity have dedicated pages at `/Admin/Products`, `/Admin/Categories` and `/Admin/AuditLogs`. Orders use the existing order operations page. Editors, visibility controls, deletion and cleanup return to their owning workspace; product actions retain the page number. Audit activity supports server-side search and pagination. Header navigation has one active destination and no separate duplicate admin bar.

Validation: clean build and expanded HTTPS smoke suite, including all three new pages, one active navigation link, dashboard isolation, audit search and editor/action redirects; category visibility suite passed. An isolated browser preview verified the focused dashboard, standalone product/category pages and an in-place product visibility update staying on Products. User store data was not modified by these tests.

## Admin products by category — 2026-11-19

The Products workspace groups rows beneath localized category headings, with shown/total counts and category filters. Admin counts include inactive products, and hidden categories remain selectable and clearly labeled. Database pagination orders by category then product ID; category selection is preserved by paging and visibility/deletion actions.

Validation: clean build, expanded HTTPS smoke checks for grouping without duplicates, category-only results, invalid category handling, page clamping and filter retention; category visibility checks passed. An isolated browser preview confirmed category selection and Hide update in place while retaining the selected category. Existing user products and categories were unchanged.

## Order payment-state presentation — 2026-11-19

Admin order lists/details, customer order history/account summaries and private tracking now distinguish recorded expired, failed or cancelled sessions, checkouts without a session, and active sessions awaiting payment. Admin filters match these states. Paid fulfilment, refunds, underpayments and payment-review flags retain precedence. Terminal payment pages use the corresponding expiry/failure/cancellation heading. This is derived presentation from persisted gateway evidence; fulfilment transitions, inventory release, wallet movements and settlement rules were not changed.

Validation: clean build; relational checkout/status checks; storefront/admin HTTPS smoke suite; expanded mock NilePay suite covering missing sessions, active sessions, signed expiry/failure/cancellation events, admin filter membership, tracking/details and later confirmation overriding stale expiry. Browser verification used disposable preview orders, including the asynchronous expiry filter. No live payments were made. Existing local records included two expired sessions and one order without a session.

## Independent language preferences — 2026-11-19

Replaced the shared culture cookie provider with route-scoped admin/storefront preferences. Guests have separate secure cookies; authenticated preferences are stored per account and per area using existing Identity claims. Customer signup and account settings expose a preferred-language selector. Authentication runs before localization so saved account choices override other accounts' browser cookies. Unsupported cultures are rejected; no schema migration is required.

Validation: clean build and JS syntax; 18 isolated language checks covering independent admin/storefront settings, separate guest browsers, different customers on a shared browser, account updates and cross-browser login persistence. Storefront/admin smoke and bilingual category visibility suites passed. A disposable browser preview verified the account selector. No real customer language preferences were changed by the tests.
