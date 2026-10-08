// JSON models for the simple protocol between Bubu clients and the .NET relay.
// Text frames carry these JSON envelopes; binary frames carry raw PCM audio.

using System.Text.Json.Serialization;

namespace BubuVoiceRelay.Server.Protocol;

// ── Client → Relay ───────────────────────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionStartMessage), "session.start")]
[JsonDerivedType(typeof(SessionEndMessage), "session.end")]
[JsonDerivedType(typeof(SessionInterruptMessage), "session.interrupt")]
public abstract class ClientMessage;

public sealed class SessionStartMessage : ClientMessage
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "id";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "models/gemini-2.0-flash-exp";
}

public sealed class SessionEndMessage : ClientMessage;

public sealed class SessionInterruptMessage : ClientMessage;

// ── Relay → Client ───────────────────────────────────────────────────────────

public sealed class SessionReadyMessage
{
    [JsonPropertyName("type")]
    public string Type => "session.ready";
}

public sealed class TranscriptMessage
{
    [JsonPropertyName("type")]
    public string Type => "transcript";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "assistant";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

public sealed class ToolExecutingMessage
{
    [JsonPropertyName("type")]
    public string Type => "tool.executing";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("args")]
    public Dictionary<string, object>? Args { get; set; }
}

public sealed class ToolResultMessage
{
    [JsonPropertyName("type")]
    public string Type => "tool.result";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class SessionEndedMessage
{
    [JsonPropertyName("type")]
    public string Type => "session.ended";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "user";
}

public sealed class ErrorMessage
{
    [JsonPropertyName("type")]
    public string Type => "error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public sealed class TurnCompleteMessage
{
    [JsonPropertyName("type")]
    public string Type => "turn.complete";
}

public sealed class InterruptedMessage
{
    [JsonPropertyName("type")]
    public string Type => "interrupted";
}
