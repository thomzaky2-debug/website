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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var ticks = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                await using var scope = scopes.CreateAsyncScope();
                await SendAccountEmailsAsync(scope.ServiceProvider, stoppingToken);
                if (++ticks % 15 != 0) continue;
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

                // Auto-release inventory for expired unpaid orders in bounded oldest-first batches
                var lifetime = await db.StoreSettings.AsNoTracking().OrderBy(s => s.Id).Select(s => s.CheckoutSessionLifetimeMinutes).FirstOrDefaultAsync(stoppingToken);
                if (lifetime <= 0) lifetime = 15;
                var expiryCutoff = DateTime.UtcNow.AddMinutes(-lifetime);
                const int batchSize = 20;
                const int maxBatches = 10;
                for (var batch = 0; batch < maxBatches && !stoppingToken.IsCancellationRequested; batch++) {
                    var expiredIds = await db.Orders.AsNoTracking()
                        .Where(o => o.PaymentStatus == "Unpaid" && !o.InventoryReleased && o.PaidAt == null && o.Status == Models.OrderStatuses.Pending && o.CreatedAt <= expiryCutoff)
                        .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
                        .Select(o => o.Id)
                        .Take(batchSize)
                        .ToListAsync(stoppingToken);

                    if (expiredIds.Count == 0) break;
                    foreach (var expId in expiredIds) {
                        if (stoppingToken.IsCancellationRequested) break;
                        try {
                            await scope.ServiceProvider.GetRequiredService<NilePayService>().ReleaseInventoryIfUnpaidAsync(expId, "Expired session worker cleanup", stoppingToken);
                        } catch (Exception e) when (!stoppingToken.IsCancellationRequested) {
                            logger.LogWarning("Worker inventory release failed for order {OrderId}: {Type}", expId, e.GetType().Name);
                        }
                    }
                    if (expiredIds.Count < batchSize) break;
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
    public static async Task SendAccountEmailsAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var mail = services.GetRequiredService<AccountMailSender>();
        var otp = services.GetRequiredService<EmailOtp>();
        var protection = services.GetRequiredService<IDataProtectionProvider>();
        var now = DateTime.UtcNow;
        var items = await db.AccountEmailQueues
            .Where(e => !e.Quarantined && e.SentAtUtc == null && e.NextAttemptAtUtc <= now && e.Attempts < 5)
            .OrderBy(e => e.Id)
            .Take(10)
            .ToListAsync(ct);

        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;
            if ((item.ExpiresAtUtc.HasValue && item.ExpiresAtUtc.Value <= now) ||
                (!string.IsNullOrEmpty(item.ChallengeKey) && !otp.IsChallengeActive(item.ChallengeKey)))
            {
                item.SentAtUtc = now;
                item.LastError = "SupersededOrExpired";
                item.Body = "";
                await db.SaveChangesAsync(ct);
                continue;
            }

            string body;
            try
            {
                body = AccountMailSender.UnprotectBody(protection, item.Body);
            }
            catch
            {
                // Fail closed without destroying body: quarantine for human review and exclude from dispatch & purge.
                item.Quarantined = true;
                item.LastError = "DecryptFailed";
                await db.SaveChangesAsync(ct);
                continue;
            }

            item.Attempts++;
            var sent = await mail.SendAsync(item.RecipientEmail, item.Subject, body, ct);
            if (sent)
            {
                item.SentAtUtc = DateTime.UtcNow;
                item.LastError = null;
                item.Body = "";
            }
            else
            {
                item.LastError = "DeliveryFailed";
                item.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, item.Attempts)));
                if (item.Attempts >= 5) item.Body = "";
            }
            await db.SaveChangesAsync(ct);
        }

        await db.AccountEmailQueues
            .Where(e => !e.Quarantined && (e.SentAtUtc != null || e.Attempts >= 5) && e.CreatedAtUtc < now.AddHours(-24))
            .ExecuteDeleteAsync(ct);
    }
}
