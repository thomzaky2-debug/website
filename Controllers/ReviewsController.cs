using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
using NileShop.ViewModels;
namespace NileShop.Controllers;
[Authorize]
public class ReviewsController(ApplicationDbContext db,UserManager<IdentityUser> users) : Controller
{
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int productId,int rating,string text)
    {
        if(!ModelState.IsValid||rating is <1 or >5||string.IsNullOrWhiteSpace(text)||text.Length>1000)return BadRequest();
        var user=await users.GetUserAsync(User);if(user is null)return Challenge();
        var product=await db.Products.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==productId&&p.IsActive);if(product is null)return NotFound();
        if(!await db.Orders.AnyAsync(o=>o.CustomerUserId==user.Id&&(o.PaymentStatus=="Paid"||o.PaymentStatus=="Overpaid")&&!o.NeedsPaymentReview&&o.Status==OrderStatuses.Shipped&&o.Items.Any(i=>i.ProductId==productId)))return Forbid();
        if(await db.ProductReviews.AnyAsync(r=>r.UserId==user.Id&&r.ProductId==productId)){TempData["Error"]="Review_Exists";return RedirectToAction("Details","Products",new{slug=product.Slug});}
        var name=(await users.GetClaimsAsync(user)).FirstOrDefault(c=>c.Type==CustomerProfileViewModel.FullNameClaim)?.Value??"Customer";
        db.ProductReviews.Add(new ProductReview{UserId=user.Id,ProductId=productId,Rating=rating,Text=text.Trim(),DisplayName=name.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"Customer"});
        try{await db.SaveChangesAsync();}catch(DbUpdateException){if(!await db.ProductReviews.AsNoTracking().AnyAsync(r=>r.UserId==user.Id&&r.ProductId==productId))throw;}
        TempData["Success"]="Review_Submitted";return RedirectToAction("Details","Products",new{slug=product.Slug});
    }
}
