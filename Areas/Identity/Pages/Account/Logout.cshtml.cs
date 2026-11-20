using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace NileShop.Areas.Identity.Pages.Account;
public class LogoutModel(SignInManager<IdentityUser> signIn, NileShop.Services.EmailOtp otp) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Account/Login");
    public async Task<IActionResult> OnPostAsync() { await signIn.SignOutAsync(); otp.Clear(HttpContext.Session); NileShop.Services.AuthSession.ClearCheckout(HttpContext); return LocalRedirect("/"); }
}
