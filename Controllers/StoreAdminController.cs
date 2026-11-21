using System.Text;
using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
namespace NileShop.Controllers;
[Authorize(Roles = "Admin")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class StoreAdminController(ApplicationDbContext db, CustomerAdminService customers, WalletService wallet, ProductImageService images, OrderOperations operations, NilePayService payments, AuditService audit, IDataProtectionProvider protection, NilePayClient gateway, IOptions<NilePayOptions> gatewayOptions) : Controller
{
    public async Task<IActionResult> Customers(string? q, string segment = "all", int page = 1)
    {
        q = q?.Trim(); if (q?.Length > 100) q = q[..100];
        page = Math.Clamp(page, 1, 100000);
        if (segment is not ("all" or "with-orders" or "without-orders")) segment = "all";
        var query = customers.Customers();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(user => (user.Email != null && user.Email.Contains(q))
            || (user.PhoneNumber != null && user.PhoneNumber.Contains(q))
            || db.UserClaims.Any(claim => claim.UserId == user.Id && (claim.ClaimType == CustomerProfileViewModel.FullNameClaim || claim.ClaimType == CustomerProfileViewModel.WhatsAppClaim || claim.ClaimType == CustomerProfileViewModel.AddressClaim) && claim.ClaimValue != null && claim.ClaimValue.Contains(q)));
        if (segment == "with-orders") query = query.Where(user => db.Orders.Any(order => order.CustomerUserId == user.Id));
        if (segment == "without-orders") query = query.Where(user => !db.Orders.Any(order => order.CustomerUserId == user.Id));
        var count = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(count / 30d)));
        var users = await query.OrderBy(user => user.Email).ThenBy(user => user.Id).Skip((page - 1) * 30).Take(30).ToListAsync();
        return View(new CustomersAdminViewModel(await customers.RowsAsync(users), q, page, (int)Math.Ceiling(count / 30d), count, segment));
    }
    [HttpGet]
    public async Task<IActionResult> Customer(string id, int page = 1)
    {
        if (string.IsNullOrWhiteSpace(id)) return NotFound();
        var user = await customers.Customers().SingleOrDefaultAsync(user => user.Id == id);
        if (user is null) return NotFound();
        var customer = (await customers.RowsAsync(new[] { user })).Single();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(customer.Orders / 20d)));
        var orders = await db.Orders.AsNoTracking().Where(order => order.CustomerUserId == id)
            .OrderByDescending(order => order.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
        await OrderStatusPresentation.LoadAsync(db, orders);
        var reviews = await db.ProductReviews.AsNoTracking().Where(review => review.UserId == id)
            .Include(review => review.Product).ThenInclude(product => product.Translations).OrderByDescending(review => review.Id).Take(5).ToListAsync();
        return View(new CustomerDetailAdminViewModel(customer, await wallet.GetBalanceAsync(id),
            await db.SavedProducts.CountAsync(saved => saved.UserId == id), await db.ProductReviews.CountAsync(review => review.UserId == id),
            orders, page, (int)Math.Ceiling(customer.Orders / 20d), await wallet.GetTransactionsAsync(id, 10), reviews));
    }
    public async Task<IActionResult> Reviews(int page=1)
    {
        page=Math.Clamp(page,1,100000);var query=db.ProductReviews.AsNoTracking();
        ViewData["Page"]=page;ViewData["Pages"]=(int)Math.Ceiling(await query.CountAsync()/30d);
        return View(await query.Include(r=>r.Product).ThenInclude(p=>p.Translations).OrderBy(r=>r.IsApproved).ThenByDescending(r=>r.Id).Skip((page-1)*30).Take(30).ToListAsync());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ModerateReview(int id,bool approved)
    {
        var review=await db.ProductReviews.FindAsync(id);if(review is null)return NotFound();review.IsApproved=approved;
        await audit.LogAsync("REVIEW_MODERATED","Product",$"Review {id}; approved {approved}");return RedirectToAction(nameof(Reviews));
    }
    public async Task<IActionResult> Index(string? q, string? status, int page = 1)
    {
        q = q?.Trim(); if (q?.Length > 100) q = q[..100]; page = Math.Clamp(page,1,100000);
        var query = db.Orders.AsNoTracking();
        if (!string.IsNullOrEmpty(q)) query = query.Where(o => o.OrderNumber.Contains(q) || o.CustomerName.Contains(q) || o.CustomerEmail.Contains(q));
        if (status is "Expired" or "PaymentCancelled" or "PaymentFailed" or "Incomplete" or "AwaitingPayment") query = OrderStatusPresentation.Filter(db, query, status);
        else if (status == "review") query = query.Where(o => o.NeedsPaymentReview);
        else if (status is "Overpaid" or "Underpaid") {
            var gatewayState = status == "Overpaid" ? "OVERPAID" : "UNDERPAID";
            query = query.Where(o => o.PaymentStatus == status || (o.Status == OrderStatuses.Pending && o.PaymentStatus == "Unpaid" && !o.NeedsPaymentReview && db.PaymentAttempts.Any(p => p.OrderId == o.Id && p.SessionId != null && p.GatewayStatus == gatewayState)));
        }
        else if (status is "Paid" or "Unpaid" or "Refunded") query = query.Where(o => o.PaymentStatus == status);
        else if (OrderStatuses.All.Contains(status)) query = query.Where(o => o.Status == status);
        var count = await query.CountAsync();
        var orders = await query.OrderByDescending(o => o.Id).Skip((page-1)*30).Take(30).ToListAsync();
        await OrderStatusPresentation.LoadAsync(db, orders);
        return View(new OperationsViewModel { Orders = orders, Page=page, TotalPages=(int)Math.Ceiling(count/30d), Search=q, Status=status,
            LowStock=await db.Products.AsNoTracking().Include(p=>p.Translations).Where(p=>p.IsActive && p.StockQuantity<=5).OrderBy(p=>p.StockQuantity).Take(50).ToListAsync(),
            PendingEmails=await db.OrderEmails.CountAsync(e=>e.SentAt==null), FailedEmails=await db.OrderEmails.CountAsync(e=>e.SentAt==null && e.Attempts>=8), EmailConfigured=await db.StoreMailSettings.AnyAsync(s=>s.Enabled && s.Host!="" && s.From!="") });
    }
    public async Task<IActionResult> Order(int id)
    {
        var order=await db.Orders.AsNoTracking().Include(o=>o.Items).Include(o=>o.Updates).AsSplitQuery().SingleOrDefaultAsync(o=>o.Id==id);
        if(order is null)return NotFound();
        await OrderStatusPresentation.LoadAsync(db, new[] { order });
        ViewData["Attempt"]=await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p=>p.OrderId==id);
        ViewData["Emails"]=await db.OrderEmails.AsNoTracking().Where(e=>e.OrderId==id).ToListAsync();
        return View(order);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Reconcile(int id, CancellationToken ct)
    {
        var attempt=await db.PaymentAttempts.AsNoTracking().SingleOrDefaultAsync(p=>p.OrderId==id,ct);
        try {
            if(attempt is not null) {
                if(attempt.SessionId is null) {
                    var order=await db.Orders.AsNoTracking().Include(o=>o.Items).SingleAsync(o=>o.Id==id,ct);
                    attempt=await payments.StartAsync(order,ct); // Same persisted key and payload resolve an uncertain creation.
                }
                await payments.ReconcileAsync(attempt.Id,ct);
                await audit.LogAsync("PAYMENT_RECONCILE","Order",$"Order {id}");
            }
        } catch(NilePayException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Order),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id,string reason,bool reviewed,CancellationToken ct)
    {
        if(!reviewed || string.IsNullOrWhiteSpace(reason) || reason.Length>200)return BadRequest();
        if(!await db.Orders.AnyAsync(o=>o.Id==id,ct))return NotFound();
        try{await operations.CancelUnpaidAsync(id,reason,ct);TempData["Success"]="Admin_Saved";}catch(NilePayException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Order),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(int id,string reference,bool confirmed,bool restock,CancellationToken ct)
    {
        if(!confirmed || string.IsNullOrWhiteSpace(reference) || reference.Length>200)return BadRequest();
        if(!await db.Orders.AnyAsync(o=>o.Id==id,ct))return NotFound();
        try{await operations.RecordRefundAsync(id,reference.Trim(),restock,ct);TempData["Success"]="Admin_Saved";}catch(NilePayException e){TempData["Error"]=e.Message;}
        return RedirectToAction(nameof(Order),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispatch(int id,string carrier,string trackingNumber,string? trackingUrl)
    {
        if(string.IsNullOrWhiteSpace(carrier)||carrier.Length>100||string.IsNullOrWhiteSpace(trackingNumber)||trackingNumber.Length>100)return BadRequest();
        if(!string.IsNullOrWhiteSpace(trackingUrl) && (trackingUrl.Length>800 || !Uri.TryCreate(trackingUrl,UriKind.Absolute,out var url)||url.Scheme!="https"||!string.IsNullOrEmpty(url.UserInfo)))return BadRequest();
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var order=await db.Orders.SingleOrDefaultAsync(o=>o.Id==id);if(order is null)return NotFound();
        if(order.PaymentStatus is not ("Paid" or "Overpaid")||order.NeedsPaymentReview||order.Status is not (OrderStatuses.Processing or OrderStatuses.Shipped))return BadRequest();
        order.Carrier=carrier.Trim();order.TrackingNumber=trackingNumber.Trim();order.TrackingUrl=trackingUrl;
        if(order.Status!=OrderStatuses.Shipped){order.Status=OrderStatuses.Shipped;order.Updates.Add(new OrderUpdate{Status="Shipped"});db.OrderEmails.Add(new OrderEmail{OrderId=id,Kind="Shipped"});}
        await audit.LogAsync("DISPATCH","Order",order.OrderNumber);await tx.CommitAsync();return RedirectToAction(nameof(Order),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryEmail(int id)
    {
        var email=await db.OrderEmails.FindAsync(id);if(email is null)return NotFound();
        if(email.SentAt is null){email.Attempts=0;email.NextAttemptAt=DateTime.UtcNow;email.LastError=null;await audit.LogAsync("EMAIL_RETRY","Order",$"Order {email.OrderId}");}
        return RedirectToAction(nameof(Order),new{id=email.OrderId});
    }
    async Task SettingsData() {
        ViewData["Shipping"]=await db.ShippingMethods.AsNoTracking().OrderBy(s=>s.Id).ToListAsync();
        ViewData["GatewayReady"]=gateway.Ready;
        ViewData["GatewayOrigin"]=gatewayOptions.Value.ApiUrl;
        ViewData["PublicUrl"]=gatewayOptions.Value.PublicUrl;
        var mail=await db.StoreMailSettings.AsNoTracking().OrderBy(s => s.Id).FirstAsync();
        ViewData["Mail"]=new MailSettingsViewModel { Enabled=mail.Enabled,Host=mail.Host,Port=mail.Port,Username=mail.Username,HasPassword=!string.IsNullOrEmpty(mail.EncryptedPassword),From=mail.From,EnableSsl=mail.EnableSsl };
    }
    public async Task<IActionResult> Settings()
    {
        await SettingsData();
        var settings = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).FirstAsync();
        settings.ReturnsPolicyEn = ReturnPolicyWindow.Apply(settings.ReturnsPolicyEn, settings.ReturnDays, arabic: false);
        settings.ReturnsPolicyAr = ReturnPolicyWindow.Apply(settings.ReturnsPolicyAr, settings.ReturnDays, arabic: true);
        return View(settings);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(StoreSettings model)
    {
        model.FacebookUrl = NormalizeSocialUrl(model.FacebookUrl, nameof(model.FacebookUrl));
        model.InstagramUrl = NormalizeSocialUrl(model.InstagramUrl, nameof(model.InstagramUrl));
        model.TwitterUrl = NormalizeSocialUrl(model.TwitterUrl, nameof(model.TwitterUrl));
        model.WhatsAppUrl = NormalizeSocialUrl(model.WhatsAppUrl, nameof(model.WhatsAppUrl), allowPhone: true);
        if(!ModelState.IsValid){await SettingsData();return View(model);}
        model.ReturnsPolicyEn = ReturnPolicyWindow.Apply(model.ReturnsPolicyEn, model.ReturnDays, arabic: false);
        model.ReturnsPolicyAr = ReturnPolicyWindow.Apply(model.ReturnsPolicyAr, model.ReturnDays, arabic: true);
        var settings=await db.StoreSettings.OrderBy(s => s.Id).FirstAsync();model.Id=settings.Id;model.HeroImageUrl=settings.HeroImageUrl;db.Entry(settings).CurrentValues.SetValues(model);
        await db.SaveChangesAsync();
        await audit.LogAsync("STORE_SETTINGS","Store","Updated contact, social and policy settings");TempData["Success"]="Admin_Saved";return RedirectToAction(nameof(Settings));
    }
    string? NormalizeSocialUrl(string? raw, string field, bool allowPhone = false)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (allowPhone && !value.Contains("://", StringComparison.Ordinal))
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length >= 8 && digits.Length <= 15 && value.All(c => char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')'))
            {
                if (digits.StartsWith("01") && digits.Length == 11) digits = "2" + digits;
                return "https://wa.me/" + digits;
            }
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo))
            return uri.AbsoluteUri;
        ModelState.AddModelError(field, "Validation_SocialUrl");
        return value;
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> HeroImage(IFormFile? heroImage, bool reset = false)
    {
        var settings = await db.StoreSettings.OrderBy(s => s.Id).FirstAsync();
        if (reset) settings.HeroImageUrl = null;
        else {
            if (heroImage is null || heroImage.Length == 0) {
                TempData["Error"] = "Hero_Image_Required";
                return RedirectToAction(nameof(Settings), new { anchor = "appearance" });
            }
            try { settings.HeroImageUrl = (await images.SaveAsync(new[] { heroImage })).Single(); }
            catch (InvalidDataException) {
                TempData["Error"] = "Images_Invalid";
                return RedirectToAction(nameof(Settings), new { anchor = "appearance" });
            }
        }
        await audit.LogAsync("HERO_IMAGE_UPDATED", "Store", reset ? "Restored default homepage image" : "Updated homepage image");
        TempData["Success"] = "Admin_Saved";
        return RedirectToAction(nameof(Settings), new { anchor = "appearance" });
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Mail(MailSettingsViewModel model)
    {
        if(!ModelState.IsValid || model.NewPassword?.Length>500 || (model.Enabled && (string.IsNullOrWhiteSpace(model.Host)||string.IsNullOrWhiteSpace(model.From)||!model.EnableSsl)))return BadRequest();
        var mail=await db.StoreMailSettings.OrderBy(s => s.Id).FirstAsync();
        mail.Enabled=model.Enabled;mail.Host=model.Host?.Trim()??"";mail.Port=model.Port;mail.Username=model.Username?.Trim()??"";mail.From=model.From?.Trim()??"";mail.EnableSsl=model.EnableSsl;
        if(!string.IsNullOrEmpty(model.NewPassword))mail.EncryptedPassword=protection.CreateProtector("NileShop.SMTP.v1").Protect(model.NewPassword);
        await db.SaveChangesAsync();
        await audit.LogAsync("SMTP_SETTINGS","Store","Updated mail delivery settings");TempData["Success"]="Admin_Saved";return RedirectToAction(nameof(Settings),new{anchor="email"});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Shipping(ShippingMethod model)
    {
        if(!ModelState.IsValid||model.MaxDays<model.MinDays||decimal.Round(model.Fee,2)!=model.Fee)return BadRequest();
        if(model.Id==0)db.ShippingMethods.Add(model);
        else {var shipping=await db.ShippingMethods.FindAsync(model.Id);if(shipping is null)return NotFound();db.Entry(shipping).CurrentValues.SetValues(model);}
        await audit.LogAsync("SHIPPING_SETTINGS","Shipping",$"Method {model.Id}");return RedirectToAction(nameof(Settings));
    }
    async Task<ReportViewModel> Report(DateTime? from,DateTime? to)
    {
        var start=(from??DateTime.UtcNow.AddDays(-30)).Date;var end=(to??DateTime.UtcNow).Date;
        if(end<start)end=start;if((end-start).TotalDays>366)start=end.AddDays(-366);
        var until=end.AddDays(1);
        var paid=await db.Orders.AsNoTracking().Where(o=>o.PaidAt>=start&&o.PaidAt<until).Select(o=>new {o.PaidAt,o.TotalAmount}).ToListAsync();
        var days=paid.GroupBy(o=>o.PaidAt!.Value.Date).Select(g=>new SalesDay(g.Key,g.Count(),g.Sum(o=>o.TotalAmount))).OrderBy(x=>x.Date).ToList();
        var refunds=(await db.Orders.AsNoTracking().Where(o=>o.RefundedAt>=start&&o.RefundedAt<until).Select(o=>o.TotalAmount).ToListAsync()).Sum();
        return new(start,end,days,refunds);
    }
    public async Task<IActionResult> Reports(DateTime? from,DateTime? to)=>View(await Report(from,to));
    public async Task<IActionResult> Export(DateTime? from,DateTime? to)
    {
        var report=await Report(from,to);var csv="Date,Orders,CollectedEGP\r\n"+string.Join("\r\n",report.Days.Select(d=>$"{d.Date:yyyy-MM-dd},{d.Orders},{d.Collected.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)}"));
        return File(Encoding.UTF8.GetBytes(csv),"text/csv","nileshop-sales.csv");
    }
}
