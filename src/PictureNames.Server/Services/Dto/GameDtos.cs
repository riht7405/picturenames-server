using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

public record AssignTeamRequest(Guid PlayerId, TeamColor? TeamColor);
public record AssignRoleRequest(Guid PlayerId, PlayerRole Role);
public record StartGameRequest(Guid PlayerId);

public record GiveClueRequest(Guid PlayerId, string Word, int Number);
public record RevealCardRequest(Guid PlayerId, Guid CardId);
public record ToggleVoteRequest(Guid PlayerId, Guid CardId);

public enum RevealOutcome
{
    Correct,
    WrongTeam,
    Neutral,
    Assassin,
    Ignored
}

public record RevealResult(RevealOutcome Outcome, int BonusSeconds);

public record ClueDto(string Word, int Number, TeamColor Team);

public record VoteInfoDto(Guid PlayerId, string Color, bool IsMe);

public record PlayerInGameDto(
    Guid Id,
    string Nickname,
    PlayerRole Role,
    TeamColor? TeamColor,
    bool IsConnected,
    string? VoteColor,
    bool IsHost
);

public record ClueHistoryItemDto(
    string Word,
    int Number,
    TeamColor Team,
    DateTime At,
    string? SpymasterNickname
);

public record CardForSpymasterDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor Color, bool IsRevealed,
    IReadOnlyList<VoteInfoDto> Voters,
    int VotersCount
);

public record CardForOperativeDto(
    Guid Id, int Position, string ImageUrl, string? AltText,
    CardColor? Color, bool IsRevealed,
    IReadOnlyList<VoteInfoDto> Voters,
    int VotersCount
);

public record ActiveVoteDto(
    string Kind,
    Guid? CardId,
    int VotesCount,
    int VotesNeeded,
    DateTime? DeadlineUtc,
    IReadOnlyList<VoteInfoDto> Voters
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
    IReadOnlyList<TeamDto> Teams,
    DateTime ServerNowUtc,
    DateTime? TurnDeadlineUtc,
    DateTime? SpymasterDeadlineUtc,
    ActiveVoteDto? ActiveVote,
    IReadOnlyList<PlayerInGameDto> Players,
    IReadOnlyList<ClueHistoryItemDto> ClueHistory
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
    DateTime ServerNowUtc,
    DateTime? TurnDeadlineUtc,
    DateTime? SpymasterDeadlineUtc,
    ActiveVoteDto? ActiveVote,
    IReadOnlyList<PlayerInGameDto> Players,
    IReadOnlyList<ClueHistoryItemDto> ClueHistory
);