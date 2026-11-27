using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace NileShop.Services;

public sealed class EmailOtp(
    IMemoryCache cache, NileShop.Data.ApplicationDbContext db, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection, IConfiguration config, IHostEnvironment environment, WhatsAppOtpClient whatsApp)
{
    private const string SessionKey = "EmailOtpChallenge";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // An explicit development override supports isolated tests without sending email.
    public bool Enabled => !environment.IsDevelopment() || config.GetValue("EmailOtp:Enabled", true);
    public bool IsWhatsAppConfigured => whatsApp.Configured;

    public sealed record Verified(string UserId, string Stamp, string Destination, bool Admin, bool Remote, string? Phone);
    // Channel-separated confirmation semantics: an email code confirms the email,
    // a WhatsApp code confirms the phone it was sent to, and only if that phone is
    // still the account's current number (a phone change after the challenge was
    // issued must not confirm the new number).
    // Phone-first gate: when WhatsApp delivery is configured and the account has a
    // phone number, the sign-in code goes to WhatsApp only (no silent email
    // fallback); without a phone the legacy email path applies.
    public static bool ShouldUseWhatsApp(bool whatsAppConfigured, string? phone) =>
        whatsAppConfigured && !string.IsNullOrWhiteSpace(phone);
    public static bool ShouldConfirmEmail(Verified v) => !v.Remote;
    // Purpose binding: while the WhatsApp gate is configured, no challenge of any
    // kind may sign in a phone-bearing account whose phone is not yet confirmed.
    public static bool ShouldBlockSignInWithoutPhone(bool whatsAppConfigured, string? phone, bool phoneConfirmed) =>
        whatsAppConfigured && !string.IsNullOrWhiteSpace(phone) && !phoneConfirmed;
    // Interrupted-signup gate: a WhatsApp (phone) verify with the email still
    // unconfirmed must resume the email gate, not sign the user in.
    public static bool ShouldDeferSignInForEmailGate(Verified v, bool emailConfirmed) =>
        v.Remote && !emailConfirmed;
    public static bool ShouldConfirmPhone(Verified v, string? currentPhone) =>
        v.Remote && v.Phone is not null && currentPhone is not null
        && WhatsAppOtpClient.NormalizePhone(currentPhone) == v.Phone;
    private sealed class Challenge(Verified result, byte[]? hash, string? phone)
    {
        public Verified Result { get; } = result;
        public byte[]? Hash { get; } = hash;
        public string? Phone { get; } = phone;
        public bool Remote => Phone is not null;
        public int Attempts { get; set; } = 5;
    }

    public void Clear(ISession session)
    {
        if (session.GetString(SessionKey) is { } previous) cache.Remove(previous);
        session.Remove(SessionKey);
    }

    public bool IsPending(ISession session) =>
        session.GetString(SessionKey) is { } key && cache.TryGetValue<Challenge>(key, out _);
    public bool IsPendingRemote(ISession session) =>
        session.GetString(SessionKey) is { } key && cache.TryGetValue<Challenge>(key, out var c) && c is { Remote: true };

    public bool IsChallengeActive(string key) =>
        cache.TryGetValue<Challenge>(key, out var challenge) && challenge is not null && challenge.Attempts > 0;

    public async Task<bool> SendAsync(HttpContext context, IdentityUser user, string destination, bool admin, bool forceEmail = false)
    {
        if (string.IsNullOrWhiteSpace(user.Email) || user.SecurityStamp is null) return false;
        Clear(context.Session);
        // Phone-first gate: WhatsApp delivery when configured and the account has a
        // phone number. A WhatsApp send failure fails the send (caller shows
        // Otp_Send_Failed) instead of silently bypassing the phone gate via email.
        // forceEmail is used by the profile email-verification step.
        if (!forceEmail && ShouldUseWhatsApp(whatsApp.Configured, user.PhoneNumber))
        {
            var phone = WhatsAppOtpClient.NormalizePhone(user.PhoneNumber!);
            if (!await whatsApp.SendAsync(phone, context.RequestAborted)) return false;
            var remoteKey = "email-otp:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            cache.Set(remoteKey, new Challenge(new Verified(user.Id, user.SecurityStamp, destination, admin, true, phone), null, phone), Lifetime);
            context.Session.SetString(SessionKey, remoteKey);
            return true;
        }
        // The per-email cooldown guards the email channel only; WhatsApp sends are
        // throttled by the endpoint rate limiter and the OTP service itself. This also
        // lets signup chain a WhatsApp verify straight into the email verification.
        if (!await AccountMailSender.CheckAndRecordCooldownAsync(db, user.Email, context.RequestAborted))
            return false;
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var key = "email-otp:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set(key, new Challenge(new Verified(user.Id, user.SecurityStamp, destination, admin, false, null),
            SHA256.HashData(Encoding.ASCII.GetBytes(code)), null), Lifetime);
        context.Session.SetString(SessionKey, key);
        var (otpSubject, otpBody) = CustomerMessages.Otp(code, (int)Lifetime.TotalMinutes);
        await AccountMailSender.QueueAsync(db, protection, user.Email, otpSubject, otpBody,
            key, DateTime.UtcNow.Add(Lifetime), context.RequestAborted);
        return true;
    }

    public async Task<Verified?> VerifyAsync(ISession session, string code, CancellationToken ct = default)
    {
        var key = session.GetString(SessionKey);
        if (key is null || !cache.TryGetValue<Challenge>(key, out var challenge) || challenge is null) return null;
        if (challenge.Remote)
        {
            bool? remote;
            lock (challenge)
            {
                if (challenge.Attempts <= 0) return null;
            }
            remote = await whatsApp.VerifyAsync(challenge.Phone!, code, ct);
            lock (challenge)
            {
                // Transport/service errors do not consume an attempt.
                if (remote is null) return null;
                challenge.Attempts--;
                if (remote == false)
                {
                    if (challenge.Attempts == 0) Clear(session);
                    return null;
                }
                challenge.Attempts = 0;
                Clear(session);
                return challenge.Result;
            }
        }
        lock (challenge)
        {
            if (challenge.Attempts <= 0) return null;
            challenge.Attempts--;
            if (!CryptographicOperations.FixedTimeEquals(challenge.Hash!,
                    SHA256.HashData(Encoding.ASCII.GetBytes(code))))
            {
                if (challenge.Attempts == 0) Clear(session);
                return null;
            }
            challenge.Attempts = 0;
            Clear(session);
            return challenge.Result;
        }
    }
}
