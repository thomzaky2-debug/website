using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using NileShop.Services;
using NileShop.ViewModels;

namespace NileShop.Controllers;

[AllowAnonymous]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("/Account/Login")]
public sealed class PasswordRecoveryController(
    UserManager<IdentityUser> users, NileShop.Data.ApplicationDbContext db, EmailOtp otp,
    IStringLocalizer<SharedResource> localizer, IOptions<NilePayOptions> gatewayOptions) : Controller
{
    [HttpGet("ForgotPassword")]
    public IActionResult Forgot() => View(new ForgotPasswordViewModel());

    [HttpPost("ForgotPassword"), ValidateAntiForgeryToken, EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Forgot(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var allowed = await AccountMailSender.CheckAndRecordCooldownAsync(db, model.Email, HttpContext.RequestAborted);
        if (allowed)
        {
            var user = await users.FindByEmailAsync(model.Email.Trim());
            if (!string.IsNullOrEmpty(user?.Email))
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var path = Url.Action(nameof(Reset), new { userId = user.Id, token = encoded });
                var origin = PublicOrigin();
                if (origin is not null && path is not null)
                {
                    var link = new Uri(origin, path).AbsoluteUri;
                    await AccountMailSender.QueueAsync(db, user.Email, "Chic & Unique | إعادة تعيين كلمة المرور",
                        $"لإعادة تعيين كلمة المرور، افتح الرابط التالي. تجاهل هذه الرسالة إذا لم تطلب إعادة التعيين.\n\n{link}",
                        ct: HttpContext.RequestAborted);
                }
            }
        }
        return RedirectToAction(nameof(Requested));
    }

    [HttpGet("PasswordResetRequested")]
    public IActionResult Requested() => View();

    [HttpGet("ResetPassword")]
    public IActionResult Reset(string? userId, string? token) => View(new ResetPasswordViewModel
    {
        UserId = userId is { Length: <= 450 } ? userId : "",
        Token = token is { Length: <= 2048 } ? token : ""
    });

    [HttpPost("ResetPassword"), ValidateAntiForgeryToken, EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Reset(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        string decoded;
        try { decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token)); }
        catch (FormatException) { return InvalidReset(model); }

        var user = await users.FindByIdAsync(model.UserId);
        if (user is null) return InvalidReset(model);
        var result = await users.ResetPasswordAsync(user, decoded, model.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                return InvalidReset(model);
            ModelState.AddModelError(nameof(model.Password), localizer["Customer_Password_Help"]);
            return View(model);
        }

        otp.Clear(HttpContext.Session);
        TempData["Success"] = "Recovery_Complete";
        return RedirectToAction("Login", "Account");
    }

    private IActionResult InvalidReset(ResetPasswordViewModel model)
    {
        ModelState.AddModelError("", localizer["Recovery_Invalid"]);
        return View(nameof(Reset), model);
    }

    private Uri? PublicOrigin()
    {
        if (Uri.TryCreate(gatewayOptions.Value.PublicUrl, UriKind.Absolute, out var configured)
            && configured.Scheme == Uri.UriSchemeHttps && configured.AbsolutePath == "/"
            && string.IsNullOrEmpty(configured.UserInfo) && string.IsNullOrEmpty(configured.Query)
            && string.IsNullOrEmpty(configured.Fragment))
            return configured;

        // Host filtering in Program.cs limits production requests to configured site hosts.
        return Request.IsHttps ? new Uri($"https://{Request.Host.ToUriComponent()}/") : null;
    }
}
