using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

public record AssignTeamRequest(Guid PlayerId, TeamColor? TeamColor);
public record AssignRoleRequest(Guid PlayerId, PlayerRole Role);
public record StartGameRequest(Guid PlayerId);

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

public record ClueDto(string Word, int Number, TeamColor Team);

public record CardForSpymasterDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor Color, bool IsRevealed
);

public record CardForOperativeDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor? Color, bool IsRevealed
);

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
    IReadOnlyList<TeamDto> Teams
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
    IReadOnlyList<TeamDto> Teams
);