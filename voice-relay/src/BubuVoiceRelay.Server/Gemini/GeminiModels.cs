// Gemini Live API (BidiGenerateContent) JSON message models.
// These map 1:1 to the WebSocket protocol at
// wss://generativelanguage.googleapis.com/ws/...BidiGenerateContent

using System.Text.Json.Serialization;

namespace BubuVoiceRelay.Server.Gemini;

// ── Client → Gemini messages ────────────────────────────────────────────────

/// <summary>The very first message on the WebSocket — configures the session.</summary>
public sealed class SetupMessage
{
    [JsonPropertyName("setup")]
    public SetupPayload Setup { get; set; } = new();
}

public sealed class SetupPayload
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "models/gemini-3.8-live";

    [JsonPropertyName("generationConfig")]
    public GenerationConfig? GenerationConfig { get; set; }

    [JsonPropertyName("systemInstruction")]
    public ContentPayload? SystemInstruction { get; set; }

    [JsonPropertyName("tools")]
    public List<ToolDeclaration>? Tools { get; set; }
}

public sealed class GenerationConfig
{
    [JsonPropertyName("responseModalities")]
    public List<string> ResponseModalities { get; set; } = ["AUDIO"];

    [JsonPropertyName("speechConfig")]
    public SpeechConfig? SpeechConfig { get; set; }
}

public sealed class SpeechConfig
{
    [JsonPropertyName("voiceConfig")]
    public VoiceConfig? VoiceConfig { get; set; }
}

public sealed class VoiceConfig
{
    [JsonPropertyName("prebuiltVoiceConfig")]
    public PrebuiltVoiceConfig? PrebuiltVoiceConfig { get; set; }
}

public sealed class PrebuiltVoiceConfig
{
    [JsonPropertyName("voiceName")]
    public string VoiceName { get; set; } = "Aoede";
}

public sealed class ContentPayload
{
    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; } = [];
}

public sealed class Part
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

// ── Tool declarations ────────────────────────────────────────────────────────

public sealed class ToolDeclaration
{
    [JsonPropertyName("functionDeclarations")]
    public List<FunctionDeclaration> FunctionDeclarations { get; set; } = [];
}

public sealed class FunctionDeclaration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("parameters")]
    public ParameterSchema? Parameters { get; set; }
}

public sealed class ParameterSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "OBJECT";

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

public sealed class PropertySchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "STRING";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

// ── Realtime audio input ─────────────────────────────────────────────────────

public sealed class RealtimeInputMessage
{
    [JsonPropertyName("realtimeInput")]
    public RealtimeInput RealtimeInput { get; set; } = new();
}

public sealed class RealtimeInput
{
    [JsonPropertyName("mediaChunks")]
    public List<MediaChunk> MediaChunks { get; set; } = [];
}

public sealed class MediaChunk
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "audio/pcm;rate=16000";

    [JsonPropertyName("data")]
    public string Data { get; set; } = ""; // base64
}

// ── Client Content (Text) ──────────────────────────────────────────────────

public sealed class ClientContentMessage
{
    [JsonPropertyName("clientContent")]
    public ClientContent ClientContent { get; set; } = new();
}

public sealed class ClientContent
{
    [JsonPropertyName("turns")]
    public List<ClientTurn> Turns { get; set; } = [];

    [JsonPropertyName("turnComplete")]
    public bool TurnComplete { get; set; } = true;
}

public sealed class ClientTurn
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("parts")]
    public List<Part> Parts { get; set; } = [];
}

// ── Tool response (after executing a function call) ──────────────────────────

public sealed class ToolResponseMessage
{
    [JsonPropertyName("toolResponse")]
    public ToolResponse ToolResponse { get; set; } = new();
}

public sealed class ToolResponse
{
    [JsonPropertyName("functionResponses")]
    public List<FunctionResponse> FunctionResponses { get; set; } = [];
}

public sealed class FunctionResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("response")]
    public Dictionary<string, object> Response { get; set; } = [];
}

// ── Gemini → Client (server messages) ────────────────────────────────────────
// These are parsed from the JSON the Gemini WebSocket sends us.

/// <summary>
/// A raw JSON envelope from Gemini. We inspect it to determine the message type.
/// The actual parsing is done in GeminiMessageParser.
/// </summary>
public sealed class GeminiServerMessage
{
    // Setup complete confirmation
    [JsonPropertyName("setupComplete")]
    public object? SetupComplete { get; set; }

    [JsonPropertyName("serverContent")]
    public ServerContent? ServerContent { get; set; }

    [JsonPropertyName("toolCall")]
    public ToolCallPayload? ToolCall { get; set; }

    [JsonPropertyName("toolCallCancellation")]
    public object? ToolCallCancellation { get; set; }
}

public sealed class ServerContent
{
    [JsonPropertyName("modelTurn")]
    public ModelTurn? ModelTurn { get; set; }

    [JsonPropertyName("turnComplete")]
    public bool? TurnComplete { get; set; }

    [JsonPropertyName("interrupted")]
    public bool? Interrupted { get; set; }
}

public sealed class ModelTurn
{
    [JsonPropertyName("parts")]
    public List<ServerPart> Parts { get; set; } = [];
}

public sealed class ServerPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("inlineData")]
    public InlineData? InlineData { get; set; }
}

public sealed class InlineData
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    [JsonPropertyName("data")]
    public string Data { get; set; } = ""; // base64 PCM audio
}

public sealed class ToolCallPayload
{
    [JsonPropertyName("functionCalls")]
    public List<FunctionCall> FunctionCalls { get; set; } = [];
}

public sealed class FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("args")]
    public Dictionary<string, object>? Args { get; set; }
}
