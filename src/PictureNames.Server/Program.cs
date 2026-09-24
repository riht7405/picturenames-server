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

builder.Services.AddHostedService<RoomCleanupService>();

var app = builder.Build();

// === Статика ===
app.UseDefaultFiles();
app.UseStaticFiles();

// === SignalR ===
app.MapHub<GameHub>("/gamehub");

// === SVG-заглушки картинок ===
// Работает офлайн. Когда появятся настоящие картинки — просто замени URL в БД.
app.MapGet("/img/{seed:int}.svg", (int seed) =>
{
    var hue = (seed * 47) % 360;
    var svg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 300">
          <defs>
            <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stop-color="hsl({hue}, 35%, 22%)"/>
              <stop offset="100%" stop-color="hsl({(hue + 40) % 360}, 30%, 14%)"/>
            </linearGradient>
          </defs>
          <rect width="300" height="300" fill="url(#g)"/>
          <text x="150" y="165" text-anchor="middle" font-family="system-ui, sans-serif"
                font-size="96" font-weight="300" fill="hsl({hue}, 60%, 75%)" opacity="0.85">{seed}</text>
        </svg>
        """;
    return Results.Content(svg, "image/svg+xml");
});

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

// === Автомиграция + seed ===
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Применяем миграции при старте. Если БД нет — создастся.
    // Если есть, но устарела — обновится.
    db.Database.Migrate();

    // Seed картинок, если база пустая
    if (!db.Images.Any())
    {
        for (int i = 1; i <= 50; i++)
        {
            db.Images.Add(new Image
            {
                Url = $"/img/{i}.svg",
                AltText = $"Картинка {i}",
                IsPublic = true
            });
        }
        db.SaveChanges();
    }
}

app.Run();