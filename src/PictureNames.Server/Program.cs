using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Hubs;
using PictureNames.Server.Services;
using PictureNames.Server.Services.Dto;
using PictureNames.Server.Services.Notifications;
using PictureNames.Server.Entities;

var builder = WebApplication.CreateBuilder(args);

// === Сервисы ===

builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=picturenames.db"));

builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<GameService>();
builder.Services.AddSingleton<ILobbyNotifier, SignalRLobbyNotifier>();

var app = builder.Build();

// === Pipeline ===

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Seed placeholder-картинок, если база пустая
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!db.Images.Any())
    {
        for (int i = 1; i <= 50; i++)
        {
            db.Images.Add(new Image
            {
                Url = $"https://picsum.photos/seed/pn{i}/300/300",
                AltText = $"Картинка {i}",
                IsPublic = true
            });
        }
        db.SaveChanges();
    }
}

// app.UseHttpsRedirection();

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

app.MapPost("/api/rooms/{roomId:guid}/start", async (Guid roomId, StartGameRequest req, RoomService rooms, CancellationToken ct) =>
{
    try { await rooms.StartGameAsync(roomId, req.PlayerId, ct); return Results.Ok(new { ok = true }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/rooms/{roomId:guid}/game/{playerId:guid}", async (Guid roomId, Guid playerId, GameService game, CancellationToken ct) =>
{
    try
    {
        var room = await game.GetForOperativeAsync(roomId, playerId, ct);
        // Если игрок — спаймастер, отдаём расширенный DTO
        if (room.YourRole == PlayerRole.Spymaster)
            return Results.Ok(await game.GetForSpymasterAsync(roomId, playerId, ct));
        return Results.Ok(room);
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// === Шаблонный weatherforecast — оставим для быстрой проверки живости сервера ===

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild",
    "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}