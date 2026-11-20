using System.ComponentModel.DataAnnotations;
using NileShop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
namespace NileShop.Areas.Identity.Pages.Account;
public class LoginModel(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, AuditService audit, EmailOtp otp, IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty, Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password), StringLength(128)] public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Email.Trim());
        if(user is not null && await users.IsInRoleAsync(user,"Admin")) {
            var result = otp.Enabled
                ? await signIn.CheckPasswordSignInAsync(user, Password, lockoutOnFailure: true)
                : await signIn.PasswordSignInAsync(user, Password, false, lockoutOnFailure: true);
            if (otp.Enabled && result.Succeeded)
            {
                var destination = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Admin/Dashboard";
                if (await otp.SendAsync(HttpContext, user, destination, admin: true))
                    return RedirectToAction("Index", "EmailVerification", new { area = "" });
                ModelState.AddModelError("", localizer["Otp_Send_Failed"]);
                return Page();
            }
            if(result.Succeeded){AuthSession.ClearCheckout(HttpContext);HttpContext.User = await signIn.CreateUserPrincipalAsync(user);await audit.LogAsync("ADMIN_LOGIN","Identity",$"Admin {user.Email} signed in");return LocalRedirect(!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)? ReturnUrl! : "/Admin/Dashboard");}
        }
        ModelState.AddModelError("",localizer["InvalidLogin"]);return Page();
    }
}
