using System.Net;
using System.Net.Mail;
using NileShop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
namespace NileShop.Services;
public class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
}
public class StoreWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<StoreWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var client = scope.ServiceProvider.GetRequiredService<NilePayClient>();
                if (config.GetValue("StoreWorker:ReconcilePayments", true) && client.Ready) {
                    var cutoff = DateTime.UtcNow.AddMinutes(-1);
                    var ids = await db.PaymentAttempts.AsNoTracking().Where(p => p.SessionId != null && p.Order.PaymentStatus == "Unpaid" && p.GatewayStatus != "CONFIRMED" && p.GatewayStatus != "EXPIRED" && p.GatewayStatus != "CANCELLED" && p.GatewayStatus != "FAILED" && (p.LastCheckedAt == null || p.LastCheckedAt < cutoff)).OrderBy(p => p.LastCheckedAt).Select(p => p.Id).Take(10).ToListAsync(stoppingToken);
                    foreach (var id in ids) {
                        try { await scope.ServiceProvider.GetRequiredService<NilePayService>().ReconcileAsync(id, stoppingToken); }
                        catch (Exception e) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("Payment reconciliation failed for attempt {Id}: {Type}", id, e.GetType().Name); }
                    }
                }

                // Auto-release inventory for any unpaid orders whose session has expired
                var lifetime = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.CheckoutSessionLifetimeMinutes).FirstOrDefaultAsync(stoppingToken);
                if (lifetime <= 0) lifetime = 15;
                var expiredOrders = await db.Orders.AsNoTracking()
                    .Where(o => o.PaymentStatus == "Unpaid" && !o.InventoryReleased && o.PaidAt == null)
                    .Select(o => new { o.Id, o.CreatedAt, AttemptCreated = db.PaymentAttempts.Where(p => p.OrderId == o.Id).OrderByDescending(p => p.Id).Select(p => (DateTime?)p.CreatedAt).FirstOrDefault() })
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var exp in expiredOrders) {
                    var sessionStart = exp.AttemptCreated ?? exp.CreatedAt;
                    if (DateTime.UtcNow > sessionStart.AddMinutes(lifetime)) {
                        try {
                            await scope.ServiceProvider.GetRequiredService<NilePayService>().ReleaseInventoryIfUnpaidAsync(exp.Id, "Expired session worker cleanup", stoppingToken);
                        } catch (Exception e) when (!stoppingToken.IsCancellationRequested) {
                            logger.LogWarning("Worker inventory release failed for order {OrderId}: {Type}", exp.Id, e.GetType().Name);
                        }
                    }
                }

                await SendEmailsAsync(scope.ServiceProvider, stoppingToken);
            } catch (Exception e) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(e, "Store background processing failed; will retry"); }
        }
    }
    static async Task SendEmailsAsync(IServiceProvider services, CancellationToken ct)
    {
        var smtp = services.GetRequiredService<IOptions<SmtpOptions>>().Value;
        var site = services.GetRequiredService<IOptions<NilePayOptions>>().Value.PublicUrl;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var saved = smtp.Enabled ? null : await db.StoreMailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (saved is not null) {
            if (!saved.Enabled) return;
            var password = string.IsNullOrEmpty(saved.EncryptedPassword) ? "" : services.GetRequiredService<IDataProtectionProvider>().CreateProtector("NileShop.SMTP.v1").Unprotect(saved.EncryptedPassword);
            smtp = new SmtpOptions { Enabled = saved.Enabled, Host = saved.Host, Port = saved.Port, Username = saved.Username, Password = password, From = saved.From, EnableSsl = saved.EnableSsl };
        }
        if (!smtp.Enabled || !smtp.EnableSsl || string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.From) || !Uri.TryCreate(site, UriKind.Absolute, out var origin) || origin.Scheme != "https") return;
        var now = DateTime.UtcNow;
        var ids = await db.OrderEmails.AsNoTracking().Where(e => e.SentAt == null && e.NextAttemptAt <= now && e.Attempts < 8).OrderBy(e => e.Id).Select(e => e.Id).Take(10).ToListAsync(ct);
        foreach (var id in ids) {
            var claimed = await db.OrderEmails.Where(e => e.Id == id && e.SentAt == null && e.NextAttemptAt <= now).ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, now.AddMinutes(5)).SetProperty(e => e.Attempts, e => e.Attempts + 1), ct);
            if (claimed == 0) continue;
            var email = await db.OrderEmails.AsNoTracking().Include(e => e.Order).SingleAsync(e => e.Id == id, ct);
            try {
                using var transport = new SmtpClient(smtp.Host.Trim(), smtp.Port) { EnableSsl = smtp.EnableSsl, UseDefaultCredentials = false, Timeout = 20000 };
                if (!string.IsNullOrEmpty(smtp.Username)) transport.Credentials = new NetworkCredential(smtp.Username.Trim(), smtp.Password);
                var statusAr = email.Kind switch {
                    "Paid" => "تم تأكيد الدفع",
                    "Overpaid" => "تم تأكيد الدفع (مبلغ إضافي)",
                    "Confirmed" => "تم تأكيد الطلب",
                    "Processing" => "قيد التجهيز",
                    "Shipped" => "تم الشحن",
                    "Delivered" => "تم التوصيل",
                    "Cancelled" => "تم الإلغاء",
                    "Refunded" => "تم الاسترداد",
                    _ => email.Kind
                };
                using var message = new MailMessage(smtp.From, email.Order.CustomerEmail) {
                    Subject = $"Chic & Unique · الطلب {email.Order.OrderNumber} · {statusAr}",
                    Body = $"Chic & Unique\nرقم الطلب: {email.Order.OrderNumber}\nحالة الطلب: {statusAr}\nالإجمالي: {email.Order.TotalAmount:0.00} ج.م\n\nتتبع طلبك:\n{site.TrimEnd('/')}/orders/track?token={email.Order.PaymentAccessToken}\n\nاحتفظ برابط التتبع الخاص بك في مكان آمن."
                };
                message.Headers.Add("Message-ID", $"<nileshop-order-{email.OrderId}-{email.Kind}@{origin.Host}>");
                await transport.SendMailAsync(message, ct);
                await db.OrderEmails.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.SentAt, DateTime.UtcNow).SetProperty(e => e.LastError, (string?)null), ct);
            } catch (Exception e) when (!ct.IsCancellationRequested) {
                await db.OrderEmails.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastError, e.GetType().Name).SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddMinutes(Math.Min(240, Math.Pow(2, email.Attempts)))), ct);
            }
        }
    }
}
