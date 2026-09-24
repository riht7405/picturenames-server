using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Services.Dto;

namespace PictureNames.Server.Services;

public class GameService
{
    private readonly AppDbContext _db;

    public GameService(AppDbContext db)
    {
        _db = db;
    }

    public async Task GenerateFieldAsync(Guid roomId, CancellationToken ct = default)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        // Случайно выбираем, кто начинает. Первая команда получает 9 карт.
        var starting = Random.Shared.Next(2) == 0 ? TeamColor.Blue : TeamColor.Red;
        var other = starting == TeamColor.Blue ? TeamColor.Red : TeamColor.Blue;

        var colors = new List<CardColor>();
        colors.AddRange(Enumerable.Repeat(
            starting == TeamColor.Blue ? CardColor.Blue : CardColor.Red, 9));
        colors.AddRange(Enumerable.Repeat(
            other == TeamColor.Blue ? CardColor.Blue : CardColor.Red, 8));
        colors.AddRange(Enumerable.Repeat(CardColor.Neutral, 7));
        colors.Add(CardColor.Assassin);

        // Перемешиваем цвета (Fisher-Yates)
        for (int i = colors.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (colors[i], colors[j]) = (colors[j], colors[i]);
        }

        // Берём все картинки в память — потом перемешаем там.
        // .OrderBy(Guid.NewGuid()) EF Core перевести не может: SQLite не знает такого оператора.
        var allImages = await _db.Images.ToListAsync(ct);

        if (allImages.Count < 25)
            throw new InvalidOperationException($"В базе {allImages.Count} картинок, нужно минимум 25.");

        // Перемешиваем в памяти и берём первые 25
        var images = allImages
            .OrderBy(_ => Random.Shared.Next())
            .Take(25)
            .ToList();

        // Удаляем старые карты, если есть (для реванша)
        var old = _db.Cards.Where(c => c.RoomId == roomId);
        _db.Cards.RemoveRange(old);

        for (int i = 0; i < 25; i++)
        {
            _db.Cards.Add(new Card
            {
                RoomId = roomId,
                ImageId = images[i].Id,
                Position = i,
                Color = colors[i],
                IsRevealed = false
            });
        }

        room.State = RoomState.InGame;
        var startingTeam = room.Teams.First(t => t.Color == starting);
        room.CurrentTurnTeamId = startingTeam.Id;
        room.GuessCount = 0;
        room.GuessesAllowed = 0;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<GameStateForOperativeDto> GetForOperativeAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);

        var cards = state.Cards
            .Select(c => new CardForOperativeDto(
                c.Id, c.Position, c.ImageUrl, c.AltText,
                c.IsRevealed ? c.Color : null,
                c.IsRevealed
            ))
            .ToList();

        return new GameStateForOperativeDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, cards, state.Teams
        );
    }

    public async Task<GameStateForSpymasterDto> GetForSpymasterAsync(Guid roomId, Guid playerId, CancellationToken ct = default)
    {
        var state = await BuildStateAsync(roomId, playerId, ct);

        var cards = state.Cards
            .Select(c => new CardForSpymasterDto(
                c.Id, c.Position, c.ImageUrl, c.AltText, c.Color, c.IsRevealed
            ))
            .ToList();

        return new GameStateForSpymasterDto(
            state.RoomId, state.Code, state.State, state.CurrentTurnTeam,
            state.YourTeam, state.YourRole, cards, state.Teams
        );
    }

    // Внутренние типы: используются как промежуточный слой.
    // Цвет здесь всегда есть — фильтрация происходит на выходе, в публичных DTO.
    private record InternalCard(
        Guid Id,
        int Position,
        string ImageUrl,
        string? AltText,
        CardColor Color,
        bool IsRevealed
    );

    private record InternalState(
        Guid RoomId,
        string Code,
        RoomState State,
        TeamColor? CurrentTurnTeam,
        TeamColor? YourTeam,
        PlayerRole YourRole,
        List<InternalCard> Cards,
        List<TeamDto> Teams
    );

    private async Task<InternalState> BuildStateAsync(Guid roomId, Guid playerId, CancellationToken ct)
    {
        var room = await _db.Rooms
            .Include(r => r.Teams)
            .Include(r => r.Players)
            .Include(r => r.Cards).ThenInclude(c => c.Image)
            .FirstOrDefaultAsync(r => r.Id == roomId, ct)
            ?? throw new InvalidOperationException("Комната не найдена.");

        var player = room.Players.FirstOrDefault(p => p.Id == playerId)
            ?? throw new InvalidOperationException("Игрок не найден в комнате.");

        var currentTurnTeam = room.Teams
            .FirstOrDefault(t => t.Id == room.CurrentTurnTeamId)?.Color;

        var yourTeam = player.TeamId.HasValue
            ? room.Teams.FirstOrDefault(t => t.Id == player.TeamId.Value)?.Color
            : null;

        var cards = room.Cards
            .OrderBy(c => c.Position)
            .Select(c => new InternalCard(
                c.Id, c.Position, c.Image.Url, c.Image.AltText, c.Color, c.IsRevealed
            ))
            .ToList();

        var teams = room.Teams
            .OrderBy(t => t.Color)
            .Select(t => new TeamDto(t.Id, t.Color, t.Score, t.SpymasterId))
            .ToList();

        return new InternalState(
            room.Id, room.Code, room.State,
            currentTurnTeam, yourTeam, player.Role,
            cards, teams
        );
    }
}