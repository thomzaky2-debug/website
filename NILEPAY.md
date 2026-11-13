# NilePay setup

The storefront calculates the EGP total from its database, reserves stock once, saves an unpaid order, then offers NilePay hosted checkout. Click **Pay securely with NilePay** on the order payment page. The customer enters their real InstaPay sender handle on NilePay. The application redirects to the returned checkoutUrl; it does not assume that checkout is hosted on the API origin.

## Configure the server

Keep API keys and webhook secrets in hosting environment variables or .NET user secrets, never browser code or source control. Configure:

```text
NilePay__Enabled=true
NilePay__ApiUrl=https://nilepay-api.runasp.net
NilePay__ApiKey=<your merchant API key>
NilePay__WebhookSecret=<your merchant webhook secret>
NilePay__PublicUrl=https://your-store.example
```

Use the API origin shown in your merchant Developers page if different. PublicUrl is your actual public HTTPS store origin, with no trailing path. The application expects root hosting. Configure one webhook URL in NilePay Settings:

```text
https://your-store.example/webhooks/nilepay
```

Apply the new `NilePayPayments` migration before launching the updated application:

```sh
dotnet ef database update
dotnet publish -c Release -o publish
```

`Data/initial.sql` is the complete idempotent SQL Server deployment script including both migrations. The payment migration creates durable attempt and event tables. Existing orders get an access token and Unpaid payment status; no historical payment is inferred. Back up your database before applying production migrations.

The merchant account must be approved, have a valid plan/trial, and have the merchant detector and actual payment configuration enabled. This code does not activate plans or initiate bank transfers for testing. Restart the app after rotating keys/secrets. No live gateway calls were used in development verification.

## Payment handling

- One persisted request and key per order; network retries and application restarts reuse both. Creation retries at most once for 429/5xx with a short delay. Timeout and conflict responses lead to a recoverable payment page without creating another order/session attempt.
- The `/payment/return` page alone never marks an order paid. Customers can check authenticated server-side status; checks are limited to one per attempt per 10 seconds. There is no automatic browser polling. Both reconciliation and webhooks use the same settlement transaction.
- Webhooks require the raw-body HMAC-SHA256 signature and timestamp within 300 seconds. Event IDs come from the signed JSON body, are checked against the header, and are uniquely recorded in the database. Unknown session mappings return 503 for retry.
- Only CONFIRMED with the expected EGP amount settles an order. UNDERPAID, OVERPAID and EXPIRED alone never authorize fulfilment. Paid state cannot regress due to delayed events. Payment event, paid status, fulfilment queue transition to Processing, and the confirmation audit commit together. Repeated settlement does not repeat stock decrement or fulfilment transition.
- Guest payment links contain a random 256-bit bearer capability and display only order number, total and payment state. Treat the link as private. Payment pages send no-referrer and no-store headers. Admins can open payment status from the dashboard. Browser POSTs retain antiforgery protection; only the HMAC-authenticated webhook is exempt.
- Pending payments retain their inventory reservation. Expired, failed, conflicting or uncertain attempts require merchant reconciliation before replacement attempts or cancellation. The application deliberately does not automatically release stock, create replacement keys, issue refunds, or cancel NilePay orders. These operations need a merchant review workflow because this API guide provides no refund/cancel endpoint. The dashboard blocks fulfilment of unpaid orders and shows paid revenue only.
- No email/CRM services are connected. Processing orders form the durable queue for manual fulfilment; no external notifications are sent. The durable event ledger must not be purged with old audit logs.

## Local checks

```sh
dotnet build
dotnet run --project tests/CheckoutChecks.csproj
python3 tests/nilepay_http.py
```

The Python test uses the workspace `.dotnet/dotnet` SDK and local system SQLite. It starts an isolated mock HTTP gateway and an HTTPS development storefront, with random credentials and a new isolated database, and stops both on completion. It never contacts NilePay, InstaPay or a bank. Tests cover retry payload/key stability, restart persistence, signatures, timestamp window, duplicate deliveries, unknown sessions, amount/currency mismatch, no settlement from return or nonconfirmed events, one durable fulfilment transition, stock retention and authenticated reconciliation.

Live merchant acceptance, SQL Server concurrency/deadlock testing, and IIS webhook reachability still require your deployment environment. Monitor NilePay delivery logs and reconcile unpaid orders when deliveries fail. Back up payment mappings and event records alongside orders.
