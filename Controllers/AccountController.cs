using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using NileShop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using NileShop.Services;
using NileShop.ViewModels;
namespace NileShop.Controllers;
[ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
public class AccountController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn, IStringLocalizer<SharedResource> localizer, ApplicationDbContext db, WalletService wallet, AuditService audit, IOptions<SubdomainOptions> subOptions, EmailOtp otp) : Controller
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
        var savedCount = await db.SavedProducts.AsNoTracking().CountAsync(s => s.UserId == user.Id && s.Product.IsActive && !s.Product.Category.IsHidden);
        var reviewsCount = await db.ProductReviews.AsNoTracking().CountAsync(r => r.UserId == user.Id);
        var walletBalance = await wallet.GetBalanceAsync(user.Id);
        var recentOrders = await ordersQuery.OrderByDescending(o => o.Id).Take(3).ToListAsync();
        await OrderStatusPresentation.LoadAsync(db, recentOrders);
        ViewBag.AccountOrdersCount = ordersCount;
        ViewBag.AccountSavedCount = savedCount;
        ViewBag.AccountWalletBalance = walletBalance;
        return new CustomerProfileViewModel
        {
            Email = user.Email,
            EmailConfirmed = user.EmailConfirmed,
            HasPassword = await users.HasPasswordAsync(user),
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
        if(await users.IsInRoleAsync(user,"Admin"))return RedirectToAction("Dashboard","Admin");
        return View(await BuildProfileAsync(user));
    }
    [Authorize, HttpPost("/Account/SendEmailVerification"), ValidateAntiForgeryToken, EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> SendEmailVerification()
    {
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();
        if(user.EmailConfirmed)return RedirectToAction(nameof(Index));
        if(await otp.SendAsync(HttpContext, user, Url.Action("Index","Account")!, admin: false, forceEmail: true))
            return RedirectToAction("Index", "EmailVerification");
        TempData["Error"] = "Otp_Send_Failed";
        return RedirectToAction(nameof(Index));
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
        ViewBag.AccountSavedCount = await db.SavedProducts.AsNoTracking().CountAsync(s => s.UserId == user.Id && s.Product.IsActive && !s.Product.Category.IsHidden);
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
        ViewBag.AccountSavedCount=await db.SavedProducts.AsNoTracking().CountAsync(s=>s.UserId==id&&s.Product.IsActive && !s.Product.Category.IsHidden);
        ViewBag.AccountWalletBalance=await wallet.GetBalanceAsync(id);
        ViewData["Page"]=page;ViewData["Pages"]=(int)Math.Ceiling(total/20d);
        var orders=await query.OrderByDescending(o=>o.Id).Skip((page-1)*20).Take(20).ToListAsync();
        await OrderStatusPresentation.LoadAsync(db,orders);
        return View(orders);
    }
    [Authorize,HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(CustomerProfileViewModel model, bool stay = false)
    {
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();model.Email=user.Email;
        if(!ModelState.IsValid)return View("Index",await BuildProfileAsync(user,model));
        await using var tx=await db.Database.BeginTransactionAsync();
        // SetPhoneNumberAsync also clears PhoneNumberConfirmed when the number changes,
        // so a profile edit can never leave a stale confirmed flag on a new number.
        var result=await users.SetPhoneNumberAsync(user,model.Phone.Trim());
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
        if(result.Succeeded){
            // Phone change resets the confirmed flag; release the ownership row in
            // the same transaction so the number becomes claimable again atomically.
            await PhoneOwnership.ReleaseForUserAsync(db,user.Id);
        }
        if(!result.Succeeded){await tx.RollbackAsync();ModelState.AddModelError("",localizer["Error"]);return View("Index",await BuildProfileAsync(user,model));}
        await tx.CommitAsync();await signIn.RefreshSignInAsync(user);TempData["Success"]="Profile_Saved";
        return (stay || Request.Headers["X-NileShop-Async"] == "true") ? RedirectToAction(nameof(Index)) : RedirectToAction("Index","Home");
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
    [Authorize,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> AddPassword(CustomerAddPasswordViewModel model)
    {
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();
        if(await users.HasPasswordAsync(user))return RedirectToAction(nameof(Index));
        if(!ModelState.IsValid){
            ViewData["PasswordError"]=localizer[model.NewPassword!=model.ConfirmNewPassword?"Password_Mismatch":"Password_Change_Failed"].Value;
            return View("Index",await BuildProfileAsync(user));
        }
        var result=await users.AddPasswordAsync(user,model.NewPassword);
        if(!result.Succeeded){
            ViewData["PasswordError"]=localizer["Password_Change_Failed"].Value;
            return View("Index",await BuildProfileAsync(user));
        }
        await signIn.RefreshSignInAsync(user);
        TempData["Success"]="Password_Updated";
        return RedirectToAction(nameof(Index));
    }
    [AllowAnonymous,HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        var opts = subOptions.Value;
        var isAdminHost = SubdomainRouting.IsAdminHost(HttpContext, opts);
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin")) return LocalRedirect("/Admin/Dashboard");
            if (isAdminHost && !string.IsNullOrWhiteSpace(opts.ClientHost))
            {
                return Redirect(SubdomainRouting.BuildTargetUrl(HttpContext, opts.ClientHost, "/Account"));
            }
            return LocalRedirect(SafeReturn(returnUrl));
        }
        ViewBag.PhoneLoginAvailable = otp.IsWhatsAppConfigured;
        return View(new CustomerLoginViewModel {ReturnUrl=returnUrl});
    }
    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Login(CustomerLoginViewModel model)
    {
        if(!ModelState.IsValid)return View(model);
        var user=await users.FindByEmailAsync(model.Email.Trim());
        if(user is not null) {
            var isAdmin = await users.IsInRoleAsync(user,"Admin");
            var opts = subOptions.Value;
            var isAdminHost = SubdomainRouting.IsAdminHost(HttpContext, opts);
            var result = otp.Enabled
                ? await signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true)
                : await signIn.PasswordSignInAsync(user, model.Password, isPersistent: true, lockoutOnFailure: true);
            if(result.Succeeded) {
                if (otp.Enabled)
                {
                    if (isAdminHost && !isAdmin)
                    {
                        ModelState.AddModelError("", localizer["Admin_Portal_Restricted"]);
                        return View(model);
                    }
                    var destination = isAdmin && !Url.IsLocalUrl(model.ReturnUrl)
                        ? "/Admin/Dashboard" : SafeReturn(model.ReturnUrl);
                    if (await otp.SendAsync(HttpContext, user, destination, isAdmin))
                        return RedirectToAction("Index", "EmailVerification");
                    ModelState.AddModelError("", localizer["Otp_Send_Failed"]);
                    return View(model);
                }
                AuthSession.ClearCheckout(HttpContext);
                if(isAdmin) {
                    HttpContext.User = await signIn.CreateUserPrincipalAsync(user);
                    await audit.LogAsync("ADMIN_LOGIN","Identity",$"Admin {user.Email} signed in");
                    if (opts.Enabled && !string.IsNullOrWhiteSpace(opts.AdminHost) && !isAdminHost)
                    {
                        return Redirect(SubdomainRouting.BuildTargetUrl(HttpContext, opts.AdminHost, "/StoreAdmin"));
                    }
                    return LocalRedirect(!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/Admin/Dashboard");
                }
                if (isAdminHost) {
                    await signIn.SignOutAsync();
                    ModelState.AddModelError("", localizer["Admin_Portal_Restricted"].Value);
                    return View(model);
                }
                return LocalRedirect(SafeReturn(model.ReturnUrl));
            }
        }
        ModelState.AddModelError("",localizer["InvalidLogin"]);return View(model);
    }
    [AllowAnonymous,HttpGet("/Account/LoginPhone")]
    public IActionResult LoginPhone(string? returnUrl)
    {
        if(!otp.IsWhatsAppConfigured)return RedirectToAction(nameof(Login),new{returnUrl});
        if(User.Identity?.IsAuthenticated==true)return LocalRedirect(SafeReturn(returnUrl));
        return View(new CustomerPhoneLoginViewModel {ReturnUrl=returnUrl});
    }
    [AllowAnonymous,HttpPost("/Account/LoginPhone"),ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> LoginPhone(CustomerPhoneLoginViewModel model)
    {
        if(!otp.IsWhatsAppConfigured)return RedirectToAction(nameof(Login));
        if(!ModelState.IsValid)return View(model);
        var normalized=WhatsAppOtpClient.NormalizePhone(model.Phone.Trim());
        // Only accounts whose phone was verified can sign in this way; an unverified
        // phone number on another account is never linked automatically.
        IdentityUser? user=null;
        if(await PhoneOwnership.GetOwnerAsync(db,normalized) is { } owner)
            user=await users.FindByIdAsync(owner.UserId);
        if(user is null){
            var candidates=await users.Users.Where(u=>u.PhoneNumberConfirmed && u.PhoneNumber!=null).ToListAsync();
            user=candidates.FirstOrDefault(u=>WhatsAppOtpClient.NormalizePhone(u.PhoneNumber!)==normalized);
            if(user is not null)await PhoneOwnership.TryClaimAsync(db,user.Id,normalized);
        }
        if(user is null || await users.IsInRoleAsync(user,"Admin")){
            ModelState.AddModelError("",localizer["InvalidLogin"]);return View(model);
        }
        if(await otp.SendAsync(HttpContext, user, SafeReturn(model.ReturnUrl), admin: false))
            return RedirectToAction("Index", "EmailVerification");
        ModelState.AddModelError("",localizer["Otp_Send_Failed"]);
        return View(model);
    }
    [AllowAnonymous,HttpGet]
    public IActionResult Register(string? returnUrl)
    {
        if(User.Identity?.IsAuthenticated==true)return LocalRedirect(SafeReturn(returnUrl));
        ViewBag.PasswordOptional=otp.IsWhatsAppConfigured;
        return View(new CustomerRegisterViewModel {ReturnUrl=returnUrl});
    }
    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken,EnableRateLimiting("customer-auth")]
    public async Task<IActionResult> Register(CustomerRegisterViewModel model)
    {
        if(!ModelState.IsValid)return View(model);
        var passwordOptional=otp.IsWhatsAppConfigured;
        ViewBag.PasswordOptional=passwordOptional;
        if(!passwordOptional && string.IsNullOrWhiteSpace(model.Password)){
            ModelState.AddModelError(nameof(model.Password),localizer["Required"]);return View(model);
        }
        // A phone number already verified on another account cannot be claimed again;
        // unverified numbers are re-verified for the new account without touching the old one.
        var newPhone=WhatsAppOtpClient.NormalizePhone(model.Phone.Trim());
        if(await PhoneOwnership.GetOwnerAsync(db,newPhone) is not null){
            ModelState.AddModelError(nameof(model.Phone),localizer["Phone_In_Use"]);return View(model);
        }
        var confirmedPhones=await users.Users.Where(u=>u.PhoneNumberConfirmed && u.PhoneNumber!=null).ToListAsync();
        if(confirmedPhones.Any(u=>WhatsAppOtpClient.NormalizePhone(u.PhoneNumber!)==newPhone)){
            ModelState.AddModelError(nameof(model.Phone),localizer["Phone_In_Use"]);return View(model);
        }
        // Only basic customer fields are accepted. Registration never assigns administrative roles.
        await using var tx=await db.Database.BeginTransactionAsync();
        var email=model.Email.Trim();var user=new IdentityUser {UserName=email,Email=email,PhoneNumber=model.Phone.Trim()};
        var result=string.IsNullOrWhiteSpace(model.Password)
            ? await users.CreateAsync(user)
            : await users.CreateAsync(user,model.Password);
        if(!result.Succeeded){ModelState.AddModelError("",localizer["Registration_Failed"]);return View(model);}
        result=await users.AddClaimAsync(user,new Claim(CustomerProfileViewModel.FullNameClaim,model.FullName.Trim()));
        if(!result.Succeeded){await tx.RollbackAsync();ModelState.AddModelError("",localizer["Registration_Failed"]);return View(model);}
        result=await users.AddClaimAsync(user,new Claim(LanguagePreferences.ClaimType(false), model.PreferredLanguage));
        if(!result.Succeeded){await tx.RollbackAsync();ModelState.AddModelError("",localizer["Registration_Failed"]);return View(model);}
        await tx.CommitAsync();
        AuthSession.ClearCheckout(HttpContext);
        LanguagePreferences.WriteCookie(Response,false,model.PreferredLanguage);
        if (otp.Enabled)
        {
            if (await otp.SendAsync(HttpContext, user, SafeReturn(model.ReturnUrl), admin: false))
                return RedirectToAction("Index", "EmailVerification");
            TempData["Error"] = "Otp_Signup_Send_Failed";
            return RedirectToAction(nameof(Login), new { returnUrl = SafeReturn(model.ReturnUrl) });
        }
        await signIn.SignInAsync(user, isPersistent: true);
        return LocalRedirect(SafeReturn(model.ReturnUrl));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(){await signIn.SignOutAsync();otp.Clear(HttpContext.Session);AuthSession.ClearCheckout(HttpContext);return RedirectToAction("Index","Home");}
    string SafeReturn(string? value)=>Url.IsLocalUrl(value)?value!:Url.Action("Index","Home")!;
}
