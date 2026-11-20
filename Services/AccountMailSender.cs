using System.Net;
using System.Net.Mail;
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
    public async Task<bool> SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var smtp = defaults.Value;
            // Hosting configuration can restore account email access without an admin sign-in.
            var settings = smtp.Enabled ? null :
                await db.StoreMailSettings.AsNoTracking().FirstOrDefaultAsync(timeout.Token);
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
            using var transport = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = true };
            if (!string.IsNullOrEmpty(smtp.Username))
                transport.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
            using var message = new MailMessage(smtp.From, recipient) { Subject = subject, Body = body };
            await transport.SendMailAsync(message, timeout.Token);
        }
        catch (SmtpException error)
        {
            logger.LogWarning("Account email SMTP submission failed with status {StatusCode}.", (int)error.StatusCode);
            return false;
        }
        catch (Exception error) when (error is CryptographicException or FormatException
            or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning("Account email could not be sent ({ErrorType}).", error.GetType().Name);
            return false;
        }
        return true;
    }
}
