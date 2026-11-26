using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
DevelopmentSqlite.Initialize();
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var c = new Category { Slug="test", Translations=[new(){LanguageCode="en",Name="Test"}] };
var p = new Product { Slug="test-product",Category=c,Price=10.25m,StockQuantity=3,Translations=[new(){LanguageCode="en",Name="Test product",FullDescription="Test"}] };
var unavailable = new Product { Slug="unavailable",Category=c,Price=5,StockQuantity=0 };
db.Products.AddRange(p,unavailable);await db.SaveChangesAsync();
var service = new CheckoutService(db);
CheckoutViewModel Customer(string token)=>new(){InstaPayUsername="ahmed.mohammed",CheckoutToken=token,CustomerName="Test",CustomerEmail="test@example.com",CustomerPhone="01234567890",ShippingAddress="Test address"};
void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
var order=await service.PlaceAsync(Customer("token-one"),new Dictionary<int,int>{{p.Id,2}});
Check(order.TotalAmount==20.5m,"server-calculated decimal total");
Check(await db.Products.Where(x=>x.Id==p.Id).Select(x=>x.StockQuantity).SingleAsync()==1,"inventory decremented");
var retry=await service.PlaceAsync(Customer("token-one"),new Dictionary<int,int>{{p.Id,2}});
Check(retry.Id==order.Id && await db.Orders.CountAsync()==1,"idempotent checkout retry");
try{await service.PlaceAsync(Customer("token-two"),new Dictionary<int,int>{{p.Id,1},{unavailable.Id,1}});throw new Exception("Expected stock failure");}catch(CheckoutException){}
Check(await db.Products.Where(x=>x.Id==p.Id).Select(x=>x.StockQuantity).SingleAsync()==1,"partial stock update rolled back");
Check(await db.Orders.CountAsync()==1,"failed checkout creates no order");
CultureInfo.CurrentUICulture=new CultureInfo("ar");Check(CatalogService.Name(p)=="Test product","English translation fallback");
Check(!OrderStatuses.CanTransition("Cancelled","Pending") && !OrderStatuses.CanTransition("Shipped","Cancelled"),"terminal order transitions rejected");
Check(OrderStatuses.CanTransition("Pending","Cancelled"),"pending cancellation allowed");
Console.WriteLine("All checkout checks passed.");

Check(order.InstaPaySenderHandle == "ahmed.mohammed@instapay", "username suffix appended once and saved");
Check(InstaPayHandle.FromUsername("  ahmed.mohammed  ") == "ahmed.mohammed@instapay", "username whitespace trimmed");
Check(InstaPayHandle.FromUsername("ahmed@instapay") is null && InstaPayHandle.FromUsername("a b") is null && InstaPayHandle.FromUsername("") is null, "invalid or full handles rejected");
try { var changed = Customer("token-one"); changed.InstaPayUsername = "another.user"; await service.PlaceAsync(changed, new Dictionary<int,int>{{p.Id,1}}); throw new Exception("Expected immutable sender failure"); } catch (CheckoutException e) { Check(e.Message == "InstaPay_Locked", "existing order sender cannot change on retry"); }

