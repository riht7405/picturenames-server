namespace PictureNames.Server.Services;

// Сканирует wwwroot/sounds/{category}/ и отдаёт манифест клиенту.
// Клиент сам выбирает случайный файл из нужной категории.
public class SoundService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SoundService> _logger;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".m4a", ".flac", ".aac"
    };

    public SoundService(IWebHostEnvironment env, ILogger<SoundService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public Dictionary<string, List<string>> GetManifest()
    {
        var result = new Dictionary<string, List<string>>();
        var root = Path.Combine(_env.WebRootPath, "sounds");

        if (!Directory.Exists(root))
        {
            _logger.LogInformation("Папка {dir} не найдена. Звуков не будет.", root);
            return result;
        }

        foreach (var categoryDir in Directory.GetDirectories(root))
        {
            var category = Path.GetFileName(categoryDir);
            var files = Directory.GetFiles(categoryDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .Select(f => $"/sounds/{category}/{Path.GetFileName(f)}")
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
                result[category] = files;
        }

        _logger.LogInformation("Звуков найдено в {n} категориях.", result.Count);
        return result;
    }
}