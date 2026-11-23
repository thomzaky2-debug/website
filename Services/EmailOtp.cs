using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace NileShop.Services;

public sealed class EmailOtp(
    IMemoryCache cache, AccountMailSender mail, IConfiguration config, IHostEnvironment environment)
{
    private const string SessionKey = "EmailOtpChallenge";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    // An explicit development override supports isolated tests without sending email.
    public bool Enabled => !environment.IsDevelopment() || config.GetValue("EmailOtp:Enabled", true);

    public sealed record Verified(string UserId, string Stamp, string Destination, bool Admin);
    private sealed class Challenge(Verified result, byte[] hash)
    {
        public Verified Result { get; } = result;
        public byte[] Hash { get; } = hash;
        public int Attempts { get; set; } = 5;
    }

    public void Clear(ISession session)
    {
        if (session.GetString(SessionKey) is { } previous) cache.Remove(previous);
        session.Remove(SessionKey);
    }

    public bool IsPending(ISession session) =>
        session.GetString(SessionKey) is { } key && cache.TryGetValue<Challenge>(key, out _);

    public async Task<bool> SendAsync(HttpContext context, IdentityUser user, string destination, bool admin)
    {
        Clear(context.Session);
        if (string.IsNullOrWhiteSpace(user.Email) || user.SecurityStamp is null) return false;
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var key = "email-otp:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var sent = await mail.SendAsync(user.Email, "Chic & Unique | رمز التحقق",
            $"رمز التحقق: {code}\nتنتهي صلاحية الرمز بعد 10 دقائق. تجاهل هذه الرسالة إذا لم تطلب الرمز.",
            context.RequestAborted);
        if (!sent) return false;
        cache.Set(key, new Challenge(new Verified(user.Id, user.SecurityStamp, destination, admin),
            SHA256.HashData(Encoding.ASCII.GetBytes(code))), Lifetime);
        context.Session.SetString(SessionKey, key);
        return true;
    }

    public Verified? Verify(ISession session, string code)
    {
        var key = session.GetString(SessionKey);
        if (key is null || !cache.TryGetValue<Challenge>(key, out var challenge) || challenge is null) return null;
        lock (challenge)
        {
            if (challenge.Attempts <= 0) return null;
            challenge.Attempts--;
            if (!CryptographicOperations.FixedTimeEquals(challenge.Hash,
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
