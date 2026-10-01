using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Entities;
using PictureNames.Server.Hubs;
using PictureNames.Server.Services;
using PictureNames.Server.Services.Dto;
using PictureNames.Server.Services.Notifications;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// === Сервисы ===
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=picturenames.db"));

builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddSingleton<ILobbyNotifier, SignalRLobbyNotifier>();
builder.Services.AddSingleton<IGameNotifier, SignalRGameNotifier>();   // ← новое
builder.Services.AddSingleton<TurnTimerService>();
builder.Services.AddScoped<ImagePackScanner>();// ← новое
builder.Services.AddSingleton<VoteService>();
builder.Services.AddSingleton<SoundService>();

builder.Services.AddHostedService<RoomCleanupService>();

var app = builder.Build();

// === Статика ===
app.UseDefaultFiles();
app.UseStaticFiles();

// === SignalR ===
app.MapHub<GameHub>("/gamehub");

// === REST: Лобби ===
app.MapPost("/api/rooms", async (CreateRoomRequest req, RoomService rooms, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Nickname))
        return Results.BadRequest(new { error = "Ник не может быть пустым." });

    try
    {
        var lobby = await rooms.CreateRoomAsync(req.Nickname, ct);
        return Results.Ok(lobby);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/rooms/{code}/join", async (string code, JoinRoomRequest req, RoomService rooms, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Nickname))
        return Results.BadRequest(new { error = "Ник не может быть пустым." });

    try
    {
        var lobby = await rooms.JoinRoomAsync(code, req.Nickname, ct);
        return Results.Ok(lobby);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/rooms/{roomId:guid}", async (Guid roomId, RoomService rooms, CancellationToken ct) =>
{
    try
    {
        var lobby = await rooms.GetLobbyAsync(roomId, ct);
        return Results.Ok(lobby);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// === REST: Команды и роли ===
app.MapPost("/api/rooms/{roomId:guid}/team", async (Guid roomId, AssignTeamRequest req, RoomService rooms, CancellationToken ct) =>
{
    try { return Results.Ok(await rooms.AssignTeamAsync(roomId, req.PlayerId, req.TeamColor, ct)); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/rooms/{roomId:guid}/role", async (Guid roomId, AssignRoleRequest req, RoomService rooms, CancellationToken ct) =>
{
    try { return Results.Ok(await rooms.AssignRoleAsync(roomId, req.PlayerId, req.Role, ct)); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPut("/api/rooms/{roomId:guid}/settings", async (
    Guid roomId, UpdateSettingsRequest req, RoomService rooms, CancellationToken ct) =>
{
    try
    {
        var lobby = await rooms.UpdateSettingsAsync(roomId, req, ct);
        return Results.Ok(lobby);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// === REST: Партия ===
app.MapPost("/api/rooms/{roomId:guid}/start", async (Guid roomId, StartGameRequest req, RoomService rooms, CancellationToken ct) =>
{
    try
    {
        await rooms.StartGameAsync(roomId, req.PlayerId, ct);
        return Results.Ok(new { ok = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/rooms/{roomId:guid}/game/{playerId:guid}", async (Guid roomId, Guid playerId, GameService game, CancellationToken ct) =>
{
    try
    {
        var asOperative = await game.GetForOperativeAsync(roomId, playerId, ct);
        if (asOperative.YourRole == PlayerRole.Spymaster)
            return Results.Ok(await game.GetForSpymasterAsync(roomId, playerId, ct));
        return Results.Ok(asOperative);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Сканируем wwwroot/images/* и наполняем таблицу Images
    var scanner = scope.ServiceProvider.GetRequiredService<ImagePackScanner>();
    await scanner.ScanAsync();
}

// === REST для отладки ходов (SignalR-путь будет параллельно) ===

app.MapPost("/api/rooms/{roomId:guid}/clue", async (
    Guid roomId, GiveClueRequest req, GameService game, CancellationToken ct) =>
{
    try
    {
        await game.GiveClueAsync(roomId, req.PlayerId, req.Word, req.Number, ct);
        return Results.Ok(new { ok = true });
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});



app.MapPost("/api/rooms/{roomId:guid}/reveal", async (
    Guid roomId, RevealCardRequest req, GameService game, CancellationToken ct) =>
{
    try
    {
        var result = await game.RevealCardAsync(roomId, req.PlayerId, req.CardId, ct);
        return Results.Ok(new
        {
            outcome = result.Outcome.ToString(),
            bonusSeconds = result.BonusSeconds
        });
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/image-packs", (ImagePackScanner scanner) =>
{
    var packs = scanner.GetPacks()
        .Select(p => new { name = p.Name, count = p.Count })
        .ToList();
    return Results.Ok(packs);
});

app.MapGet("/api/sounds", (SoundService sounds) =>
{
    return Results.Ok(sounds.GetManifest());
});

app.Run();