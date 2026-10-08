// Bubu Voice Relay — ASP.NET Core Minimal API with WebSocket endpoint.
// Runs locally on the user's machine, acts as a bridge between Bubu clients
// and the Gemini Live API.
//
// Usage: dotnet run
// Default: http://localhost:5123/ws/voice

using BubuVoiceRelay.Server.WebSocket;

var builder = WebApplication.CreateBuilder(args);

// Allow CORS from any localhost origin (Tauri dev server, etc.)
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin =>
    {
        var uri = new Uri(origin);
        return uri.Host is "localhost" or "127.0.0.1" or "tauri.localhost";
    })
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Health check
app.MapGet("/", () => "Bubu Voice Relay is running 🎙️");

// WebSocket endpoint for voice sessions
app.Map("/ws/voice", async (HttpContext context, ILoggerFactory loggerFactory) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connection required");
        return;
    }

    var logger = loggerFactory.CreateLogger("VoiceEndpoint");
    logger.LogInformation("New voice session from {Remote}", context.Connection.RemoteIpAddress);

    using var ws = await context.WebSockets.AcceptWebSocketAsync();

    await using var session = new VoiceSession(ws, loggerFactory);
    await session.RunAsync();
});

app.Logger.LogInformation("Bubu Voice Relay starting on http://localhost:5123");
app.Run("http://localhost:5123");
