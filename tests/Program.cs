using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

// 1. Legacy plaintext scrub & idempotency
db.AccountEmailQueues.Add(new AccountEmailQueue { RecipientEmail = "legacy@example.com", Subject = "Legacy", Body = "PlaintextCode999", PayloadVersion = 0 });
db.AccountEmailQueues.Add(new AccountEmailQueue { RecipientEmail = "ambig@example.com", Subject = "Ambig", Body = "dp:v1:ambiguous", PayloadVersion = 0 });
await db.SaveChangesAsync();
var (scrubbed1, skipped1) = await AccountMailSender.ScrubLegacyPlaintextAsync(db);
var (scrubbed2, skipped2) = await AccountMailSender.ScrubLegacyPlaintextAsync(db);
var legacyRow = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.RecipientEmail == "legacy@example.com");
var currentRow = await db.AccountEmailQueues.AsNoTracking().SingleAsync(x => x.RecipientEmail == "regtest@example.com");
Check(scrubbed1 == 1 && skipped1 == 1, "legacy scrub clears plaintext row and skips ambiguous dp:v1 row");
Check(legacyRow.Body == "" && legacyRow.SentAtUtc != null && legacyRow.LastError == "LegacyPlaintextScrubbed", "scrubbed row is cleared and non-dispatchable");
Check(currentRow.PayloadVersion == 1 && currentRow.Body.StartsWith("dp:v1:", StringComparison.Ordinal), "current encrypted row survives scrub untouched");
Check(scrubbed2 == 0 && skipped2 == 1, "legacy scrub is idempotent across repeated runs");

// 2. Cross-process DataProtection key persistence
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

sealed class LocalTestWebEnv(string webRoot) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string WebRootPath { get; set; } = webRoot;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "NileShop";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = webRoot;
    public string EnvironmentName { get; set; } = "Development";
}
