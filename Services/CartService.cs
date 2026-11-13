using System.Text.Json;
using BubbleThemeShop.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace BubbleThemeShop.Services;
public class CartService(IHttpContextAccessor accessor, CatalogService catalog)
{
    const string Key = "ShoppingCart";
    ISession Session => accessor.HttpContext!.Session;
    public Dictionary<int, int> Read() => JsonSerializer.Deserialize<Dictionary<int, int>>(Session.GetString(Key) ?? "{}") ?? new();
    public int Count => Read().Values.Sum();
    public void Save(Dictionary<int, int> cart) => Session.SetString(Key, JsonSerializer.Serialize(cart));
    public void Clear() => Session.Remove(Key);
    public async Task<CartViewModel> ViewAsync()
    {
        var cart = Read(); var ids = cart.Keys.ToArray();
        var products = await catalog.Active().Where(p => ids.Contains(p.Id)).ToListAsync();
        return new CartViewModel { Lines = products.Select(p => new CartLine(CatalogService.Card(p), cart[p.Id])).ToList() };
    }
}
