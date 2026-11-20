using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using NileShop.Services;

namespace NileShop.Controllers;

[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[Route("/Account/VerifyEmailCode")]
public sealed class EmailVerificationController(
    EmailOtp otp, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
    AuditService audit, IStringLocalizer<SharedResource> localizer, IOptions<SubdomainOptions> subOptions) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(otp.IsPending(HttpContext.Session));

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Index([FromForm] string? code)
    {
        var verified = code is { Length: 6 } && code.All(char.IsAsciiDigit)
            ? otp.Verify(HttpContext.Session, code) : null;
        if (verified is null)
        {
            ModelState.AddModelError("", localizer["Otp_Invalid"]);
            return View(otp.IsPending(HttpContext.Session));
        }

        var user = await users.FindByIdAsync(verified.UserId);
        if (user is null || user.SecurityStamp != verified.Stamp || await users.IsLockedOutAsync(user)
            || await users.IsInRoleAsync(user, "Admin") != verified.Admin)
            return View(false);

        if (!user.EmailConfirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            if (!(await users.ConfirmEmailAsync(user, token)).Succeeded) return View(false);
        }

        AuthSession.ClearCheckout(HttpContext);
        await signIn.SignInAsync(user, isPersistent: false);
        if (verified.Admin)
        {
            HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
            await audit.LogAsync("ADMIN_LOGIN", "Identity", $"Admin {user.Email} signed in");
            var options = subOptions.Value;
            if (options.Enabled && !string.IsNullOrWhiteSpace(options.AdminHost)
                && !SubdomainRouting.IsAdminHost(HttpContext, options))
                return Redirect(SubdomainRouting.BuildTargetUrl(HttpContext, options.AdminHost, "/StoreAdmin"));
        }
        return LocalRedirect(Url.IsLocalUrl(verified.Destination) ? verified.Destination : "/");
    }
}