var options=Microsoft.Extensions.Options.Options.Create(new NilePayOptions());
var gateway=new NilePayClient(new HttpClient(),options,new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName="Development" });
var payments=new NilePayService(db,gateway,options);
var operations=new OrderOperations(db,gateway,payments);
db.ChangeTracker.Clear();
await operations.CancelUnpaidAsync(order.Id,"Customer cancellation",default);
db.ChangeTracker.Clear();await operations.CancelUnpaidAsync(order.Id,"Retry",default);
Check(await db.Products.Where(x=>x.Id==p.Id).Select(x=>x.StockQuantity).SingleAsync()==3,"cancellation releases inventory exactly once");
db.PaymentAttempts.Add(new PaymentAttempt {OrderId=order.Id,IdempotencyKey="test-late",SessionId="late-session",RequestJson="{}"});await db.SaveChangesAsync();db.ChangeTracker.Clear();
await payments.ApplyAsync(new NileCheckout("late-session","CONFIRMED",order.TotalAmount,"EGP",null),"late-event",default);
var late=await db.Orders.AsNoTracking().SingleAsync(o=>o.Id==order.Id);
Check(late.PaymentStatus=="Paid" && late.NeedsPaymentReview && late.Status=="Cancelled","late confirmation flags released inventory for review");
db.ChangeTracker.Clear();await operations.RecordRefundAsync(order.Id,"external-refund",true,default);
db.ChangeTracker.Clear();await operations.RecordRefundAsync(order.Id,"external-refund",true,default);
Check(await db.Products.Where(x=>x.Id==p.Id).Select(x=>x.StockQuantity).SingleAsync()==3,"repeated refund never duplicates released inventory");
await payments.ApplyAsync(new NileCheckout("late-session","CONFIRMED",order.TotalAmount,"EGP",null),"later-event",default);
Check(await db.Orders.AsNoTracking().Where(o=>o.Id==order.Id).Select(o=>o.PaymentStatus).SingleAsync()=="Refunded","late duplicate confirmation cannot undo refund");
var wrongOwner = Customer("token-one"); wrongOwner.CustomerUserId = "other-customer";
try { await service.PlaceAsync(wrongOwner, new Dictionary<int,int>{{p.Id,1}}); throw new Exception("Expected ownership rejection"); }
catch (CheckoutException) { Check(true,"checkout retry cannot move an order between accounts"); }
db.ChangeTracker.Clear();
var securityOrder = await service.PlaceAsync(Customer("security-payment"), new Dictionary<int,int>{{p.Id,1}});
db.PaymentAttempts.Add(new PaymentAttempt {OrderId=securityOrder.Id,IdempotencyKey="security",SessionId="security-session",RequestJson="{}"});
await db.SaveChangesAsync(); db.ChangeTracker.Clear();
try { await payments.ApplyAsync(new NileCheckout("security-session","CONFIRMED",securityOrder.TotalAmount,"EGP",null,0.01m),"invalid-confirmation",default); throw new Exception("Expected underfunded confirmation rejection"); }
catch (NilePayException) { Check(true,"CONFIRMED cannot settle an explicitly underfunded transfer"); }
Check(await db.Orders.AsNoTracking().Where(o=>o.Id==securityOrder.Id).Select(o=>o.PaymentStatus).SingleAsync()=="Unpaid" && !await db.PaymentEvents.AnyAsync(e=>e.EventId=="invalid-confirmation"),"rejected confirmation leaves payment and event ledger unchanged");
db.Users.Add(new Microsoft.AspNetCore.Identity.IdentityUser {Id="security-owner",UserName="security@example.test",NormalizedUserName="SECURITY@EXAMPLE.TEST"});
await db.SaveChangesAsync();
await db.Orders.Where(o=>o.Id==order.Id).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.CustomerUserId,"security-owner"));
db.ChangeTracker.Clear();
await payments.ApplyAsync(new NileCheckout("late-session","OVERPAID",order.TotalAmount,"EGP",null,order.TotalAmount+50),"late-overpay-refund",default);
Check(!await db.WalletTransactions.AnyAsync(w=>w.UserId=="security-owner") && await db.Orders.Where(o=>o.Id==order.Id).Select(o=>o.PaymentStatus).SingleAsync()=="Refunded","late overpayment event cannot credit a refunded order wallet");
await db.Orders.Where(o=>o.Id==securityOrder.Id).ExecuteUpdateAsync(s=>s.SetProperty(o=>o.CustomerUserId,"security-owner"));
db.ChangeTracker.Clear();
await payments.ApplyAsync(new NileCheckout("security-session","OVERPAID",securityOrder.TotalAmount,"EGP",null,securityOrder.TotalAmount+5),"valid-excess",default);
db.ChangeTracker.Clear();
await payments.ApplyAsync(new NileCheckout("security-session","OVERPAID",securityOrder.TotalAmount,"EGP",null,securityOrder.TotalAmount+5),"repeated-valid-excess",default);
Check(await new WalletService(db).GetBalanceAsync("security-owner")==5m && await db.WalletTransactions.CountAsync(w=>w.OrderId==securityOrder.Id)==1,"legitimate overpayment credits the excess once across different event IDs");
var priceForm=new ProductFormViewModel {Price=1.001m};
Check(priceForm.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(priceForm)).Any(e=>e.ErrorMessage=="Validation_PricePrecision"),"fractional piastres rejected before catalog save");

var exact=new Product {Slug="red-leather-bag",CategoryId=c.Id,Price=50,StockQuantity=2,Translations=[new(){LanguageCode="en",Name="Red leather bag",FullDescription="Details"},new(){LanguageCode="ar",Name="حقيبة حمراء",FullDescription="تفاصيل"}]};
var descriptionOnly=new Product {Slug="plain-bag",CategoryId=c.Id,Price=40,StockQuantity=2,Translations=[new(){LanguageCode="en",Name="Plain bag",ShortDescription="Red leather bag accessory",FullDescription="Details"}]};
db.Products.AddRange(exact,descriptionOnly);await db.SaveChangesAsync();
var active=db.Products.AsNoTracking().Where(x=>x.IsActive);
var mixed=await CatalogSearch.Match(active," LEATHER   RED ").Select(x=>x.Id).ToListAsync();
Check(mixed.Contains(exact.Id),"search matches case-insensitive words in any order");
Check(await CatalogSearch.Rank(CatalogSearch.Match(active,"red leather bag"),"red leather bag").Select(x=>x.Id).FirstAsync()==exact.Id,"exact product name outranks description-only match");
Check(await CatalogSearch.Match(active,"حمراء حقيبة").AnyAsync(x=>x.Id==exact.Id),"Arabic multiword search works across translations");

