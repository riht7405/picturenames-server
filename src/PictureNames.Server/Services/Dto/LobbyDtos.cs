using PictureNames.Server.Entities;

namespace PictureNames.Server.Services.Dto;

// Запрос на создание комнаты
public record CreateRoomRequest(string Nickname);

// Запрос на вход в комнату
public record JoinRoomRequest(string Nickname);

// Снимок лобби — то, что отправляем клиенту
public record LobbyDto(
    Guid RoomId,
    string Code,
    RoomState State,
    Guid? HostId,
    IReadOnlyList<PlayerDto> Players,
    IReadOnlyList<TeamDto> Teams
);

// Игрок в лобби. Обрати внимание: нет ConnectionId. Наружу не уходит.
public record PlayerDto(
    Guid Id,
    string Nickname,
    Guid? TeamId,
    TeamColor? TeamColor,
    PlayerRole Role,
    bool IsHost,
    bool IsConnected
);

// Член команды — для боковых панелей
public record TeamMemberDto(
    string Nickname,
    PlayerRole Role,
    bool IsYou
);

// Команда
public record TeamDto(
    Guid Id,
    TeamColor Color,
    int Score,
    Guid? SpymasterId,
    IReadOnlyList<TeamMemberDto> Members
);