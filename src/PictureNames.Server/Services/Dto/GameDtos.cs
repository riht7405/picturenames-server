using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

// Запрос: назначить игрока в команду (teamColor = null → выйти из команды)
public record AssignTeamRequest(Guid PlayerId, TeamColor? TeamColor);

// Запрос: сменить роль
public record AssignRoleRequest(Guid PlayerId, PlayerRole Role);

// Запрос: старт игры (только хост)
public record StartGameRequest(Guid PlayerId);

// Карта для спаймастера — с цветом
public record CardForSpymasterDto(
    Guid Id,
    int Position,
    string ImageUrl,
    string? AltText,
    CardColor Color,
    bool IsRevealed
);

// Карта для оперативника — БЕЗ цвета, если не открыта
public record CardForOperativeDto(
    Guid Id,
    int Position,
    string ImageUrl,
    string? AltText,
    CardColor? Color,   // null, если IsRevealed = false
    bool IsRevealed
);

// Полное состояние партии — для оперативника
public record GameStateForOperativeDto(
    Guid RoomId,
    string Code,
    RoomState State,
    TeamColor? CurrentTurnTeam,
    TeamColor? YourTeam,
    PlayerRole YourRole,
    IReadOnlyList<CardForOperativeDto> Cards,
    IReadOnlyList<TeamDto> Teams
);

// Полное состояние партии — для спаймастера
public record GameStateForSpymasterDto(
    Guid RoomId,
    string Code,
    RoomState State,
    TeamColor? CurrentTurnTeam,
    TeamColor? YourTeam,
    PlayerRole YourRole,
    IReadOnlyList<CardForSpymasterDto> Cards,
    IReadOnlyList<TeamDto> Teams
);