var displayOrder = new Order();
Check(OrderStatusPresentation.Key(displayOrder)=="Order_Checkout_Incomplete","checkout without a session is labeled incomplete");
foreach(var state in new[]{("EXPIRED","Order_Payment_Expired"),("FAILED","Order_Payment_Failed"),("CANCELLED","Order_Payment_Cancelled"),("PENDING","Order_Awaiting_Payment")}) {
    displayOrder.PaymentSessionStatus=state.Item1;
    Check(OrderStatusPresentation.Key(displayOrder)==state.Item2,"payment display state: "+state.Item1);
}
displayOrder.PaymentSessionStatus="EXPIRED";displayOrder.PaymentStatus="Paid";displayOrder.Status=OrderStatuses.Processing;
Check(OrderStatusPresentation.Key(displayOrder)==OrderStatuses.Processing,"settled order cannot display a stale session expiry");
displayOrder.PaymentStatus="Refunded";
Check(OrderStatusPresentation.Key(displayOrder)=="Refunded","refund status wins over session and fulfilment state");
displayOrder.NeedsPaymentReview=true;
Check(OrderStatusPresentation.Key(displayOrder)=="Payment_Review","review flag remains prominent for late or mismatched payments");

// Regression tests: email queue encryption at rest, non-sliding cooldown, and payment release invariants
await DevelopmentSqlite.EnsureWalletSchemaAsync(db);
var dpProvider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "nileshop-test-keys-" + Guid.NewGuid().ToString("N"))));
var secretCode = "654321";
await AccountMailSender.QueueAsync(db, dpProvider, "regtest@example.com", "Test OTP", $"Code: {secretCode}", "key-1", DateTime.UtcNow.AddMinutes(10));
var queuedRow = await db.AccountEmailQueues.AsNoTracking().OrderByDescending(x => x.Id).FirstAsync();
Check(!queuedRow.Body.Contains(secretCode, StringComparison.Ordinal), "queued email payload is encrypted at rest");
Check(AccountMailSender.UnprotectBody(dpProvider, queuedRow.Body) == $"Code: {secretCode}", "queued email payload decrypts cleanly");

var firstCooldown = await AccountMailSender.CheckAndRecordCooldownAsync(db, "cooldown@example.com", CancellationToken.None);
var firstEntry = await db.EmailRateLimits.AsNoTracking().SingleAsync(x => x.NormalizedEmail == "COOLDOWN@EXAMPLE.COM");
var secondCooldown = await AccountMailSender.CheckAndRecordCooldownAsync(db, "cooldown@example.com", CancellationToken.None);
var secondEntry = await db.EmailRateLimits.AsNoTracking().SingleAsync(x => x.NormalizedEmail == "COOLDOWN@EXAMPLE.COM");
Check(firstCooldown && !secondCooldown, "per-email cooldown blocks immediate repeat");
Check(firstEntry.LastSentUtc == secondEntry.LastSentUtc, "rejected cooldown request does not extend LastSentUtc");

var releaseOrder = await service.PlaceAsync(new CheckoutViewModel { CheckoutToken = Guid.NewGuid().ToString("N"), CustomerName = "Rel", CustomerEmail = "rel@example.com", CustomerPhone = "01000000000", ShippingAddress = "Cairo", InstaPayUsername = "rel" }, new Dictionary<int, int> { [p.Id] = 1 });
var stockBeforeRelease = (await db.Products.AsNoTracking().SingleAsync(x => x.Id == p.Id)).StockQuantity;
var nilePaySvc = new NilePayService(db, new NilePayClient(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()), null!), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()));
var firstRel = await nilePaySvc.ReleaseInventoryIfUnpaidAsync(releaseOrder.Id, "test");
var secondRel = await nilePaySvc.ReleaseInventoryIfUnpaidAsync(releaseOrder.Id, "test");
var stockAfterRelease = (await db.Products.AsNoTracking().SingleAsync(x => x.Id == p.Id)).StockQuantity;
Check(firstRel && !secondRel && stockAfterRelease == stockBeforeRelease + 1, "unpaid order releases stock exactly once");

// 1. Legacy queue dry-run, apply, and reviewed clear (no destructive heuristic)
var oldFormatCipher = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Protect(dpProvider.CreateProtector("NileShop.AccountEmailQueue.v1"), "old-unprefixed-secret");
var oldCipherRow = new AccountEmailQueue { RecipientEmail = "oldcipher@example.com", Subject = "OldCipher", Body = oldFormatCipher, PayloadVersion = 0 };
var unprovenRow = new AccountEmailQueue { RecipientEmail = "legacy@example.com", Subject = "Legacy", Body = "PlaintextCode999", PayloadVersion = 0 };
db.AccountEmailQueues.AddRange(oldCipherRow, unprovenRow);
await db.SaveChangesAsync();

