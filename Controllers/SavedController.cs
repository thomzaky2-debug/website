using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
namespace NileShop.Controllers;
[Authorize,ResponseCache(Duration=0,Location=ResponseCacheLocation.None,NoStore=true)]
public class SavedController(ApplicationDbContext db,UserManager<IdentityUser> users,CatalogService catalog,WalletService wallet) : Controller
{
    [HttpGet("/saved")]
    public async Task<IActionResult> Index()
    {
        var id=users.GetUserId(User)!;
        var products=await db.Products.AsNoTracking().Where(p=>p.IsActive && !p.Category.IsHidden&&db.SavedProducts.Any(s=>s.UserId==id&&s.ProductId==p.Id)).Include(p=>p.Translations).Include(p=>p.Category).ThenInclude(c=>c.Translations).ToListAsync();
        ViewBag.AccountSavedCount=products.Count;
        ViewBag.AccountOrdersCount=await db.Orders.AsNoTracking().CountAsync(o=>o.CustomerUserId==id);
        ViewBag.AccountWalletBalance=await wallet.GetBalanceAsync(id);
        ViewBag.Ratings=await catalog.GetProductRatingsAsync(db,products.Select(p=>p.Id));
        ViewBag.BestsellerIds=await catalog.GetBestsellerIdsAsync(db);
        return View(products.Select(CatalogService.Card).ToList());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int productId,string? returnUrl)
    {
        var id=users.GetUserId(User)!;
        if(!await db.Products.AnyAsync(p=>p.Id==productId&&p.IsActive && !p.Category.IsHidden))return NotFound();
        var isAjax = Request.Headers.Accept.ToString().Contains("application/json") || Request.Headers["X-Requested-With"] == "XMLHttpRequest";
        var existing = await db.SavedProducts.FirstOrDefaultAsync(s=>s.UserId==id&&s.ProductId==productId);
        bool isSaved;

        if (isAjax && existing != null)
        {
            db.SavedProducts.Remove(existing);
            await db.SaveChangesAsync();
            isSaved = false;
        }
        else
        {
            if (existing == null)
            {
                db.SavedProducts.Add(new SavedProduct{UserId=id,ProductId=productId});
                try{await db.SaveChangesAsync();}catch(DbUpdateException){if(!await db.SavedProducts.AsNoTracking().AnyAsync(s=>s.UserId==id&&s.ProductId==productId))throw;}
            }
            isSaved = true;
        }

        if (isAjax)
        {
            return Json(new { success = true, saved = isSaved, productId });
        }

        TempData["Success"]="Saved_Added";return LocalRedirect(Url.IsLocalUrl(returnUrl)?returnUrl!:Url.Action(nameof(Index))!);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId){var id=users.GetUserId(User)!;await db.SavedProducts.Where(s=>s.UserId==id&&s.ProductId==productId).ExecuteDeleteAsync();return RedirectToAction(nameof(Index));}
}
