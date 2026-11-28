using NileShop.Models;
namespace NileShop.Services;

/// <summary>Pure offer maths shared by product cards, cart, checkout and the order total, so every screen shows the same price.</summary>
public static class OfferPricing
{
    public record Applied(decimal Price, Offer Offer);

    public static DateTime StoreNow() => CairoTime.Now;
    public static bool IsLive(Offer o, DateTime nowLocal) => o.IsActive && (o.StartsAt is null || o.StartsAt <= nowLocal) && (o.EndsAt is null || o.EndsAt >= nowLocal);

    public static bool Matches(Offer o, int productId, int categoryId) => (o.Kind == OfferKinds.Percent || o.Kind == OfferKinds.Fixed) && o.Scope switch
    {
        OfferScopes.All => true,
        OfferScopes.Category => o.CategoryId == categoryId,
        OfferScopes.Products => ParseIds(o.ProductIds).Contains(productId),
        _ => false
    };
    public static HashSet<int> ParseIds(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToHashSet();

    /// <summary>Price after one offer, rounded to piasters. Null when the offer would not lower the price.</summary>
    public static decimal? Reduce(decimal price, Offer o)
    {
        if (price <= 0 || o.Value <= 0) return null;
        decimal result = o.Kind switch
        {
            OfferKinds.Percent => o.Value >= 100 ? 0m : decimal.Round(price * (100m - o.Value) / 100m, 2, MidpointRounding.AwayFromZero),
            OfferKinds.Fixed => decimal.Round(price - o.Value, 2, MidpointRounding.AwayFromZero),
            _ => price
        };
        result = Math.Max(result, 0.01m);
        return result < price ? result : null;
    }

    /// <summary>Whole-order discount: the largest single order offer whose minimum is met. Never stacks, never takes the items below 0.01.</summary>
    public static (decimal Amount, Offer? Offer) OrderDiscount(decimal subtotal, IEnumerable<Offer> live)
    {
        decimal best = 0m; Offer? by = null;
        if (subtotal <= 0.01m) return (0m, null);
        foreach (var o in live)
        {
            if (o.Kind != OfferKinds.OrderPercent && o.Kind != OfferKinds.OrderFixed) continue;
            if (o.Value <= 0 || subtotal < (o.MinSubtotal ?? 0m)) continue;
            var amount = o.Kind == OfferKinds.OrderPercent
                ? decimal.Round(subtotal * Math.Min(o.Value, 100m) / 100m, 2, MidpointRounding.AwayFromZero)
                : decimal.Round(o.Value, 2, MidpointRounding.AwayFromZero);
            amount = Math.Min(amount, subtotal - 0.01m);
            if (amount > best || (amount == best && by is not null && o.Id < by.Id)) { best = amount; by = o; }
        }
        return (best, by);
    }

    /// <summary>The single lowest price among live offers. Offers never stack.</summary>
    public static Applied? Best(int productId, int categoryId, decimal price, IEnumerable<Offer> live)
    {
        Applied? best = null;
        foreach (var o in live)
        {
            if (!Matches(o, productId, categoryId)) continue;
            var r = Reduce(price, o);
            if (r is null) continue;
            if (best is null || r < best.Price || (r == best.Price && o.Id < best.Offer.Id)) best = new Applied(r.Value, o);
        }
        return best;
    }

    /// <summary>Combines the store-wide free delivery setting with live free-delivery offers: lowest threshold wins.</summary>
    public static (bool Enabled, decimal Threshold, Offer? Offer) FreeDelivery(bool settingEnabled, decimal settingThreshold, IEnumerable<Offer> live)
    {
        var enabled = settingEnabled; var threshold = settingThreshold; Offer? by = null;
        foreach (var o in live.Where(x => x.Kind == OfferKinds.FreeDelivery))
        {
            var min = o.MinSubtotal ?? 0m;
            if (!enabled || min < threshold) { enabled = true; threshold = min; by = o; }
        }
        return (enabled, threshold, by);
    }
}