var dryRun = await AccountMailSender.ClassifyLegacyQueueDryRunAsync(db, dpProvider);
Check(dryRun.ProvenEncrypted.Any(x => x.Id == oldCipherRow.Id) && !dryRun.ProvenEncrypted.Any(x => x.Id == unprovenRow.Id), "dry-run classifies unprefixed ciphertext as proven-encrypted");
Check(dryRun.UnprovenUndecryptable.Any(x => x.Id == unprovenRow.Id), "dry-run classifies undecryptable row as unproven");
var afterDryOld = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == oldCipherRow.Id);
var afterDryUnproven = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == unprovenRow.Id);
Check(afterDryOld.PayloadVersion == 0 && afterDryOld.Body == oldFormatCipher && afterDryUnproven.Body == "PlaintextCode999", "dry-run modifies zero rows");

var reEnveloped = await AccountMailSender.ApplyLegacyReEnvelopeAsync(db, dpProvider);
var reEnveloped2 = await AccountMailSender.ApplyLegacyReEnvelopeAsync(db, dpProvider);
var afterApplyOld = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == oldCipherRow.Id);
var afterApplyUnproven = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == unprovenRow.Id);
Check(reEnveloped == 1 && reEnveloped2 == 0, "apply re-envelopes proven row once and is idempotent");
Check(afterApplyOld.PayloadVersion == 1 && afterApplyOld.Body.StartsWith("dp:v1:", StringComparison.Ordinal) && AccountMailSender.UnprotectBody(dpProvider, afterApplyOld.Body) == "old-unprefixed-secret", "re-enveloped row keeps valid decryptable payload with dp:v1 prefix");
Check(afterApplyUnproven.PayloadVersion == 0 && afterApplyUnproven.Body == "PlaintextCode999", "apply leaves unproven row body untouched");

var staleClear = await AccountMailSender.ClearReviewedUnprovenRowsAsync(db, [new AccountMailSender.ReviewedClearTarget(unprovenRow.Id, 0, "WRONGHASH")]);
Check(staleClear.Cleared == 0 && staleClear.SkippedChanged == 1, "reviewed clear skips row when snapshot hash does not match");
var unprovenEntry = dryRun.UnprovenUndecryptable.Single(x => x.Id == unprovenRow.Id);
var goodClear = await AccountMailSender.ClearReviewedUnprovenRowsAsync(db, [new AccountMailSender.ReviewedClearTarget(unprovenEntry.Id, unprovenEntry.PayloadVersion, unprovenEntry.BodySha256)]);
var afterClearUnproven = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == unprovenRow.Id);
Check(goodClear.Cleared == 1 && goodClear.SkippedChanged == 0 && afterClearUnproven.Body == "" && afterClearUnproven.LastError == "OperatorReviewedClear", "reviewed clear clears only explicitly matched snapshot");

// 2. Quarantine undecryptable row in dispatch (preserves body, excludes from dispatch & 24h purge)
var quarantineRow = new AccountEmailQueue { RecipientEmail = "q@example.com", Subject = "Q", Body = "lost-key-ciphertext", PayloadVersion = 1, CreatedAtUtc = DateTime.UtcNow.AddHours(-48) };
var oldExpiredRow = new AccountEmailQueue { RecipientEmail = "exp@example.com", Subject = "Exp", Body = "dp:v1:expired", PayloadVersion = 1, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5), CreatedAtUtc = DateTime.UtcNow.AddHours(-48) };
db.AccountEmailQueues.AddRange(quarantineRow, oldExpiredRow);
await db.SaveChangesAsync();

var workerServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
workerServices.AddSingleton(db);
workerServices.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(dpProvider);
workerServices.AddMemoryCache();
workerServices.AddLogging();
workerServices.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
workerServices.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new LocalTestWebEnv(Path.GetTempPath()));
workerServices.AddSingleton(Microsoft.Extensions.Options.Options.Create(new SmtpOptions()));
workerServices.AddSingleton(Microsoft.Extensions.Options.Options.Create(new WhatsAppOtpOptions()));
workerServices.AddSingleton(new HttpClient());
workerServices.AddSingleton<WhatsAppOtpClient>();
workerServices.AddSingleton<Microsoft.Extensions.Logging.ILogger<WhatsAppOtpClient>>(Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppOtpClient>.Instance);
workerServices.AddSingleton(Microsoft.Extensions.Options.Options.Create(new NilePayOptions()));
workerServices.AddSingleton<AccountMailSender>();
workerServices.AddSingleton<EmailOtp>();
using var workerSp = workerServices.BuildServiceProvider();
await StoreWorker.SendAccountEmailsAsync(workerSp, CancellationToken.None);
var afterQ = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.Id == quarantineRow.Id);
var afterExp = await db.AccountEmailQueues.AsNoTracking().SingleOrDefaultAsync(x => x.Id == oldExpiredRow.Id);
Check(afterQ.Quarantined && afterQ.LastError == "DecryptFailed" && afterQ.Body == "lost-key-ciphertext", "undecryptable row is quarantined with body preserved and survives 24h purge");
Check(afterExp is null, "expired terminal row older than 24h is purged");

