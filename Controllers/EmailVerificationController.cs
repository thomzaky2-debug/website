using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
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
    AuditService audit, IStringLocalizer<SharedResource> localizer, IOptions<SubdomainOptions> subOptions,
    NileShop.Data.ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Index() => View((otp.IsPending(HttpContext.Session), otp.IsPendingRemote(HttpContext.Session)));

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Index([FromForm] string? code)
    {
        var verified = code is { Length: 6 } && code.All(char.IsAsciiDigit)
            ? await otp.VerifyAsync(HttpContext.Session, code, HttpContext.RequestAborted) : null;
        if (verified is null)
        {
            ModelState.AddModelError("", localizer["Otp_Invalid"]);
            return View((otp.IsPending(HttpContext.Session), otp.IsPendingRemote(HttpContext.Session)));
        }

        var user = await users.FindByIdAsync(verified.UserId);
        if (user is null || user.SecurityStamp != verified.Stamp || await users.IsLockedOutAsync(user)
            || await users.IsInRoleAsync(user, "Admin") != verified.Admin)
            return View((false, false));

        // Channel-specific confirmation: an email-delivered code confirms the email;
        // a WhatsApp-delivered code confirms the registered phone number instead.
        if (verified.Remote)
        {
            if (!user.PhoneNumberConfirmed && EmailOtp.ShouldConfirmPhone(verified, user.PhoneNumber))
            {
                // Atomic claim: the ConfirmedPhoneOwners primary key enforces that
                // only one account can own this normalized phone, even under
                // concurrent confirmations across instances.
                if (!await PhoneOwnership.TryClaimAsync(db, user.Id, verified.Phone!, HttpContext.RequestAborted))
                    return View((false, false));
                user.PhoneNumberConfirmed = true;
                if (!(await users.UpdateAsync(user)).Succeeded)
                {
                    await PhoneOwnership.ReleaseForUserAsync(db, user.Id, HttpContext.RequestAborted);
                    return View((false, false));
                }
            }
        }
        else if (!user.EmailConfirmed)
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            if (!(await users.ConfirmEmailAsync(user, token)).Succeeded) return View((false, false));
        }

        // Purpose binding: an email-channel challenge (profile verification, legacy
        // login, or the signup chain) must never sign in a phone-bearing account
        // whose phone is unconfirmed while the WhatsApp gate is configured. The
        // signup chain reaches this point only after its phone verify, so this
        // cannot deadlock legitimate signups; admins have no phone and skip it.
        if (EmailOtp.ShouldBlockSignInWithoutPhone(otp.IsWhatsAppConfigured, user.PhoneNumber, user.PhoneNumberConfirmed))
            return View((false, false));
        AuthSession.ClearCheckout(HttpContext);
        // Phone-first signup verifies both identifiers: after a WhatsApp (phone) verify
        // with the email still unconfirmed (an interrupted signup, reached again via
        // phone or password login), the flow resumes the email gate instead of signing
        // in; sign-in happens only once the email challenge confirms the email.
        if (EmailOtp.ShouldDeferSignInForEmailGate(verified, user.EmailConfirmed))
        {
            if (await otp.SendAsync(HttpContext, user, verified.Destination, admin: false, forceEmail: true))
                return View((true, false));
            ModelState.AddModelError("", localizer["Otp_Send_Failed"]);
            return View((false, false));
        }
        await signIn.SignInAsync(user, isPersistent: true);
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
