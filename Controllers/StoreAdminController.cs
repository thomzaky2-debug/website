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
public class StoreAdminController(ApplicationDbContext db, OrderOperations operations, NilePayService payments, AuditService audit, IDataProtectionProvider protection, NilePayClient gateway, IOptions<NilePayOptions> gatewayOptions) : Controller
{
    public async Task<IActionResult> Customers(string? q,int page=1)
    {
        q=q?.Trim();if(q?.Length>100)q=q[..100];page=Math.Clamp(page,1,100000);
        var query=db.Users.AsNoTracking().Where(u=>!db.UserRoles.Any(ur=>ur.UserId==u.Id&&db.Roles.Any(r=>r.Id==ur.RoleId&&r.Name=="Admin")));
        if(!string.IsNullOrWhiteSpace(q))query=query.Where(u=>(u.Email!=null&&u.Email.Contains(q))||(u.PhoneNumber!=null&&u.PhoneNumber.Contains(q))||db.UserClaims.Any(c=>c.UserId==u.Id&&(c.ClaimType==CustomerProfileViewModel.FullNameClaim||c.ClaimType==CustomerProfileViewModel.WhatsAppClaim)&&c.ClaimValue!=null&&c.ClaimValue.Contains(q)));
        var count=await query.CountAsync();
        var rows=await query.OrderBy(u=>u.Email).Skip((page-1)*30).Take(30).Select(u=>new CustomerAdminRow(u.Id,u.Email??"",u.PhoneNumber??"",db.UserClaims.Where(c=>c.UserId==u.Id&&c.ClaimType==CustomerProfileViewModel.FullNameClaim).Select(c=>c.ClaimValue).FirstOrDefault()??"",db.Orders.Count(o=>o.CustomerUserId==u.Id))).ToListAsync();
        return View(new CustomersAdminViewModel(rows,q,page,(int)Math.Ceiling(count/30d)));
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
        if (status == "review") query = query.Where(o => o.NeedsPaymentReview);
        else if (status is "Paid" or "Unpaid" or "Refunded") query = query.Where(o => o.PaymentStatus == status);
        else if (OrderStatuses.All.Contains(status)) query = query.Where(o => o.Status == status);
        var count = await query.CountAsync();
        return View(new OperationsViewModel { Orders = await query.OrderByDescending(o => o.Id).Skip((page-1)*30).Take(30).ToListAsync(), Page=page, TotalPages=(int)Math.Ceiling(count/30d), Search=q, Status=status,
            LowStock=await db.Products.AsNoTracking().Include(p=>p.Translations).Where(p=>p.IsActive && p.StockQuantity<=5).OrderBy(p=>p.StockQuantity).Take(50).ToListAsync(),
            PendingEmails=await db.OrderEmails.CountAsync(e=>e.SentAt==null), FailedEmails=await db.OrderEmails.CountAsync(e=>e.SentAt==null && e.Attempts>=8), EmailConfigured=await db.StoreMailSettings.AnyAsync(s=>s.Enabled && s.Host!="" && s.From!="") });
    }
    public async Task<IActionResult> Order(int id)
    {
        var order=await db.Orders.AsNoTracking().Include(o=>o.Items).Include(o=>o.Updates).AsSplitQuery().SingleOrDefaultAsync(o=>o.Id==id);
        if(order is null)return NotFound();
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
        if(order.PaymentStatus!="Paid"||order.NeedsPaymentReview||order.Status is not (OrderStatuses.Processing or OrderStatuses.Shipped))return BadRequest();
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
        var mail=await db.StoreMailSettings.AsNoTracking().FirstAsync();
        ViewData["Mail"]=new MailSettingsViewModel { Enabled=mail.Enabled,Host=mail.Host,Port=mail.Port,Username=mail.Username,HasPassword=!string.IsNullOrEmpty(mail.EncryptedPassword),From=mail.From,EnableSsl=mail.EnableSsl };
    }
    public async Task<IActionResult> Settings() { await SettingsData();return View(await db.StoreSettings.AsNoTracking().FirstAsync()); }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(StoreSettings model)
    {
        if(!ModelState.IsValid){await SettingsData();return View(model);}
        var settings=await db.StoreSettings.FirstAsync();model.Id=settings.Id;db.Entry(settings).CurrentValues.SetValues(model);
        await audit.LogAsync("STORE_SETTINGS","Store","Updated contact and policy settings");TempData["Success"]="Admin_Saved";return RedirectToAction(nameof(Settings));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Mail(MailSettingsViewModel model)
    {
        if(!ModelState.IsValid || model.NewPassword?.Length>500 || (model.Enabled && (string.IsNullOrWhiteSpace(model.Host)||string.IsNullOrWhiteSpace(model.From))))return BadRequest();
        var mail=await db.StoreMailSettings.FirstAsync();
        mail.Enabled=model.Enabled;mail.Host=model.Host?.Trim()??"";mail.Port=model.Port;mail.Username=model.Username?.Trim()??"";mail.From=model.From?.Trim()??"";mail.EnableSsl=model.EnableSsl;
        if(!string.IsNullOrEmpty(model.NewPassword))mail.EncryptedPassword=protection.CreateProtector("NileShop.SMTP.v1").Protect(model.NewPassword);
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