// 3. True concurrent payment-vs-release race with independent DbContexts
var raceDbPath = Path.Combine(Path.GetTempPath(), "nileshop-race-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    ApplicationDbContext CreateRaceDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={raceDbPath};Default Timeout=10").Options;
        return new ApplicationDbContext(opts);
    }
    int raceOrderId;
    int raceProdId;
    using (var setupDb = CreateRaceDb())
    {
        await setupDb.Database.EnsureCreatedAsync();
        await setupDb.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
        var rc = new Category { Slug = "race-cat", Translations = [new() { LanguageCode = "en", Name = "Race" }] };
        setupDb.Categories.Add(rc); await setupDb.SaveChangesAsync();
        var rp = new Product { Slug = "race-prod", CategoryId = rc.Id, Price = 100m, StockQuantity = 5, Translations = [new() { LanguageCode = "en", Name = "RaceProd", FullDescription = "D" }] };
        setupDb.Products.Add(rp); await setupDb.SaveChangesAsync();
        raceProdId = rp.Id;
        var ro = await new CheckoutService(setupDb).PlaceAsync(new CheckoutViewModel { CheckoutToken = Guid.NewGuid().ToString("N"), CustomerName = "Race", CustomerEmail = "race@example.com", CustomerPhone = "01000000000", ShippingAddress = "Cairo", InstaPayUsername = "raceuser" }, new Dictionary<int, int> { [rp.Id] = 1 });
        raceOrderId = ro.Id;
        setupDb.PaymentAttempts.Add(new PaymentAttempt { OrderId = ro.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), SessionId = "race-sess-1", RequestJson = "{}" });
        await setupDb.SaveChangesAsync();
    }

    using var barrier = new Barrier(2);
    var tRelease = Task.Run(async () =>
    {
        using var rdb = CreateRaceDb();
        var svc = new NilePayService(rdb, new NilePayClient(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()), null!), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()));
        barrier.SignalAndWait();
        try { await svc.ReleaseInventoryIfUnpaidAsync(raceOrderId, "race"); } catch (Microsoft.Data.Sqlite.SqliteException) { }
    });
    var tPay = Task.Run(async () =>
    {
        using var pdb = CreateRaceDb();
        var svc = new NilePayService(pdb, new NilePayClient(new HttpClient(), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()), null!), Microsoft.Extensions.Options.Options.Create(new NilePayOptions()));
        barrier.SignalAndWait();
        try { await svc.ApplyAsync(new NileCheckout("race-sess-1", "CONFIRMED", 100m, "EGP", null), "race-evt-1", CancellationToken.None); } catch (Microsoft.Data.Sqlite.SqliteException) { }
    });
    await Task.WhenAll(tRelease, tPay);

    using (var verifyDb = CreateRaceDb())
    {
        var finalOrder = await verifyDb.Orders.AsNoTracking().SingleAsync(x => x.Id == raceOrderId);
        var finalProd = await verifyDb.Products.AsNoTracking().SingleAsync(x => x.Id == raceProdId);
        var invariantOk =
            (finalOrder.PaymentStatus == "Paid" && !finalOrder.InventoryReleased && finalProd.StockQuantity == 4) ||
            (finalOrder.InventoryReleased && finalOrder.Status == OrderStatuses.Cancelled && finalProd.StockQuantity == 5 && (finalOrder.PaymentStatus == "Unpaid" || finalOrder.NeedsPaymentReview));
        Check(invariantOk, "concurrent payment-vs-release ends in consistent state without double release or lost funds");
    }
}
finally
{
    try { File.Delete(raceDbPath); File.Delete(raceDbPath + "-wal"); File.Delete(raceDbPath + "-shm"); } catch { }
}

// 4. Controllable-clock SecurityStampValidator interval & Admin 8h absolute cap
var fakeClock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 24, 10, 0, 0, TimeSpan.Zero));
var authServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
authServices.AddSingleton<TimeProvider>(fakeClock);
authServices.AddLogging();
authServices.AddHttpContextAccessor();
authServices.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
authServices.AddSingleton(db);
authServices.AddIdentityCore<Microsoft.AspNetCore.Identity.IdentityUser>()
    .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole>()
    .AddSignInManager()
    .AddEntityFrameworkStores<ApplicationDbContext>();
authServices.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(o => { o.ValidationInterval = TimeSpan.FromMinutes(5); o.TimeProvider = fakeClock; });
using var authSp = authServices.BuildServiceProvider();
var userMgr = authSp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
var roleMgr = authSp.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
var signInMgr = authSp.GetRequiredService<Microsoft.AspNetCore.Identity.SignInManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
if (!await roleMgr.RoleExistsAsync("Admin")) await roleMgr.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Admin"));

