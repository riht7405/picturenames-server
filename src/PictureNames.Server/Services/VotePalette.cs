namespace PictureNames.Server.Services;

// Палитра цветов для точек голосования. 12 отличимых на тёмном фоне.
public static class VotePalette
{
    public static readonly string[] Colors =
    {
        "#ff6b6b", // красный
        "#4ecdc4", // бирюзовый
        "#ffe66d", // жёлтый
        "#a06cd5", // фиолетовый
        "#ff9f68", // оранжевый
        "#95e06c", // салатовый
        "#68c3ff", // голубой
        "#ff8ec7", // розовый
        "#c4e538", // лайм
        "#f8b195", // персиковый
        "#7dffcc", // мятный
        "#d4a5ff"  // сиреневый
    };

    // Берём случайный цвет, которого ещё нет среди занятых.
    // Если все заняты — любой случайный.
    public static string PickUnused(IEnumerable<string?> usedColors)
    {
        var used = usedColors
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var free = Colors.Where(c => !used.Contains(c)).ToArray();
        return free.Length > 0
            ? free[Random.Shared.Next(free.Length)]
            : Colors[Random.Shared.Next(Colors.Length)];
    }
}