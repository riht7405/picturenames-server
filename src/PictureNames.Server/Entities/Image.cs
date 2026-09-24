namespace PictureNames.Server.Entities;

public class Image
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Url { get; set; } = string.Empty;
    public string? AltText { get; set; }

    // Для MVP — просто строка "animals,food". Позже можно вынести в отдельную таблицу
    public string? Tags { get; set; }

    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}