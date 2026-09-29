using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

public record CreateRoomRequest(string Nickname);
public record JoinRoomRequest(string Nickname);

public record GameSettingsDto(
    int GridSize,
    bool TimerEnabled,
    string ImagePack,
    int FirstSpymasterSeconds,
    int SpymasterSeconds,
    int OperativeSeconds,
    int BonusPerCorrectSeconds
);

public record UpdateSettingsRequest(
    Guid PlayerId,
    bool? TimerEnabled,
    string? ImagePack,
    int? FirstSpymasterSeconds,
    int? SpymasterSeconds,
    int? OperativeSeconds,
    int? BonusPerCorrectSeconds
);

public record LobbyDto(
    Guid RoomId,
    string Code,
    RoomState State,
    Guid? HostId,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<TeamDto> Teams,
    GameSettingsDto Settings
);

public record PlayerDto(
    Guid Id,
    string Nickname,
    Guid? TeamId,
    TeamColor? TeamColor,
    PlayerRole Role,
    bool IsHost,
    bool IsConnected
);

public record TeamDto(
    Guid Id,
    TeamColor Color,
    int Score,
    Guid? SpymasterId
);