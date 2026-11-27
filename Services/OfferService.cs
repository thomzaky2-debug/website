using Microsoft.EntityFrameworkCore;
using NileShop.Data;
using NileShop.Models;
namespace NileShop.Services;

/// <summary>Ambient per-request list of live offers, read by the static product card builder.</summary>
public static class OfferContext
{
    static readonly AsyncLocal<IReadOnlyList<Offer>?> current = new();
    public static IReadOnlyList<Offer> Live { get => current.Value ?? []; set => current.Value = value; }
}
public class OfferService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<Offer>> LiveAsync(CancellationToken ct = default)
    {
        var now = OfferPricing.StoreNow();
        var all = await db.Offers.AsNoTracking().Where(o => o.IsActive).ToListAsync(ct);
        return all.Where(o => OfferPricing.IsLive(o, now)).ToList();
    }
}