var custUser = new Microsoft.AspNetCore.Identity.IdentityUser { UserName = "stampcust@example.com", Email = "stampcust@example.com", EmailConfirmed = true };
await userMgr.CreateAsync(custUser, "StrongPass!12345");
var custPrincipal = await signInMgr.CreateUserPrincipalAsync(custUser);
var custProps = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IssuedUtc = fakeClock.GetUtcNow(), ExpiresUtc = fakeClock.GetUtcNow().AddDays(14) };
var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = authSp };
authSp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext = httpCtx;
var scheme = new Microsoft.AspNetCore.Authentication.AuthenticationScheme("Identity.Application", null, typeof(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationHandler));
var cookieOpts = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions { TimeProvider = fakeClock };

await userMgr.UpdateSecurityStampAsync(custUser);
fakeClock.Advance(TimeSpan.FromMinutes(2));
var beforeIntervalCtx = new Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext(httpCtx, scheme, cookieOpts, new Microsoft.AspNetCore.Authentication.AuthenticationTicket(custPrincipal, custProps, scheme.Name));
await AuthCookieEvents.OnValidatePrincipalAsync(beforeIntervalCtx, fakeClock);
Check(beforeIntervalCtx.Principal is not null, "session remains valid before 5-minute stamp validation interval elapses");

fakeClock.Advance(TimeSpan.FromMinutes(4)); // T+6m
var afterIntervalCtx = new Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext(httpCtx, scheme, cookieOpts, new Microsoft.AspNetCore.Authentication.AuthenticationTicket(custPrincipal, custProps, scheme.Name));
await AuthCookieEvents.OnValidatePrincipalAsync(afterIntervalCtx, fakeClock);
Check(afterIntervalCtx.Principal is null, "chained SecurityStampValidator rejects session after 5-minute interval following stamp change");

var adminUser = new Microsoft.AspNetCore.Identity.IdentityUser { UserName = "stampadmin@example.com", Email = "stampadmin@example.com", EmailConfirmed = true };
await userMgr.CreateAsync(adminUser, "StrongAdmin!12345");
await userMgr.AddToRoleAsync(adminUser, "Admin");
var adminPrincipal = await signInMgr.CreateUserPrincipalAsync(adminUser);
var adminSignInTime = fakeClock.GetUtcNow();
var adminProps = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IssuedUtc = adminSignInTime, IsPersistent = true, AllowRefresh = true };
var adminSignInCtx = new Microsoft.AspNetCore.Authentication.Cookies.CookieSigningInContext(httpCtx, scheme, cookieOpts, adminPrincipal, adminProps, new Microsoft.AspNetCore.Http.CookieOptions());
await AuthCookieEvents.OnSigningInAsync(adminSignInCtx, fakeClock);
var expectedAdminExpiry = adminSignInTime.AddHours(8);
Check(adminProps.ExpiresUtc == expectedAdminExpiry && adminProps.AllowRefresh == false && adminProps.IsPersistent == false, "admin sign-in anchors 8h absolute expiry and disables refresh");

fakeClock.Advance(TimeSpan.FromMinutes(6)); // triggers SecurityStampValidator renewal check with valid stamp
var adminRenewCtx = new Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext(httpCtx, scheme, cookieOpts, new Microsoft.AspNetCore.Authentication.AuthenticationTicket(adminPrincipal, adminProps, scheme.Name));
await AuthCookieEvents.OnValidatePrincipalAsync(adminRenewCtx, fakeClock);
Check(adminRenewCtx.Principal is not null && adminRenewCtx.Properties.ExpiresUtc == expectedAdminExpiry && adminRenewCtx.Properties.AllowRefresh == false, "admin expiry stays anchored to original T+8h even when stamp validator triggers renewal");

fakeClock.Advance(TimeSpan.FromHours(8)); // past T+8h
var adminExpiredCtx = new Microsoft.AspNetCore.Authentication.Cookies.CookieValidatePrincipalContext(httpCtx, scheme, cookieOpts, new Microsoft.AspNetCore.Authentication.AuthenticationTicket(adminPrincipal, adminProps, scheme.Name));
await AuthCookieEvents.OnValidatePrincipalAsync(adminExpiredCtx, fakeClock);
Check(adminExpiredCtx.Principal is null, "admin session is rejected after T+8h absolute cap");

// 5. Cross-process DataProtection key persistence
var keyDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "nileshop-dp-persist-" + Guid.NewGuid().ToString("N")));
var dpA = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(keyDir);
var dpB = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(keyDir);
var protectedSynthetic = Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Protect(dpA.CreateProtector("NileShop.AccountEmailQueue.v1"), "synthetic-throwaway-payload");
Check(Microsoft.AspNetCore.DataProtection.DataProtectionCommonExtensions.Unprotect(dpB.CreateProtector("NileShop.AccountEmailQueue.v1"), protectedSynthetic) == "synthetic-throwaway-payload", "persisted DataProtection key ring decrypts across provider instances");

