using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NileShop.Data;

namespace NileShop.Services;

public sealed class AccountMailSender(
    ApplicationDbContext db, IOptions<SmtpOptions> defaults,
    IDataProtectionProvider protection, ILogger<AccountMailSender> logger)
{
    public static async Task<bool> CheckAndRecordCooldownAsync(ApplicationDbContext db, string rawEmail, CancellationToken ct)
    {
        var normalized = rawEmail.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized)) return false;
        var now = DateTime.UtcNow;
        var entry = await db.EmailRateLimits.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
        if (entry is null)
        {
            db.EmailRateLimits.Add(new Models.EmailRateLimit {
                NormalizedEmail = normalized,
                WindowStartUtc = now,
                HourCount = 1,
                LastSentUtc = now
            });
            try { await db.SaveChangesAsync(ct); return true; }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); entry = await db.EmailRateLimits.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct); }
        }
        if (entry is null) return false;
        if (now - entry.LastSentUtc < TimeSpan.FromMinutes(1)) return false;
        if (now - entry.WindowStartUtc >= TimeSpan.FromHours(1))
        {
            entry.WindowStartUtc = now;
            entry.HourCount = 1;
        }
        else
        {
            if (entry.HourCount >= 5) return false;
            entry.HourCount++;
        }
        entry.LastSentUtc = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public static async Task QueueAsync(ApplicationDbContext db, IDataProtectionProvider protection, string recipient, string subject, string body, string? challengeKey = null, DateTime? expiresAtUtc = null, CancellationToken ct = default)
    {
        var protectedBody = "dp:v1:" + protection.CreateProtector("NileShop.AccountEmailQueue.v1").Protect(body);
        db.AccountEmailQueues.Add(new Models.AccountEmailQueue {
            RecipientEmail = recipient.Trim(),
            Subject = subject,
            Body = protectedBody,
            ChallengeKey = challengeKey,
            ExpiresAtUtc = expiresAtUtc,
            NextAttemptAtUtc = DateTime.UtcNow,
            PayloadVersion = 1
        });
        await db.SaveChangesAsync(ct);
    }

    public static string UnprotectBody(IDataProtectionProvider protection, string protectedBody)
    {
        var payload = protectedBody.StartsWith("dp:v1:", StringComparison.Ordinal) ? protectedBody[6..] : protectedBody;
        return protection.CreateProtector("NileShop.AccountEmailQueue.v1").Unprotect(payload);
    }

    public static async Task<(int Scrubbed, int SkippedAmbiguous)> ScrubLegacyPlaintextAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        var candidates = await db.AccountEmailQueues
            .Where(e => e.PayloadVersion == 0 && e.Body != "")
            .ToListAsync(ct);
        int scrubbed = 0, skipped = 0;
        var now = DateTime.UtcNow;
        foreach (var row in candidates)
        {
            if (row.Body.StartsWith("dp:v1:", StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }
            row.Body = "";
            row.SentAtUtc ??= now;
            row.LastError = "LegacyPlaintextScrubbed";
            scrubbed++;
        }
        if (scrubbed > 0) await db.SaveChangesAsync(ct);
        return (scrubbed, skipped);
    }
    public async Task<bool> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var smtp = defaults.Value;
        try
        {
            // Hosting configuration can restore account email access without an admin sign-in.
            var settings = smtp.Enabled ? null :
                await db.StoreMailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(timeout.Token);
            if (settings is { Enabled: true })
            {
                smtp = new SmtpOptions
                {
                    Enabled = true, EnableSsl = settings.EnableSsl, Host = settings.Host,
                    Port = settings.Port, From = settings.From, Username = settings.Username,
                    Password = string.IsNullOrEmpty(settings.EncryptedPassword) ? "" :
                        protection.CreateProtector("NileShop.SMTP.v1").Unprotect(settings.EncryptedPassword)
                };
            }
            if (!smtp.Enabled || !smtp.EnableSsl || string.IsNullOrWhiteSpace(smtp.Host) ||
                string.IsNullOrWhiteSpace(smtp.From))
            {
                logger.LogWarning("Account email requires enabled SMTP, STARTTLS, a host and a sender address.");
                return false;
            }
            // Hosting fields are often pasted with surrounding whitespace. Never trim passwords.
            using var transport = new SmtpClient(smtp.Host.Trim(), smtp.Port)
            { EnableSsl = true, UseDefaultCredentials = false };
            if (!string.IsNullOrEmpty(smtp.Username))
                transport.Credentials = new NetworkCredential(smtp.Username.Trim(), smtp.Password);
            using var message = new MailMessage(smtp.From.Trim(), recipient) { Subject = subject, Body = body };
            await transport.SendMailAsync(message, timeout.Token);
        }
        catch (SmtpException error)
        {
            // Do not log raw SMTP responses, addresses, credentials, or message contents.
            var cause = error.GetBaseException();
            var reason = cause switch
            {
                SocketException socket => $"Network/{socket.SocketErrorCode}",
                AuthenticationException => "TLS certificate or handshake failure",
                _ when (int)error.StatusCode == 535 => "SMTP credentials rejected",
                _ when error.StatusCode == SmtpStatusCode.MustIssueStartTlsFirst => "SMTP authentication or STARTTLS required",
                _ when error is SmtpFailedRecipientException => "Recipient rejected",
                _ => cause.GetType().Name
            };
            logger.LogWarning("Account email SMTP submission failed with status {StatusCode}. Reason {Reason}. Port {Port}.",
                (int)error.StatusCode, reason, smtp.Port);
            return false;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Account email could not be sent ({Reason}).",
                cancellationToken.IsCancellationRequested ? "Request cancelled" : "SMTP operation exceeded 20 seconds; check outbound connectivity");
            return false;
        }
        catch (Exception error) when (error is CryptographicException or FormatException
            or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning("Account email could not be sent ({ErrorType}).", error.GetType().Name);
            return false;
        }
        return true;
    }
}
