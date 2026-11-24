using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace NileShop.Services;

public static class AuthCookieEvents
{
    public const string AdminAbsoluteExpiryKey = "AdminAbsoluteExpiresUtc";
    public static readonly TimeSpan AdminAbsoluteLifetime = TimeSpan.FromHours(8);

    public static Task OnSigningInAsync(CookieSigningInContext context, TimeProvider? clock = null)
    {
        if (context.Principal?.IsInRole("Admin") == true)
        {
            var now = (clock ?? context.HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            if (!context.Properties.Items.TryGetValue(AdminAbsoluteExpiryKey, out var rawAbs) ||
                !DateTimeOffset.TryParse(rawAbs, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var absExpiry))
            {
                absExpiry = now.Add(AdminAbsoluteLifetime);
                context.Properties.Items[AdminAbsoluteExpiryKey] = absExpiry.ToString("O", CultureInfo.InvariantCulture);
            }
            context.Properties.AllowRefresh = false;
            context.Properties.IsPersistent = false;
            context.Properties.ExpiresUtc = absExpiry;
        }
        return Task.CompletedTask;
    }

    public static Task OnCheckSlidingExpirationAsync(CookieSlidingExpirationContext context)
    {
        if (context.Principal?.IsInRole("Admin") == true)
            context.ShouldRenew = false;
        return Task.CompletedTask;
    }

    public static async Task OnValidatePrincipalAsync(CookieValidatePrincipalContext context, TimeProvider? clock = null)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null) return;

        if (context.Principal.IsInRole("Admin"))
        {
            var now = (clock ?? context.HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            if (!context.Properties.Items.TryGetValue(AdminAbsoluteExpiryKey, out var rawAbs) ||
                !DateTimeOffset.TryParse(rawAbs, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var absExpiry) ||
                now > absExpiry ||
                context.Properties.ExpiresUtc is null ||
                (context.Properties.IssuedUtc.HasValue && context.Properties.ExpiresUtc.Value - context.Properties.IssuedUtc.Value > TimeSpan.FromHours(8.5)))
            {
                context.RejectPrincipal();
                return;
            }

            // Clamp expiry even when SecurityStampValidator sets ShouldRenew = true so re-issue never extends past T+8h.
            context.Properties.AllowRefresh = false;
            context.Properties.IsPersistent = false;
            context.Properties.ExpiresUtc = absExpiry;
        }
    }
}