// 3. Upload validation (magic bytes & random filename)
var imgSvc = new ProductImageService(new LocalTestWebEnv(Path.GetTempPath()));
var validPng = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13 };
var savedPaths = await imgSvc.SaveAsync([new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(validPng), 0, validPng.Length, "file", "evil../../name.png")]);
Check(savedPaths.Count == 1 && savedPaths[0].StartsWith("/assets/uploads/", StringComparison.Ordinal) && !savedPaths[0].Contains("evil"), "valid PNG accepted with server-generated random filename");
var invalidRejected = false;
try { await imgSvc.SaveAsync([new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>")), 0, 25, "file", "xss.png")]); }
catch (InvalidDataException) { invalidRejected = true; }
Check(invalidRejected, "invalid file bytes rejected even with .png extension");


// Presentation money format: invariant culture, dot decimal, two decimals.
Check(MoneyText.Format(1234.567m)=="1,234.57","money format groups with dot decimal");
CultureInfo.CurrentUICulture=new CultureInfo("ar");
Check(MoneyText.Format(235m)=="235.00","money format culture-independent");
Check(WhatsAppOtpClient.NormalizePhone("01065466760")=="+201065466760","egyptian local phone normalized");
Check(WhatsAppOtpClient.NormalizePhone("+201065466760")=="+201065466760","international phone kept");
Check(WhatsAppOtpClient.NormalizePhone("00201065466760")=="+201065466760","00-prefixed phone normalized");
var stub = new HttpClient(new DelegateStub()) { BaseAddress = new Uri("https://otp.example") };
var otpClient = new WhatsAppOtpClient(stub, Microsoft.Extensions.Options.Options.Create(new WhatsAppOtpOptions { Enabled = true, BaseUrl = "https://otp.example", ApiKey = "test-key" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppOtpClient>.Instance);
Check(await otpClient.SendAsync("+201065466760", CancellationToken.None), "whatsapp send accepted with challengeId");
Check(await otpClient.VerifyAsync("+201065466760", "123456", CancellationToken.None) == true, "whatsapp verify true");
Check(otpClient.Configured, "whatsapp client configured");
var emailVerified = new EmailOtp.Verified("u1", "s", "/", false, false, null);
var waVerified = new EmailOtp.Verified("u1", "s", "/", false, true, "+201065466760");
Check(EmailOtp.ShouldConfirmEmail(emailVerified), "email challenge confirms email");
Check(!EmailOtp.ShouldConfirmPhone(emailVerified, "+201065466760"), "email challenge never confirms phone");
Check(!EmailOtp.ShouldConfirmEmail(waVerified), "whatsapp challenge never confirms email");
Check(EmailOtp.ShouldConfirmPhone(waVerified, "01065466760"), "whatsapp challenge confirms matching current phone");
Check(!EmailOtp.ShouldConfirmPhone(waVerified, "+20101234567"), "phone changed after challenge does not confirm");
Check(EmailOtp.ShouldUseWhatsApp(true, "01065466760"), "whatsapp gate applies when configured and phone present");
Check(!EmailOtp.ShouldUseWhatsApp(true, null), "no phone falls back to email channel");
Check(!EmailOtp.ShouldUseWhatsApp(false, "01065466760"), "whatsapp disabled keeps email channel");
Check(!new WhatsAppOtpClient(stub, Microsoft.Extensions.Options.Options.Create(new WhatsAppOtpOptions { Enabled = true, BaseUrl = "http://insecure.example", ApiKey = "k" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppOtpClient>.Instance).Configured, "whatsapp client refuses non-https base url");

// Phone-first signup flow, service level with stubbed WhatsApp transport:
// send a WhatsApp challenge, verify it, and confirm channel-separated semantics.
{
    var flowCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
    var waClient = new WhatsAppOtpClient(stub, Microsoft.Extensions.Options.Options.Create(new WhatsAppOtpOptions { Enabled = true, BaseUrl = "https://otp.example", ApiKey = "k" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppOtpClient>.Instance);
    var flowConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
    var flowOtp = new EmailOtp(flowCache, db, dpProvider, flowConfig, new LocalTestWebEnv(Path.GetTempPath()), waClient);
    var session = new FakeSession();
    var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext { Session = session };
    var flowUser = new IdentityUser { Id = Guid.NewGuid().ToString(), Email = "flow@example.com", SecurityStamp = Guid.NewGuid().ToString(), PhoneNumber = "01065466760" };
    var sent = await flowOtp.SendAsync(ctx, flowUser, "/Account", admin: false);
    Check(sent && flowOtp.IsPendingRemote(session), "phone-first send issues a WhatsApp challenge");
    var verifiedFlow = await flowOtp.VerifyAsync(session, "123456", CancellationToken.None);
    Check(verifiedFlow is { Remote: true } && verifiedFlow.Phone == "+201065466760", "whatsapp verify returns confirmed phone, not email");
    Check(EmailOtp.ShouldConfirmPhone(verifiedFlow!, flowUser.PhoneNumber) && !EmailOtp.ShouldConfirmEmail(verifiedFlow!), "phone-first verify confirms phone only");
    Check(!flowOtp.IsPending(session), "verified challenge is consumed");
    var replay = await flowOtp.VerifyAsync(session, "123456", CancellationToken.None);
    Check(replay is null, "consumed challenge cannot be replayed");
    // Email-channel challenge (forceEmail) after phone verify: confirms email only.
    // Reset the per-email send cooldown to simulate the sequential signup flow.
    var rl = await db.EmailRateLimits.SingleOrDefaultAsync(x => x.NormalizedEmail == "FLOW@EXAMPLE.COM");
    Check(rl is null, "whatsapp send does not consume the email cooldown");
    if (rl is not null) { rl.LastSentUtc = DateTime.UtcNow.AddMinutes(-2); await db.SaveChangesAsync(); }
    var sentEmail = await flowOtp.SendAsync(ctx, flowUser, "/Account", admin: false, forceEmail: true);
    Check(sentEmail && flowOtp.IsPending(session) && !flowOtp.IsPendingRemote(session), "forceEmail issues an email challenge even with WhatsApp configured");
}

// Atomic confirmed-phone ownership: concurrent claims for the same normalized
// phone resolve to exactly one owner, enforced by the database primary key.
{
    var claimDbPath = Path.Combine(Path.GetTempPath(), "nileshop-claim-" + Guid.NewGuid().ToString("N") + ".db");
    try
    {
        ApplicationDbContext CreateClaimDb()
        {
            var o = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={claimDbPath};Default Timeout=10").Options;
            return new ApplicationDbContext(o);
        }
        using (var setupDb = CreateClaimDb()) { await setupDb.Database.EnsureCreatedAsync(); }
        var claimResults = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var cx = CreateClaimDb();
            return await PhoneOwnership.TryClaimAsync(cx, "user-" + i, "+201065466760");
        }));
        Check(claimResults.Count(x => x) == 1, "concurrent claims for same phone yield exactly one owner");
        var ownerRow = await PhoneOwnership.GetOwnerAsync(CreateClaimDb(), "+201065466760");
        Check(ownerRow is not null && claimResults.Select((ok, i) => (ok, i)).Where(t => t.ok).All(t => ownerRow.UserId == "user-" + t.i), "owner row matches the winning claim");
        await using (var claimDb2 = CreateClaimDb())
        {
            Check(await PhoneOwnership.TryClaimAsync(claimDb2, ownerRow!.UserId, "+201065466760"), "same owner reclaim is idempotent");
            Check(!await PhoneOwnership.TryClaimAsync(claimDb2, "user-other", "+201065466760"), "different owner cannot claim while held");
            await PhoneOwnership.ReleaseForUserAsync(claimDb2, ownerRow.UserId);
            Check(await PhoneOwnership.TryClaimAsync(claimDb2, "user-other", "+201065466760"), "released phone becomes claimable");
        }
    }
    finally { foreach (var f in new[] { claimDbPath, claimDbPath + "-wal", claimDbPath + "-shm" }) if (File.Exists(f)) File.Delete(f); }
}

// Purpose binding: no challenge may sign in a phone-bearing, phone-unconfirmed
// account while the WhatsApp gate is configured.
Check(EmailOtp.ShouldBlockSignInWithoutPhone(true, "01065466760", false), "email challenge cannot sign in unconfirmed-phone account with WhatsApp gate on");
Check(!EmailOtp.ShouldBlockSignInWithoutPhone(true, "01065466760", true), "confirmed phone never blocks sign-in");
Check(!EmailOtp.ShouldBlockSignInWithoutPhone(true, null, false), "phoneless account (admin) never blocked");
Check(!EmailOtp.ShouldBlockSignInWithoutPhone(false, "01065466760", false), "WhatsApp disabled keeps legacy email sign-in");

// Interrupted-signup gate: phone verify with unconfirmed email defers sign-in
Check(EmailOtp.ShouldDeferSignInForEmailGate(waVerified, emailConfirmed: false), "phone verify with unconfirmed email resumes email gate instead of signing in");
Check(!EmailOtp.ShouldDeferSignInForEmailGate(waVerified, emailConfirmed: true), "phone verify with confirmed email signs in normally");
Check(!EmailOtp.ShouldDeferSignInForEmailGate(emailVerified, emailConfirmed: false), "email challenge itself never defers sign-in");

sealed class FakeSession : Microsoft.AspNetCore.Http.ISession
{
    private readonly Dictionary<string, byte[]> _store = new();
    public IEnumerable<string> Keys => _store.Keys;
    public string Id { get; } = Guid.NewGuid().ToString();
    public bool IsAvailable => true;
    public void Clear() => _store.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public void Set(string key, byte[] value) => _store[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
}

sealed class LocalTestWebEnv(string webRoot) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string WebRootPath { get; set; } = webRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "NileShop";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = webRoot;
    public string EnvironmentName { get; set; } = "Development";
}

sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

sealed class DelegateStub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = request.RequestUri!.AbsolutePath.Contains("send") ? "{\"challengeId\":\"abc\",\"expiresInSeconds\":600}" : "{\"verified\":true}";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}
