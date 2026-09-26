using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

// === Лобби (используется RoomService) ===
public record AssignTeamRequest(Guid PlayerId, TeamColor? TeamColor);
public record AssignRoleRequest(Guid PlayerId, PlayerRole Role);
public record StartGameRequest(Guid PlayerId);

// === Партия ===
public record GiveClueRequest(Guid PlayerId, string Word, int Number);
public record RevealCardRequest(Guid PlayerId, Guid CardId);

public enum RevealOutcome
{
    Correct,
    WrongTeam,
    Neutral,
    Assassin,
    Ignored
}

// Текущая подсказка
public record ClueDto(string Word, int Number, TeamColor Team);

// Одна запись в истории подсказок
public record ClueHistoryDto(
    string Word,
    int Number,
    TeamColor Team,
    DateTime At
);

// === Карты ===
public record CardForSpymasterDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor Color, bool IsRevealed
);

public record CardForOperativeDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor? Color, bool IsRevealed
);

// === Полное состояние партии ===
public record GameStateForOperativeDto(
    Guid RoomId,
    string Code,
    RoomState State,
    TeamColor? CurrentTurnTeam,
    TeamColor? YourTeam,
    PlayerRole YourRole,
    bool IsHost,
    ClueDto? CurrentClue,
    int GuessesMade,
    int GuessesAllowed,
    IReadOnlyList<CardForOperativeDto> Cards,
    IReadOnlyList<TeamDto> Teams,
    IReadOnlyList<ClueHistoryDto> ClueHistory
);

public record GameStateForSpymasterDto(
    Guid RoomId,
    string Code,
    RoomState State,
    TeamColor? CurrentTurnTeam,
    TeamColor? YourTeam,
    PlayerRole YourRole,
    bool IsHost,
    ClueDto? CurrentClue,
    int GuessesMade,
    int GuessesAllowed,
    IReadOnlyList<CardForSpymasterDto> Cards,
    IReadOnlyList<TeamDto> Teams,
    IReadOnlyList<ClueHistoryDto> ClueHistory
);