using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;

namespace PictureNames.Server.Services;

// Сканирует wwwroot/images/{pack}/ и наполняет таблицу Images.
// Папка = ImagePack (например, "default", "memes").
// AltText = имя файла без расширения (cat.jpg → "cat").
public class ImagePackScanner
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImagePackScanner> _logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
    };

    public ImagePackScanner(AppDbContext db, IWebHostEnvironment env, ILogger<ImagePackScanner> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task ScanAsync(CancellationToken ct = default)
    {
        var rootDir = Path.Combine(_env.WebRootPath, "images");
        if (!Directory.Exists(rootDir))
        {
            _logger.LogWarning("Папка {dir} не найдена. Картинок не будет.", rootDir);
            return;
        }

        var packs = Directory.GetDirectories(rootDir).ToList();
        if (packs.Count == 0)
        {
            _logger.LogWarning("В {dir} нет ни одной папки-пакета.", rootDir);
            return;
        }

        // Собираем всё, что есть на диске: Url → (AltText, Pack)
        var onDisk = new Dictionary<string, (string Alt, string Pack)>(StringComparer.OrdinalIgnoreCase);

        foreach (var packDir in packs)
        {
            var pack = Path.GetFileName(packDir);
            var files = Directory.GetFiles(packDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            _logger.LogInformation("Пак {pack}: {count} картинок", pack, files.Count);

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var url = $"/images/{pack}/{Path.GetFileName(file)}";
                onDisk[url] = (fileName, pack);
            }
        }

        if (onDisk.Count == 0)
        {
            _logger.LogWarning("Ни одной картинки на диске.");
            return;
        }

        var existing = await _db.Images.ToListAsync(ct);
        var existingByUrl = existing.ToDictionary(i => i.Url, StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0, removed = 0;

        // Добавляем новые, обновляем AltText/Tags у существующих
        foreach (var (url, info) in onDisk)
        {
            if (existingByUrl.TryGetValue(url, out var img))
            {
                bool dirty = false;
                if (img.AltText != info.Alt) { img.AltText = info.Alt; dirty = true; }
                if (img.Tags != info.Pack) { img.Tags = info.Pack; dirty = true; }
                if (!img.IsPublic) { img.IsPublic = true; dirty = true; }
                if (dirty) updated++;
            }
            else
            {
                _db.Images.Add(new Image
                {
                    Url = url,
                    AltText = info.Alt,
                    Tags = info.Pack,
                    IsPublic = true
                });
                added++;
            }
        }

        // Удаляем из БД те, которых больше нет на диске
        foreach (var img in existing)
        {
            if (!onDisk.ContainsKey(img.Url))
            {
                _db.Images.Remove(img);
                removed++;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Скан картинок завершён. Добавлено: {added}, обновлено: {updated}, удалено: {removed}, всего: {total}",
            added, updated, removed, onDisk.Count);
    }

    // Возвращает список доступных паков (папок с картинками).
    public List<(string Name, int Count)> GetPacks()
    {
        var rootDir = Path.Combine(_env.WebRootPath, "images");
        if (!Directory.Exists(rootDir)) return new List<(string, int)>();

        return Directory.GetDirectories(rootDir)
            .Select(d =>
            {
                var pack = Path.GetFileName(d);
                var count = Directory.GetFiles(d)
                    .Count(f => SupportedExtensions.Contains(Path.GetExtension(f)));
                return (Name: pack, Count: count);
            })
            .OrderBy(p => p.Name)
            .ToList();
    }
}