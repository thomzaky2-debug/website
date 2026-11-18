using System.Text.Json;
using NileShop.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace NileShop.Services;
public record CartSelection(int ProductId, int Quantity, string? Size = null, string? Color = null)
{
    public string Key => $"{ProductId}:{Uri.EscapeDataString(Size ?? "")}:{Uri.EscapeDataString(Color ?? "")}";
}
public class CartService(IHttpContextAccessor accessor, CatalogService catalog)
{
    const string Key = "ShoppingCartV2";
    ISession Session => accessor.HttpContext!.Session;
    public Dictionary<string, CartSelection> Read() => JsonSerializer.Deserialize<Dictionary<string, CartSelection>>(Session.GetString(Key) ?? "{}") ?? new();
    public int Count => Read().Values.Sum(x => x.Quantity);
    public void Save(Dictionary<string, CartSelection> cart) => Session.SetString(Key, JsonSerializer.Serialize(cart));
    public void Clear() => Session.Remove(Key);
    public async Task<CartViewModel> ViewAsync()
    {
        var cart = Read(); var ids = cart.Values.Select(x => x.ProductId).ToArray();
        var products = await catalog.Active().Where(p => ids.Contains(p.Id)).ToListAsync();
        var activeIds = products.Select(p => p.Id).ToHashSet();
        foreach (var key in cart.Where(x => !activeIds.Contains(x.Value.ProductId)).Select(x => x.Key).ToArray()) cart.Remove(key);
        Save(cart);
        return new CartViewModel { Lines = cart.Values.Where(x => products.Any(p => p.Id == x.ProductId)).Select(x => new CartLine(CatalogService.Card(products.Single(p => p.Id == x.ProductId)), x.Quantity, x.Key, x.Size, x.Color)).ToList() };
    }
}
