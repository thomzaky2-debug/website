using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NileShop.Data;
using NileShop.Models;
using NileShop.Services;

var phase = args.FirstOrDefault() ?? throw new ArgumentException("Pass 'seed' or 'verify'.");
var connStr = Environment.GetEnvironmentVariable("STAGING_SQL_CONNECTION")
    ?? throw new InvalidOperationException("STAGING_SQL_CONNECTION is required.");
var keyDirPath = Environment.GetEnvironmentVariable("STAGING_KEYS_PATH")
    ?? Path.Combine(Path.GetTempPath(), "nileshop-staging-keys");
Directory.CreateDirectory(keyDirPath);

var dbOpts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connStr).Options;
// The seed phase runs against the OLD baseline schema, so it must not write columns that later migrations add.
await using var db = phase == "seed" ? new PreMigrationSeedContext(dbOpts) : new ApplicationDbContext(dbOpts);

static IDataProtectionProvider CreateProvider(string dir)
{
    var sc = new ServiceCollection();
    sc.AddDataProtection().SetApplicationName("NileShop").PersistKeysToFileSystem(new DirectoryInfo(dir));
    return sc.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
}

static void Check(bool cond, string msg)
{
    if (!cond) throw new Exception("FAIL: " + msg);
    Console.WriteLine("PASS: " + msg);
}

const string syntheticSmtpSecret = "synthetic-staging-smtp-fixture-only";
const string syntheticOldQueuePayload = "synthetic-staging-old-queue-fixture";

