using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PictureNames.Server.Data;
using PictureNames.Server.Hubs;
using PictureNames.Server.Services;
using PictureNames.Server.Services.Dto;
using PictureNames.Server.Services.Notifications;

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
builder.Services.AddSingleton<ILobbyNotifier, SignalRLobbyNotifier>();

var app = builder.Build();

// === Pipeline ===

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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