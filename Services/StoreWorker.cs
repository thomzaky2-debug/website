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
                using var message = new MailMessage(smtp.From, email.Order.CustomerEmail) {
                    Subject = $"NileShop · {email.Order.OrderNumber} · {email.Kind}",
                    Body = $"NileShop\nOrder / الطلب: {email.Order.OrderNumber}\nUpdate / التحديث: {email.Kind}\nTotal / الإجمالي: {email.Order.TotalAmount:0.00} EGP\n\nTrack your order / تتبع طلبك:\n{site.TrimEnd('/')}/orders/track?token={email.Order.PaymentAccessToken}\n\nKeep this private tracking link secure. احتفظ برابط التتبع الخاص بك."
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
