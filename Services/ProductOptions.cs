namespace NileShop.Services;
public static class ProductOptions
{
    public static string[] Values(string? text) => (text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().Take(20).ToArray();
    public static bool Valid(string? options, string? selected) => Values(options) is var values && (values.Length == 0 ? string.IsNullOrEmpty(selected) : values.Contains(selected));
}
