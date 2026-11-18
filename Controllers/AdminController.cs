using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
using NileShop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
namespace NileShop.Controllers;
[Authorize(Roles = "Admin")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class AdminController(ApplicationDbContext db, AuditService audit, CatalogService catalog, ProductImageService images, IStringLocalizer<SharedResource> localizer) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));
    public async Task<IActionResult> Dashboard(int page = 1)
    {
        page = Math.Clamp(page, 1, 100000);
        var count = await db.Products.CountAsync();
        // Decimal aggregation is performed by SQL Server; bounded decimal conversion supports local SQLite QA.
        var revenue = db.Database.IsSqlite()
            ? (decimal)await db.Orders.Where(o => (o.PaymentStatus == "Paid" || o.PaymentStatus == "Overpaid") && o.Status != OrderStatuses.Cancelled).SumAsync(o => (double)o.TotalAmount)
            : await db.Orders.Where(o => (o.PaymentStatus == "Paid" || o.PaymentStatus == "Overpaid") && o.Status != OrderStatuses.Cancelled).SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        return View(new DashboardViewModel {
            Revenue = revenue, TotalOrders = await db.Orders.CountAsync(), ActiveProducts = await db.Products.CountAsync(p => p.IsActive),
            LowStock = await db.Products.CountAsync(p => p.IsActive && p.StockQuantity <= 5), TotalLogs = await db.AuditLogs.CountAsync(),
            Products = await db.Products.AsNoTracking().Include(p => p.Translations).OrderBy(p => p.Id).Skip((page - 1) * 50).Take(50).ToListAsync(),
            Categories = await db.Categories.AsNoTracking().Include(c => c.Translations).OrderBy(c => c.Id).ToListAsync(),
            Orders = await db.Orders.AsNoTracking().Include(o => o.Items).OrderByDescending(o => o.Id).Take(100).ToListAsync(),
            Logs = await db.AuditLogs.AsNoTracking().OrderByDescending(l => l.Id).Take(100).ToListAsync(), Page = page, TotalPages = (int)Math.Ceiling(count / 50d)
        });
    }
    [HttpGet] public async Task<IActionResult> CreateProduct() => View(new ProductFormViewModel { Categories = await catalog.CategoriesAsync() });
    [HttpGet] public async Task<IActionResult> EditProduct(int id)
    {
        var p = await db.Products.AsNoTracking().Include(p => p.Translations).SingleOrDefaultAsync(p => p.Id == id);
        if (p is null) return NotFound();
        var en = p.Translations.FirstOrDefault(t => t.LanguageCode == "en"); var ar = p.Translations.FirstOrDefault(t => t.LanguageCode == "ar");
        return View(new ProductFormViewModel { Sizes = p.Sizes, Colors = p.Colors, GalleryImages = p.GalleryImages, Id = id, OriginalStockQuantity = p.StockQuantity, Slug = p.Slug, Price = p.Price, OldPrice = p.OldPrice, StockQuantity = p.StockQuantity, ImageUrl = p.ImageUrl, CategoryId = p.CategoryId, IsActive = p.IsActive, IsFeatured = p.IsFeatured, NameEn = en?.Name ?? "", NameAr = ar?.Name ?? "", ShortDescriptionEn = en?.ShortDescription, ShortDescriptionAr = ar?.ShortDescription, FullDescriptionEn = en?.FullDescription ?? "", FullDescriptionAr = ar?.FullDescription ?? "", Categories = await catalog.CategoriesAsync() });
    }
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> CreateProduct(ProductFormViewModel model) => SaveProduct(model, false);
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> EditProduct(ProductFormViewModel model) => SaveProduct(model, true);
    async Task<IActionResult> SaveProduct(ProductFormViewModel m, bool edit)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == m.CategoryId)) ModelState.AddModelError(nameof(m.CategoryId), localizer["Validation_Category"]);
        if (await db.Products.AnyAsync(p => p.Slug == m.Slug && (!edit || p.Id != m.Id))) ModelState.AddModelError(nameof(m.Slug), localizer["Validation_UniqueSlug"]);
        if (!images.ValidPath(m.ImageUrl)) ModelState.AddModelError(nameof(m.ImageUrl), localizer["Validation_Image"]);
        if ((m.GalleryImages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(path => !images.ValidPath(path))) ModelState.AddModelError(nameof(m.GalleryImages), localizer["Validation_Image"]);
        if (ProductOptions.Values(m.Sizes).Any(x => x.Length > 100) || ProductOptions.Values(m.Colors).Any(x => x.Length > 100)) ModelState.AddModelError("", localizer["Options_Invalid"]);
        if (string.IsNullOrWhiteSpace(m.ImageUrl) && m.Uploads.Count == 0) ModelState.AddModelError(nameof(m.Uploads), localizer["Images_Required"]);
        if (!ModelState.IsValid) {
            if (m.Uploads.Count > 0) ModelState.AddModelError(nameof(m.Uploads), localizer["Images_Reselect"]);
            m.Categories = await catalog.CategoriesAsync(); return View(edit ? "EditProduct" : "CreateProduct", m);
        }
        try {
            var uploaded = await images.SaveAsync(m.Uploads);
            if (uploaded.Count > 0 && (m.ReplacePrimaryImage || string.IsNullOrEmpty(m.ImageUrl))) m.ImageUrl = uploaded[0];
            if (uploaded.Count > 0) m.GalleryImages = string.Join(',', (m.GalleryImages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Concat(uploaded).Distinct().Take(12));
        } catch (InvalidDataException) { ModelState.AddModelError(nameof(m.Uploads), localizer["Images_Invalid"]); m.Categories = await catalog.CategoriesAsync(); return View(edit ? "EditProduct" : "CreateProduct", m); }
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = edit ? await db.Products.Include(p => p.Translations).SingleOrDefaultAsync(p => p.Id == m.Id) : new Product();
        if (p is null) return NotFound();
        var oldStock = p.StockQuantity;
        p.Sizes = m.Sizes; p.Colors = m.Colors; p.GalleryImages = m.GalleryImages;
        p.Slug = m.Slug; p.Price = m.Price; p.OldPrice = m.OldPrice; p.ImageUrl = m.ImageUrl; p.CategoryId = m.CategoryId; p.IsActive = m.IsActive; p.IsFeatured = m.IsFeatured;
        // Compare-and-swap inventory to avoid overwriting a checkout that races with an admin edit.
        if (edit) {
            var changed = await db.Products.Where(x => x.Id == p.Id && x.StockQuantity == m.OriginalStockQuantity).ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, m.StockQuantity));
            if (changed != 1) { TempData["Error"] = "Error_StockChanged"; return RedirectToAction(nameof(EditProduct), new { id = p.Id }); }
        }
        p.StockQuantity = m.StockQuantity;
        if (edit) { db.Entry(p).Property(x => x.StockQuantity).OriginalValue = m.StockQuantity; db.Entry(p).Property(x => x.StockQuantity).IsModified = false; }
        foreach (var lang in new[] { "en", "ar" }) {
            var t = p.Translations.FirstOrDefault(t => t.LanguageCode == lang);
            if (t is null) { t = new ProductTranslation { LanguageCode = lang }; p.Translations.Add(t); }
            t.Name = lang == "en" ? m.NameEn : m.NameAr;
            t.ShortDescription = lang == "en" ? m.ShortDescriptionEn : m.ShortDescriptionAr;
            t.FullDescription = lang == "en" ? m.FullDescriptionEn : m.FullDescriptionAr;
        }
        if (!edit) db.Products.Add(p);
        await db.SaveChangesAsync();
        await audit.LogAsync(edit ? "UPDATE" : "CREATE", "Product", $"Product {p.Id}: {p.Slug}; stock {oldStock} -> {m.StockQuantity}");
        await tx.CommitAsync(); TempData["Success"] = "Admin_Saved"; return RedirectToAction(nameof(Dashboard));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var changed = await db.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, p => !p.IsActive));
        if (changed == 0) return NotFound();
        await audit.LogAsync("TOGGLE_ACTIVE", "Product", $"Product {id}"); await tx.CommitAsync();
        return RedirectToAction(nameof(Dashboard));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var p = await db.Products.FindAsync(id); if (p is null) return NotFound();
        if (await db.OrderItems.AnyAsync(i => i.ProductId == id) || await db.SavedProducts.AnyAsync(i=>i.ProductId==id) || await db.ProductReviews.AnyAsync(i=>i.ProductId==id)) { p.IsActive = false; TempData["Success"] = "Admin_Archived"; }
        else db.Products.Remove(p);
        try {
            await db.SaveChangesAsync(); await audit.LogAsync("DELETE_OR_ARCHIVE", "Product", $"Product {id}: {p.Slug}"); await tx.CommitAsync();
        } catch (DbUpdateException) { await tx.RollbackAsync(); TempData["Error"] = "Error_InUse"; }
        return RedirectToAction(nameof(Dashboard));
    }
    [HttpGet] public IActionResult CreateCategory() => View(new CategoryFormViewModel());
    [HttpGet] public async Task<IActionResult> EditCategory(int id)
    {
        var c = await db.Categories.AsNoTracking().Include(c => c.Translations).SingleOrDefaultAsync(c => c.Id == id);
        if (c is null) return NotFound();
        return View(new CategoryFormViewModel { Id = id, Slug = c.Slug, NameEn = c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "", NameAr = c.Translations.FirstOrDefault(t => t.LanguageCode == "ar")?.Name ?? "" });
    }
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> CreateCategory(CategoryFormViewModel model) => SaveCategory(model, false);
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> EditCategory(CategoryFormViewModel model) => SaveCategory(model, true);
    async Task<IActionResult> SaveCategory(CategoryFormViewModel m, bool edit)
    {
        if (await db.Categories.AnyAsync(c => c.Slug == m.Slug && (!edit || c.Id != m.Id))) ModelState.AddModelError(nameof(m.Slug), localizer["Validation_UniqueSlug"]);
        if (!ModelState.IsValid) return View(edit ? "EditCategory" : "CreateCategory", m);
        await using var tx = await db.Database.BeginTransactionAsync();
        var c = edit ? await db.Categories.Include(c => c.Translations).SingleOrDefaultAsync(c => c.Id == m.Id) : new Category();
        if (c is null) return NotFound(); c.Slug = m.Slug;
        foreach (var lang in new[] { "en", "ar" }) {
            var t = c.Translations.FirstOrDefault(t => t.LanguageCode == lang);
            if (t is null) { t = new CategoryTranslation { LanguageCode = lang }; c.Translations.Add(t); }
            t.Name = lang == "en" ? m.NameEn : m.NameAr;
        }
        if (!edit) db.Categories.Add(c);
        await db.SaveChangesAsync(); await audit.LogAsync(edit ? "UPDATE" : "CREATE", "Category", $"Category {c.Id}: {c.Slug}"); await tx.CommitAsync();
        TempData["Success"] = "Admin_Saved"; return RedirectToAction(nameof(Dashboard));
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> DeleteCategory(int id)
    {
        var c = await db.Categories.FindAsync(id); if (c is null) return NotFound();
        if (await db.Products.AnyAsync(p => p.CategoryId == id)) { TempData["Error"] = "Error_InUse"; return RedirectToAction(nameof(Dashboard)); }
        await using var tx = await db.Database.BeginTransactionAsync(); db.Categories.Remove(c);
        try { await db.SaveChangesAsync(); await audit.LogAsync("DELETE", "Category", $"Category {id}: {c.Slug}"); await tx.CommitAsync(); }
        catch (DbUpdateException) { await tx.RollbackAsync(); TempData["Error"] = "Error_InUse"; }
        return RedirectToAction(nameof(Dashboard));
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> UpdateOrderStatus(int orderId, string status)
    {
        if (!OrderStatuses.All.Contains(status)) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == orderId);
        if (order is null) return NotFound();
        if (!OrderStatuses.CanTransition(order.Status, status)) { TempData["Error"] = "Error_StatusTransition"; return RedirectToAction(nameof(Dashboard)); }
        if (order.Status == status) return RedirectToAction(nameof(Dashboard));
        if (status == OrderStatuses.Cancelled || (await db.PaymentAttempts.AnyAsync(p => p.OrderId == orderId) && order.PaymentStatus is not ("Paid" or "Overpaid"))) {
            TempData["Error"] = "Payment_AdminReview"; return RedirectToAction(nameof(Dashboard));
        }
        if ((order.PaymentStatus is not ("Paid" or "Overpaid") || order.NeedsPaymentReview) && (status is OrderStatuses.Processing or OrderStatuses.Shipped)) {
            TempData["Error"] = "Payment_AdminReview"; return RedirectToAction(nameof(Dashboard));
        }
        var changed = await db.Orders.Where(o => o.Id == orderId && o.Status == order.Status).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
        if (changed != 1) { TempData["Error"] = "Error_StatusTransition"; return RedirectToAction(nameof(Dashboard)); }
        if (status == OrderStatuses.Cancelled) foreach (var item in order.Items.OrderBy(i => i.ProductId))
            await db.Products.Where(p => p.Id == item.ProductId).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + item.Quantity));
        db.OrderUpdates.Add(new OrderUpdate { OrderId = orderId, Status = status });
        if (status == OrderStatuses.Shipped && !await db.OrderEmails.AnyAsync(e => e.OrderId == orderId && e.Kind == "Shipped")) db.OrderEmails.Add(new OrderEmail { OrderId = orderId, Kind = "Shipped" });
        await audit.LogAsync("ORDER_STATUS", "Order", $"{order.OrderNumber}: {order.Status} -> {status}"); await tx.CommitAsync();
        TempData["Success"] = "Admin_Saved"; return RedirectToAction(nameof(Dashboard));
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> ClearOldLogs()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var cutoff = DateTime.UtcNow.AddDays(-30); var count = await db.AuditLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync();
        await audit.LogAsync("LOG_CLEANUP", "AuditLog", $"Deleted {count} records older than {cutoff:O}"); await tx.CommitAsync();
        TempData["Success"] = "Admin_LogsCleared"; return RedirectToAction(nameof(Dashboard));
    }
}
