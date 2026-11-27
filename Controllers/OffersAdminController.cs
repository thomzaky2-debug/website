using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
using NileShop.Services;
namespace NileShop.Controllers;

public class OfferFormViewModel
{
    public int Id { get; set; }
    public string TitleEn { get; set; } = ""; public string TitleAr { get; set; } = "";
    public string? BadgeEn { get; set; } public string? BadgeAr { get; set; }
    public string Kind { get; set; } = OfferKinds.Percent;
    public string Value { get; set; } = "";
    public string Scope { get; set; } = OfferScopes.All;
    public int? CategoryId { get; set; }
    public int[] ProductIds { get; set; } = [];
    public string? MinSubtotal { get; set; }
    public string? StartsAt { get; set; } public string? EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public List<(int Id, string Name)> Categories { get; set; } = [];
    public List<(int Id, string Name)> Products { get; set; } = [];
    public string? Error { get; set; }
}

[Authorize(Roles = "Admin")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class OffersAdminController(ApplicationDbContext db, AuditService audit) : Controller
{
    const string Fmt = "yyyy-MM-dd'T'HH:mm";
    public async Task<IActionResult> Index()
    {
        var offers = await db.Offers.AsNoTracking().OrderByDescending(o => o.IsActive).ThenByDescending(o => o.Id).ToListAsync();
        ViewData["Now"] = OfferPricing.StoreNow();
        return View(offers);
    }
    public async Task<IActionResult> Create() => View("Form", await Fill(new OfferFormViewModel()));
    public async Task<IActionResult> Edit(int id)
    {
        var o = await db.Offers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id); if (o is null) return NotFound();
        var m = new OfferFormViewModel
        {
            Id = o.Id, TitleEn = o.TitleEn, TitleAr = o.TitleAr, BadgeEn = o.BadgeEn, BadgeAr = o.BadgeAr, Kind = o.Kind,
            Value = o.Value.ToString("0.##", CultureInfo.InvariantCulture), Scope = o.Scope, CategoryId = o.CategoryId,
            ProductIds = OfferPricing.ParseIds(o.ProductIds).ToArray(), MinSubtotal = o.MinSubtotal?.ToString("0.##", CultureInfo.InvariantCulture),
            StartsAt = o.StartsAt?.ToString(Fmt, CultureInfo.InvariantCulture), EndsAt = o.EndsAt?.ToString(Fmt, CultureInfo.InvariantCulture), IsActive = o.IsActive
        };
        return View("Form", await Fill(m));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(OfferFormViewModel m)
    {
        var o = m.Id == 0 ? new Offer() : await db.Offers.FindAsync(m.Id);
        if (o is null) return NotFound();
        var err = Apply(o, m);
        if (err is not null) { m.Error = err; return View("Form", await Fill(m)); }
        if (m.Id == 0) db.Offers.Add(o);
        await db.SaveChangesAsync();
        await audit.LogAsync(m.Id == 0 ? "OFFER_CREATED" : "OFFER_UPDATED", "Offer", $"Offer {o.Id}; {o.Kind} {o.Value.ToString(CultureInfo.InvariantCulture)}; {o.Scope}; active {o.IsActive}");
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var o = await db.Offers.FindAsync(id); if (o is null) return NotFound();
        o.IsActive = !o.IsActive; await db.SaveChangesAsync();
        await audit.LogAsync("OFFER_TOGGLED", "Offer", $"Offer {id}; active {o.IsActive}");
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var o = await db.Offers.FindAsync(id); if (o is null) return NotFound();
        db.Offers.Remove(o); await db.SaveChangesAsync();
        await audit.LogAsync("OFFER_DELETED", "Offer", $"Offer {id}");
        return RedirectToAction(nameof(Index));
    }

    static bool Dec(string? s, out decimal v) => decimal.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out v);
    static DateTime? Date(string? s) => DateTime.TryParseExact((s ?? "").Trim(), Fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    string? Apply(Offer o, OfferFormViewModel m)
    {
        if (string.IsNullOrWhiteSpace(m.TitleEn) || string.IsNullOrWhiteSpace(m.TitleAr)) return "Offer_Err_Title";
        if (!OfferKinds.All.Contains(m.Kind) || !OfferScopes.AllScopes.Contains(m.Scope)) return "Offer_Err_Value";
        decimal value = 0;
        if (m.Kind == OfferKinds.FreeDelivery) { }
        else
        {
            if (!Dec(m.Value, out value) || value <= 0 || value > 10000000) return "Offer_Err_Value";
            if ((m.Kind == OfferKinds.Percent || m.Kind == OfferKinds.OrderPercent) && value > 90) return "Offer_Err_Percent";
        }
        decimal? min = null;
        if (!string.IsNullOrWhiteSpace(m.MinSubtotal)) { if (!Dec(m.MinSubtotal, out var mv) || mv < 0 || mv > 10000000) return "Offer_Err_Value"; min = mv; }
        if (m.Kind == OfferKinds.FreeDelivery && min is null) return "Offer_Err_Min";
        DateTime? s = null, e = null;
        if (!string.IsNullOrWhiteSpace(m.StartsAt)) { s = Date(m.StartsAt); if (s is null) return "Offer_Err_Dates"; }
        if (!string.IsNullOrWhiteSpace(m.EndsAt)) { e = Date(m.EndsAt); if (e is null) return "Offer_Err_Dates"; }
        if (s is not null && e is not null && e <= s) return "Offer_Err_Dates";
        int? cat = null; string? ids = null;
        if (m.Kind is OfferKinds.Percent or OfferKinds.Fixed)
        {
            if (m.Scope == OfferScopes.Category) { if (m.CategoryId is null || !db.Categories.Any(c => c.Id == m.CategoryId)) return "Offer_Err_Scope"; cat = m.CategoryId; }
            if (m.Scope == OfferScopes.Products)
            {
                var chosen = m.ProductIds.Distinct().Take(200).ToArray();
                if (chosen.Length == 0) return "Offer_Err_Scope";
                ids = string.Join(',', chosen); if (ids.Length > 2000) return "Offer_Err_Scope";
            }
        }
        o.TitleEn = m.TitleEn.Trim(); o.TitleAr = m.TitleAr.Trim();
        o.BadgeEn = string.IsNullOrWhiteSpace(m.BadgeEn) ? null : m.BadgeEn.Trim(); o.BadgeAr = string.IsNullOrWhiteSpace(m.BadgeAr) ? null : m.BadgeAr.Trim();
        if (o.TitleEn.Length > 150 || o.TitleAr.Length > 150 || (o.BadgeEn?.Length ?? 0) > 40 || (o.BadgeAr?.Length ?? 0) > 40) return "Offer_Err_Title";
        o.Kind = m.Kind; o.Value = value; o.Scope = m.Kind is OfferKinds.Percent or OfferKinds.Fixed ? m.Scope : OfferScopes.All;
        o.CategoryId = cat; o.ProductIds = ids; o.MinSubtotal = min; o.StartsAt = s; o.EndsAt = e; o.IsActive = m.IsActive;
        return null;
    }
    async Task<OfferFormViewModel> Fill(OfferFormViewModel m)
    {
        m.Categories = (await db.Categories.AsNoTracking().Include(c => c.Translations).OrderBy(c => c.Id).ToListAsync()).Select(c => (c.Id, CatalogService.Name(c))).ToList();
        m.Products = (await db.Products.AsNoTracking().Include(p => p.Translations).OrderBy(p => p.Id).Take(500).ToListAsync()).Select(p => (p.Id, CatalogService.Name(p))).ToList();
        return m;
    }
}
