namespace NileShop.Services;
public class ProductImageService(IWebHostEnvironment environment)
{
    public bool ValidPath(string? path) => string.IsNullOrWhiteSpace(path) || (path.StartsWith("/assets/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains('\\') && System.Text.RegularExpressions.Regex.IsMatch(path, @"^/assets/[a-zA-Z0-9_./-]+\.(jpg|jpeg|png|webp|gif)$") && File.Exists(Path.Combine(environment.WebRootPath, path.TrimStart('/'))));
    public async Task<IReadOnlyList<string>> SaveAsync(IReadOnlyList<IFormFile> files)
    {
        if (files.Count > 6 || files.Sum(f => f.Length) > 16 * 1024 * 1024) throw new InvalidDataException();
        var images = new List<(byte[] Bytes, string Extension)>();
        foreach (var file in files) {
            if (file.Length is < 12 or > 4 * 1024 * 1024) throw new InvalidDataException();
            using var memory = new MemoryStream(); await file.CopyToAsync(memory); var bytes = memory.ToArray();
            string extension;
            if (bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) extension = ".png";
            else if (bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217) extension = ".jpg";
            else if (System.Text.Encoding.ASCII.GetString(bytes,0,4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes,8,4) == "WEBP") extension = ".webp";
            else throw new InvalidDataException();
            images.Add((bytes, extension));
        }
        var directory = Path.Combine(environment.WebRootPath, "assets", "uploads"); Directory.CreateDirectory(directory);
        var paths = new List<string>();
        foreach (var image in images) { var name = Guid.NewGuid().ToString("N") + image.Extension; await File.WriteAllBytesAsync(Path.Combine(directory, name), image.Bytes); paths.Add("/assets/uploads/" + name); }
        return paths;
    }
}