if (phase == "seed")
{
    var dp = CreateProvider(keyDirPath);
    var cat = new Category { Slug = "staging-cat", Translations = [new() { LanguageCode = "en", Name = "Staging Category" }] };
    db.Categories.Add(cat);
    await db.SaveChangesAsync();

    var prod = new Product { Slug = "staging-prod", CategoryId = cat.Id, Price = 125.50m, StockQuantity = 10, Translations = [new() { LanguageCode = "en", Name = "Staging Product", FullDescription = "Synthetic" }] };
    db.Products.Add(prod);
    await db.SaveChangesAsync();

    var order = new Order
    {
        OrderNumber = "NILE-STAGING-0001",
        CheckoutToken = "staging-tok-0001",
        CustomerName = "Staging User",
        CustomerEmail = "staging@example.test",
        CustomerPhone = "01000000000",
        ShippingAddress = "Staging Address",
        TotalAmount = 125.50m,
        Status = OrderStatuses.Pending,
        PaymentStatus = "Unpaid",
        Items = [new OrderItem { ProductId = prod.Id, ProductName = "Staging Product", Quantity = 1, UnitPrice = 125.50m }]
    };
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    db.PaymentAttempts.Add(new PaymentAttempt
    {
        OrderId = order.Id,
        IdempotencyKey = "staging-idem-0001",
        SessionId = "staging-sess-0001",
        GatewayStatus = "PENDING",
        RequestJson = "{}"
    });

    var protectedSmtp = dp.CreateProtector("NileShop.SMTP.v1").Protect(syntheticSmtpSecret);
    db.StoreMailSettings.Add(new StoreMailSettings
    {
        Enabled = false,
        Host = "smtp.staging.invalid",
        Port = 587,
        Username = "staging@example.test",
        EncryptedPassword = protectedSmtp,
        From = "staging@example.test",
        EnableSsl = true
    });
    await db.SaveChangesAsync();

    Console.WriteLine($"BEFORE_COUNTS Categories={await db.Categories.CountAsync()} Products={await db.Products.CountAsync()} Orders={await db.Orders.CountAsync()} PaymentAttempts={await db.PaymentAttempts.CountAsync()} StoreMailSettings={await db.StoreMailSettings.CountAsync()}");
}
else if (phase == "verify")
{
    // 1. Fail-loud startup schema check
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('EmailRateLimits', 'AccountEmailQueues')";
        var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Check(tableCount == 2, "startup fail-loud schema check finds EmailRateLimits and AccountEmailQueues");
    }

    // 2. Pre-existing tables preserved
    var catCount = await db.Categories.CountAsync();
    var prodCount = await db.Products.CountAsync();
    var orderCount = await db.Orders.CountAsync();
    var attemptCount = await db.PaymentAttempts.CountAsync();
    var mailCount = await db.StoreMailSettings.CountAsync();
    Console.WriteLine($"AFTER_COUNTS Categories={catCount} Products={prodCount} Orders={orderCount} PaymentAttempts={attemptCount} StoreMailSettings={mailCount} AccountEmailQueues={await db.AccountEmailQueues.CountAsync()} EmailRateLimits={await db.EmailRateLimits.CountAsync()}");
    Check(catCount == 1 && prodCount == 1 && orderCount == 1 && attemptCount == 1 && mailCount == 1, "pre-existing table row counts unchanged after migration");

    var order = await db.Orders.AsNoTracking().SingleAsync(o => o.OrderNumber == "NILE-STAGING-0001");
    var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(p => p.OrderId == order.Id);
    Check(order.TotalAmount == 125.50m && order.Status == OrderStatuses.Pending && attempt.SessionId == "staging-sess-0001", "sample order reads back with intact total, status, and payment linkage");

    // 3. Pre-migration encrypted fixture decrypts under a fresh DataProtectionProvider (simulating restart)
    var dpAfterRestart = CreateProvider(keyDirPath);
    var savedMail = await db.StoreMailSettings.AsNoTracking().SingleAsync();
    var decryptedSmtp = dpAfterRestart.CreateProtector("NileShop.SMTP.v1").Unprotect(savedMail.EncryptedPassword);
    Check(decryptedSmtp == syntheticSmtpSecret, "pre-migration encrypted StoreMailSettings password decrypts after migration and restart");

    // 4. Seed post-migration legacy queue fixtures (old unprefixed ciphertext + undecryptable garbage)
    var rawUnprefixedCipher = dpAfterRestart.CreateProtector("NileShop.AccountEmailQueue.v1").Protect(syntheticOldQueuePayload);
    var oldFormatRow = new AccountEmailQueue
    {
        RecipientEmail = "staging-old@example.test",
        Subject = "Staging Old Format",
        Body = rawUnprefixedCipher,
        PayloadVersion = 0,
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5)
    };
    var garbageRow = new AccountEmailQueue
    {
        RecipientEmail = "staging-bad@example.test",
        Subject = "Staging Undecryptable",
        Body = "not-valid-dataprotection-payload",
        PayloadVersion = 0,
        CreatedAtUtc = DateTime.UtcNow.AddHours(-48)
    };
    db.AccountEmailQueues.AddRange(oldFormatRow, garbageRow);
    await db.SaveChangesAsync();
    var expectedGarbageHash = AccountMailSender.ComputeBodyHash(garbageRow.Body);

    // 5. Startup dry-run classification (read-only)
    var dryRun = await AccountMailSender.ClassifyLegacyQueueDryRunAsync(db, dpAfterRestart);
    Console.WriteLine($"DRY_RUN ProvenEncryptedCount={dryRun.ProvenEncrypted.Count} ProvenIds=[{string.Join(",", dryRun.ProvenEncrypted.Select(x => x.Id))}] UnprovenCount={dryRun.UnprovenUndecryptable.Count} UnprovenIds=[{string.Join(",", dryRun.UnprovenUndecryptable.Select(x => x.Id))}]");
    Check(dryRun.ProvenEncrypted.Count == 1 && dryRun.ProvenEncrypted[0].Id == oldFormatRow.Id, "dry-run classifies unprefixed ciphertext as ProvenEncrypted");
    Check(dryRun.UnprovenUndecryptable.Count == 1 && dryRun.UnprovenUndecryptable[0].Id == garbageRow.Id, "dry-run classifies garbage row as UnprovenUndecryptable");

    var afterDryOld = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == oldFormatRow.Id);
    var afterDryBad = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == garbageRow.Id);
    Check(afterDryOld.PayloadVersion == 0 && afterDryBad.PayloadVersion == 0 && !afterDryBad.Quarantined, "dry-run modifies zero rows");

    // 6. Dispatch quarantines undecryptable row with body intact and excludes from 24h purge (SMTP disabled)
    var sc = new ServiceCollection();
    sc.AddSingleton(db);
    sc.AddSingleton<IDataProtectionProvider>(dpAfterRestart);
    sc.AddMemoryCache();
    sc.AddLogging();
    sc.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    sc.AddSingleton<IHostEnvironment>(new StagingEnv());
    sc.AddSingleton(Options.Create(new SmtpOptions { Enabled = false }));
    sc.AddSingleton(Options.Create(new NilePayOptions()));
    sc.AddSingleton(Options.Create(new WhatsAppOtpOptions()));
    sc.AddSingleton(sp2 => new WhatsAppOtpClient(new HttpClient(), sp2.GetRequiredService<IOptions<WhatsAppOtpOptions>>(), sp2.GetRequiredService<ILogger<WhatsAppOtpClient>>()));
    sc.AddSingleton<AccountMailSender>();
    sc.AddSingleton<EmailOtp>();
    using var sp = sc.BuildServiceProvider();
    await StoreWorker.SendAccountEmailsAsync(sp, CancellationToken.None);

    var afterDispatchBad = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == garbageRow.Id);
    Console.WriteLine($"QUARANTINE_CHECK RowId={afterDispatchBad.Id} Quarantined={afterDispatchBad.Quarantined} LastError={afterDispatchBad.LastError} BodyHashMatch={AccountMailSender.ComputeBodyHash(afterDispatchBad.Body) == expectedGarbageHash}");
    Check(afterDispatchBad.Quarantined && afterDispatchBad.LastError == "DecryptFailed" && AccountMailSender.ComputeBodyHash(afterDispatchBad.Body) == expectedGarbageHash, "undecryptable row is quarantined with body preserved and excluded from 24h purge");

    // 7. ApplyLegacyReEnvelopeAsync once + idempotency check (no ClearReviewedUnprovenRowsAsync)
    var applied1 = await AccountMailSender.ApplyLegacyReEnvelopeAsync(db, dpAfterRestart);
    var applied2 = await AccountMailSender.ApplyLegacyReEnvelopeAsync(db, dpAfterRestart);
    var afterApplyOld = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == oldFormatRow.Id);
    var afterApplyBad = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == garbageRow.Id);
    Console.WriteLine($"APPLY_REENVELOPE Run1={applied1} Run2={applied2} OldRowVersion={afterApplyOld.PayloadVersion} BadRowVersion={afterApplyBad.PayloadVersion} BadRowQuarantined={afterApplyBad.Quarantined}");
    Check(applied1 == 1 && applied2 == 0, "ApplyLegacyReEnvelopeAsync re-envelopes proven row once and is idempotent");
    Check(afterApplyOld.PayloadVersion == 1 && afterApplyOld.Body.StartsWith("dp:v1:", StringComparison.Ordinal) && AccountMailSender.UnprotectBody(dpAfterRestart, afterApplyOld.Body) == syntheticOldQueuePayload, "re-enveloped row has dp:v1 prefix, version 1, and decrypts cleanly");
    Check(afterApplyBad.PayloadVersion == 0 && afterApplyBad.Quarantined && AccountMailSender.ComputeBodyHash(afterApplyBad.Body) == expectedGarbageHash, "unproven row remains untouched by ApplyLegacyReEnvelopeAsync");
}
else
{
    throw new ArgumentException($"Unknown phase: {phase}");
}

sealed class StagingEnv : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Staging";
    public string ApplicationName { get; set; } = "NileShop";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

sealed class PreMigrationSeedContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Order>().Ignore(o => o.DiscountAmount).Ignore(o => o.DiscountLabel);
    }
}
