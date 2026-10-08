// Manages a single WebSocket connection to the Gemini Live API.
// Each voice call session gets its own GeminiLiveConnection instance.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BubuVoiceRelay.Server.Gemini;

public sealed class GeminiLiveConnection : IAsyncDisposable
{
    private const string BaseUri =
        "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent";

    private readonly ClientWebSocket _ws = new();
    private readonly ILogger<GeminiLiveConnection> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // The buffer must be large enough for audio chunks from Gemini (24kHz PCM).
    // Gemini sends chunks of ~8KB-64KB typically.
    private readonly byte[] _recvBuffer = new byte[128 * 1024];

    public bool IsConnected => _ws.State == WebSocketState.Open;

    /// <summary>Fires when Gemini sends audio data (base64-decoded PCM bytes).</summary>
    public event Action<byte[]>? OnAudioReceived;

    /// <summary>Fires when Gemini sends a text transcript part.</summary>
    public event Action<string>? OnTranscriptReceived;

    /// <summary>Fires when a model turn is complete.</summary>
    public event Action? OnTurnComplete;

    /// <summary>Fires when the model's response is interrupted (barge-in).</summary>
    public event Action? OnInterrupted;

    /// <summary>Fires when Gemini requests a function call.</summary>
    public event Action<FunctionCall>? OnToolCall;

    /// <summary>Fires on error or disconnect.</summary>
    public event Action<string>? OnError;

    public GeminiLiveConnection(ILogger<GeminiLiveConnection> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens the WebSocket to Gemini, sends the setup message, and starts
    /// the receive loop. Returns once setup is confirmed.
    /// </summary>
    public async Task ConnectAsync(string apiKey, string modelName)
    {
        var uri = new Uri($"{BaseUri}?key={apiKey}");
        _logger.LogInformation("Connecting to Gemini Live API...");

        await _ws.ConnectAsync(uri, _cts.Token);
        _logger.LogInformation("WebSocket connected, sending setup...");

        // Send the setup message
        var setup = GeminiSessionSetup.Build(modelName);
        await SendJsonAsync(setup);

        // Wait for setupComplete
        var response = await ReceiveJsonAsync();
        if (response?.SetupComplete is null)
        {
            var raw = JsonSerializer.Serialize(response, _jsonOpts);
            throw new InvalidOperationException($"Expected setupComplete, got: {raw}");
        }

        _logger.LogInformation("Gemini session ready (model: {Model})", setup.Setup?.Model ?? "unknown");
    }

    /// <summary>Starts the background receive loop that processes Gemini messages.</summary>
    public void StartReceiveLoop()
    {
        _ = Task.Run(ReceiveLoopAsync);
    }

    /// <summary>Sends raw PCM audio bytes to Gemini as a realtimeInput message.</summary>
    public async Task SendAudioAsync(byte[] pcmBytes)
    {
        if (!IsConnected) return;

        var msg = new RealtimeInputMessage
        {
            RealtimeInput = new RealtimeInput
            {
                MediaChunks =
                [
                    new MediaChunk
                    {
                        MimeType = "audio/pcm;rate=16000",
                        Data = Convert.ToBase64String(pcmBytes)
                    }
                ]
            }
        };

        await SendJsonAsync(msg);
    }

    /// <summary>Sends a text message to Gemini via clientContent.</summary>
    public async Task SendTextAsync(string text)
    {
        if (!IsConnected) return;

        var msg = new ClientContentMessage
        {
            ClientContent = new ClientContent
            {
                Turns =
                [
                    new ClientTurn
                    {
                        Parts = [new Part { Text = text }]
                    }
                ]
            }
        };

        await SendJsonAsync(msg);
    }

    /// <summary>Sends a tool response back to Gemini after executing a function.</summary>
    public async Task SendToolResponseAsync(string name, string callId, Dictionary<string, object> result)
    {
        if (!IsConnected) return;

        var msg = new ToolResponseMessage
        {
            ToolResponse = new ToolResponse
            {
                FunctionResponses =
                [
                    new FunctionResponse
                    {
                        Name = name,
                        Id = callId,
                        Response = result
                    }
                ]
            }
        };

        await SendJsonAsync(msg);
        _logger.LogInformation("Sent tool response for {Name} (id={Id})", name, callId);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task SendJsonAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, _jsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    private async Task<GeminiServerMessage?> ReceiveJsonAsync()
    {
        var result = await _ws.ReceiveAsync(_recvBuffer, _cts.Token);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            throw new InvalidOperationException($"WebSocket closed by Google: {_ws.CloseStatus} - {_ws.CloseStatusDescription}");
        }

        // Handle messages that span multiple frames
        using var ms = new MemoryStream();
        ms.Write(_recvBuffer, 0, result.Count);

        while (!result.EndOfMessage)
        {
            result = await _ws.ReceiveAsync(_recvBuffer, _cts.Token);
            ms.Write(_recvBuffer, 0, result.Count);
        }

        var json = Encoding.UTF8.GetString(ms.ToArray());
        return JsonSerializer.Deserialize<GeminiServerMessage>(json, _jsonOpts);
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (IsConnected && !_cts.IsCancellationRequested)
            {
                var message = await ReceiveJsonAsync();
                if (message is null)
                {
                    _logger.LogWarning("Gemini WebSocket closed");
                    OnError?.Invoke("Gemini connection closed");
                    break;
                }

                ProcessMessage(message);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "Gemini WebSocket error");
            OnError?.Invoke($"WebSocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in Gemini receive loop");
            OnError?.Invoke($"Error: {ex.Message}");
        }
    }

    private void ProcessMessage(GeminiServerMessage msg)
    {
        // 1. Tool calls
        if (msg.ToolCall?.FunctionCalls is { Count: > 0 } calls)
        {
            foreach (var call in calls)
            {
                _logger.LogInformation("Tool call: {Name}({Args})", call.Name,
                    call.Args is not null ? JsonSerializer.Serialize(call.Args) : "{}");
                OnToolCall?.Invoke(call);
            }
            return;
        }

        // 2. Tool call cancellation
        if (msg.ToolCallCancellation is not null)
        {
            _logger.LogInformation("Tool call cancelled by Gemini");
            return;
        }

        // 3. Server content (audio / text / turn signals)
        if (msg.ServerContent is { } content)
        {
            if (content.Interrupted == true)
            {
                _logger.LogDebug("Gemini turn interrupted (barge-in)");
                OnInterrupted?.Invoke();
            }

            if (content.ModelTurn?.Parts is { } parts)
            {
                foreach (var part in parts)
                {
                    if (part.InlineData is { } audio && !string.IsNullOrEmpty(audio.Data))
                    {
                        var pcmBytes = Convert.FromBase64String(audio.Data);
                        OnAudioReceived?.Invoke(pcmBytes);
                    }

                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        OnTranscriptReceived?.Invoke(part.Text);
                    }
                }
            }

            if (content.TurnComplete == true)
            {
                _logger.LogDebug("Gemini turn complete");
                OnTurnComplete?.Invoke();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended",
                    CancellationToken.None);
            }
            catch { /* best-effort close */ }
        }
        _ws.Dispose();
        _cts.Dispose();
    }
}
