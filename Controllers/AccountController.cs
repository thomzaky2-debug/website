using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using NileShop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using NileShop.Services;
using NileShop.ViewModels;
namespace NileShop.Controllers;
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, IStringLocalizer<SharedResource> localizer, ApplicationDbContext db, WalletService wallet) : Controller
{
    async Task<CustomerProfileViewModel> BuildProfileAsync(IdentityUser user, CustomerProfileViewModel? form = null)
    {
        var claims = await users.GetClaimsAsync(user);
        var fullName = form?.FullName ?? claims.FirstOrDefault(c => c.Type == CustomerProfileViewModel.FullNameClaim)?.Value ?? "";
        var phone = form?.Phone ?? user.PhoneNumber ?? "";
        var whatsApp = form is not null
            ? form.WhatsAppPhone
            : claims.FirstOrDefault(c => c.Type == CustomerProfileViewModel.WhatsAppClaim)?.Value;
        var address = form is not null
            ? (form.Address ?? "")
            : (claims.FirstOrDefault(c => c.Type == CustomerProfileViewModel.AddressClaim)?.Value ?? "");
        var ordersQuery = db.Orders.AsNoTracking().Where(o => o.CustomerUserId == user.Id);
        var ordersCount = await ordersQuery.CountAsync();
        var savedCount = await db.SavedProducts.AsNoTracking().CountAsync(s => s.UserId == user.Id && s.Product.IsActive);
        var reviewsCount = await db.ProductReviews.AsNoTracking().CountAsync(r => r.UserId == user.Id);
        var walletBalance = await wallet.GetBalanceAsync(user.Id);
        var recentOrders = await ordersQuery.OrderByDescending(o => o.Id).Take(3).ToListAsync();
        ViewBag.AccountOrdersCount = ordersCount;
        ViewBag.AccountSavedCount = savedCount;
        ViewBag.AccountWalletBalance = walletBalance;
        return new CustomerProfileViewModel
        {
            Email = user.Email,
            Phone = phone,
            WhatsAppPhone = whatsApp,
            FullName = fullName,
            Address = address,
            OrdersCount = ordersCount,
            SavedCount = savedCount,
            ReviewsCount = reviewsCount,
            WalletBalance = walletBalance,
            RecentOrders = recentOrders
        };
    }

    public async Task<IActionResult> Index()
    {
        var user=await users.GetUserAsync(User);if(user is null)return RedirectToAction(nameof(Login));
        return View(await BuildProfileAsync(user));
    }
    [Authorize, HttpGet("/Account/Wallet")]
    public async Task<IActionResult> Wallet()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));
        var claims = await users.GetClaimsAsync(user);
        var fullName = claims.FirstOrDefault(c => c.Type == CustomerProfileViewModel.FullNameClaim)?.Value ?? "";
        var balance = await wallet.GetBalanceAsync(user.Id);
        var txList = await wallet.GetTransactionsAsync(user.Id, 100);
        ViewBag.AccountOrdersCount = await db.Orders.AsNoTracking().CountAsync(o => o.CustomerUserId == user.Id);
        ViewBag.AccountSavedCount = await db.SavedProducts.AsNoTracking().CountAsync(s => s.UserId == user.Id && s.Product.IsActive);
        ViewBag.AccountWalletBalance = balance;
        return View(new CustomerWalletViewModel
        {
            FullName = fullName,
            Email = user.Email,
            Balance = balance,
            Transactions = txList
        });
    }
    [Authorize,HttpGet]
    public async Task<IActionResult> Orders(int page=1)
    {
        var id=users.GetUserId(User);page=Math.Clamp(page,1,100000);
        var query=db.Orders.AsNoTracking().Where(o=>o.CustomerUserId==id);
        var total=await query.CountAsync();
        ViewBag.AccountOrdersCount=total;
        ViewBag.AccountSavedCount=await db.SavedProducts.AsNoTracking().CountAsync(s=>s.UserId==id&&s.Product.IsActive);
        ViewBag.AccountWalletBalance=await wallet.GetBalanceAsync(id);
        ViewData["Page"]=page;ViewData["Pages"]=(int)Math.Ceiling(total/20d);
        return View(await query.OrderByDescending(o=>o.Id).Skip((page-1)*20).Take(20).ToListAsync());
    }
    [Authorize,HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(CustomerProfileViewModel model, bool stay = false)
    {
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();model.Email=user.Email;
        if(!ModelState.IsValid)return View("Index",await BuildProfileAsync(user,model));
        await using var tx=await db.Database.BeginTransactionAsync();
        user.PhoneNumber=model.Phone.Trim();
        var result=await users.UpdateAsync(user);
        if(result.Succeeded){
            var claims=await users.GetClaimsAsync(user);
            var old=claims.Where(c=>c.Type==CustomerProfileViewModel.FullNameClaim).ToArray();
            if(old.Length>0)result=await users.RemoveClaimsAsync(user,old);
            if(result.Succeeded)result=await users.AddClaimAsync(user,new Claim(CustomerProfileViewModel.FullNameClaim,model.FullName.Trim()));
            if(result.Succeeded && Request.Form.ContainsKey(nameof(model.WhatsAppPhone))){
                var oldWa=claims.Where(c=>c.Type==CustomerProfileViewModel.WhatsAppClaim).ToArray();
                if(oldWa.Length>0)result=await users.RemoveClaimsAsync(user,oldWa);
                if(result.Succeeded && !string.IsNullOrWhiteSpace(model.WhatsAppPhone))
                    result=await users.AddClaimAsync(user,new Claim(CustomerProfileViewModel.WhatsAppClaim,model.WhatsAppPhone.Trim()));
            }
            if(result.Succeeded && Request.Form.ContainsKey(nameof(model.Address))){
                var oldAddr=claims.Where(c=>c.Type==CustomerProfileViewModel.AddressClaim).ToArray();
                if(oldAddr.Length>0)result=await users.RemoveClaimsAsync(user,oldAddr);
                if(result.Succeeded && !string.IsNullOrWhiteSpace(model.Address))
                    result=await users.AddClaimAsync(user,new Claim(CustomerProfileViewModel.AddressClaim,model.Address.Trim()));
            }
        }
        if(!result.Succeeded){await tx.RollbackAsync();ModelState.AddModelError("",localizer["Error"]);return View("Index",await BuildProfileAsync(user,model));}
        await tx.CommitAsync();await signIn.RefreshSignInAsync(user);TempData["Success"]="Profile_Saved";
        return stay ? RedirectToAction(nameof(Index)) : RedirectToAction("Index","Home");
    }
    [Authorize,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> ChangePassword(CustomerPasswordViewModel model)
    {
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();
        if(!ModelState.IsValid){
            ViewData["PasswordError"]=localizer[model.NewPassword!=model.ConfirmNewPassword?"Password_Mismatch":"Password_Change_Failed"].Value;
            return View("Index",await BuildProfileAsync(user));
        }
        var result=await users.ChangePasswordAsync(user,model.CurrentPassword,model.NewPassword);
        if(!result.Succeeded){
            ViewData["PasswordError"]=localizer["Password_Change_Failed"].Value;
            return View("Index",await BuildProfileAsync(user));
        }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"]="Password_Updated";
        return RedirectToAction(nameof(Index));
    }
    [AllowAnonymous,HttpGet]
    public IActionResult Login(string? returnUrl)=>User.Identity?.IsAuthenticated==true ? LocalRedirect(SafeReturn(returnUrl)) : View(new CustomerLoginViewModel {ReturnUrl=returnUrl});
    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Login(CustomerLoginViewModel model)
    {
        if(!ModelState.IsValid)return View(model);
        var user=await users.FindByEmailAsync(model.Email.Trim());
        if(user is not null && !await users.IsInRoleAsync(user,"Admin")) {
            var result=await signIn.PasswordSignInAsync(user,model.Password,false,lockoutOnFailure:true);
            if(result.Succeeded)return LocalRedirect(SafeReturn(model.ReturnUrl));
        }
        ModelState.AddModelError("",localizer["InvalidLogin"]);return View(model);
    }
    [AllowAnonymous,HttpGet]
    public IActionResult Register(string? returnUrl)=>User.Identity?.IsAuthenticated==true ? LocalRedirect(SafeReturn(returnUrl)) : View(new CustomerRegisterViewModel {ReturnUrl=returnUrl});
    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Register(CustomerRegisterViewModel model)
    {
        if(!ModelState.IsValid)return View(model);
        // Only basic customer fields are accepted. Registration never assigns administrative roles.
        await using var tx=await db.Database.BeginTransactionAsync();
        var email=model.Email.Trim();var user=new IdentityUser {UserName=email,Email=email,PhoneNumber=model.Phone.Trim()};
        var result=await users.CreateAsync(user,model.Password);
        if(!result.Succeeded){ModelState.AddModelError("",localizer["Registration_Failed"]);return View(model);}
        result=await users.AddClaimAsync(user,new Claim(CustomerProfileViewModel.FullNameClaim,model.FullName.Trim()));
        if(!result.Succeeded){await tx.RollbackAsync();ModelState.AddModelError("",localizer["Registration_Failed"]);return View(model);}
        await tx.CommitAsync();
        await signIn.SignInAsync(user,isPersistent:false);
        return LocalRedirect(SafeReturn(model.ReturnUrl));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(){await signIn.SignOutAsync();return RedirectToAction("Index","Home");}
    string SafeReturn(string? value)=>Url.IsLocalUrl(value)?value!:Url.Action("Index","Home")!;
}
