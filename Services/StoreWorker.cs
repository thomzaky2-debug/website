using System.Net;
using System.Net.Mail;
using NileShop.Data;
using NileShop.Models;
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
                await SendWhatsAppNoticesAsync(scope.ServiceProvider, stoppingToken);
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
        var ids = await db.OrderEmails.AsNoTracking().Where(e => !e.Kind.StartsWith("WA:") && e.SentAt == null && e.NextAttemptAt <= now && e.Attempts < 8).OrderBy(e => e.Id).Select(e => e.Id).Take(10).ToListAsync(ct);
        foreach (var id in ids) {
            var claimed = await db.OrderEmails.Where(e => e.Id == id && e.SentAt == null && e.NextAttemptAt <= now).ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, now.AddMinutes(5)).SetProperty(e => e.Attempts, e => e.Attempts + 1), ct);
            if (claimed == 0) continue;
            var email = await db.OrderEmails.AsNoTracking().Include(e => e.Order).SingleAsync(e => e.Id == id, ct);
            try {
                using var transport = new SmtpClient(smtp.Host.Trim(), smtp.Port) { EnableSsl = smtp.EnableSsl, UseDefaultCredentials = false, Timeout = 20000 };
                if (!string.IsNullOrEmpty(smtp.Username)) transport.Credentials = new NetworkCredential(smtp.Username.Trim(), smtp.Password);
                var (subject, body) = CustomerMessages.Order(email.Kind, email.Order.CustomerName, email.Order.OrderNumber, email.Order.TotalAmount,
                    $"{site.TrimEnd('/')}/orders/track?token={email.Order.PaymentAccessToken}");
                using var message = new MailMessage(smtp.From, email.Order.CustomerEmail) { Subject = subject, Body = body };
                message.Headers.Add("Message-ID", $"<nileshop-order-{email.OrderId}-{email.Kind}@{origin.Host}>");
                await transport.SendMailAsync(message, ct);
                await db.OrderEmails.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.SentAt, DateTime.UtcNow).SetProperty(e => e.LastError, (string?)null), ct);
            } catch (Exception e) when (!ct.IsCancellationRequested) {
                await db.OrderEmails.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastError, e.GetType().Name).SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddMinutes(Math.Min(240, Math.Pow(2, email.Attempts)))), ct);
            }
        }
    }
    // WhatsApp copy of every order email. Each (order, status) has one "WA:<kind>" outbox row, so a notice goes out
    // once, never on repeat reconciles or webhook retries. Fire-and-forget: any failure is retried with backoff
    // (4 tries) and never touches payment or order state. Rows older than 2 hours are left alone.
    public static async Task SendWhatsAppNoticesAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = services.GetRequiredService<WhatsAppOtpClient>();
        if (!client.NotifyReady) return;
        var site = services.GetRequiredService<IOptions<NilePayOptions>>().Value.PublicUrl;
        if (!Uri.TryCreate(site, UriKind.Absolute, out var origin) || origin.Scheme != "https") return;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var fresh = now.AddHours(-2);
        var ids = await db.OrderEmails.AsNoTracking()
            .Where(e => e.Kind.StartsWith("WA:") && e.SentAt == null && e.Attempts < 4 && e.NextAttemptAt <= now && e.NextAttemptAt > fresh)
            .OrderBy(e => e.Id).Select(e => e.Id).Take(3).ToListAsync(ct);
        foreach (var id in ids)
        {
            var claimed = await db.OrderEmails.Where(e => e.Id == id && e.SentAt == null && e.NextAttemptAt <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, now.AddMinutes(10)).SetProperty(e => e.Attempts, e => e.Attempts + 1), ct);
            if (claimed == 0) continue;
            var row = await db.OrderEmails.AsNoTracking().Include(e => e.Order).SingleAsync(e => e.Id == id, ct);
            var kind = row.Kind[OrderEmail.WhatsAppPrefix.Length..];
            var order = row.Order;
            string outcome;
            try
            {
                var phone = WhatsAppOtpClient.NormalizePhone(order.CustomerPhone ?? "");
                var late = kind is "Paid" or "Overpaid" && (order.NeedsPaymentReview || order.Status == OrderStatuses.Cancelled);
                if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+[1-9]\d{6,14}$")) outcome = "skipped-no-phone";
                else if (late) outcome = "skipped-under-review";
                else
                {
                    var text = CustomerMessages.OrderWhatsApp(kind, order.CustomerName, order.OrderNumber, order.TotalAmount, order.PayableAmount,
                        $"{site.TrimEnd('/')}/orders/track?token={order.PaymentAccessToken}");
                    var result = await client.SendNoticeAsync(phone, text, ct);
                    outcome = result switch { WhatsAppOtpClient.NoticeResult.Sent => "sent", WhatsAppOtpClient.NoticeResult.Rejected => "rejected", _ => "retry" };
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { outcome = "retry"; }
            if (outcome == "retry")
            {
                if (row.Attempts >= 4) await FinishWhatsAppAsync(db, id, order.OrderNumber, kind, "failed", ct);
                else await db.OrderEmails.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.LastError, "WhatsAppRetry"), ct);
            }
            else await FinishWhatsAppAsync(db, id, order.OrderNumber, kind, outcome, ct);
        }
    }
    static async Task FinishWhatsAppAsync(ApplicationDbContext db, int id, string orderNumber, string kind, string outcome, CancellationToken ct)
    {
        await db.OrderEmails.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.SentAt, DateTime.UtcNow).SetProperty(e => e.LastError, outcome == "sent" ? (string?)null : outcome), ct);
        db.AuditLogs.Add(new AuditLog { Action = "WHATSAPP_NOTICE", EntityName = "Order", Details = $"{orderNumber}: {kind} WhatsApp notice {outcome}", UserEmail = "System" });
        await db.SaveChangesAsync(ct);
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
