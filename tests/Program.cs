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
