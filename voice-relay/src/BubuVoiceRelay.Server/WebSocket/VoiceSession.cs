// Orchestrates a single voice call session between a Bubu client and Gemini.
// One VoiceSession per WebSocket connection from the client.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BubuVoiceRelay.Server.Gemini;
using BubuVoiceRelay.Server.Protocol;
using BubuVoiceRelay.Server.Tools;
using Silk.NET.OpenAL;
using Silk.NET.OpenAL.Extensions.EXT;

namespace BubuVoiceRelay.Server.WebSocket;

public sealed class VoiceSession : IAsyncDisposable
{
    private readonly System.Net.WebSockets.WebSocket _clientWs;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<VoiceSession> _logger;
    private readonly ToolExecutor _toolExecutor;
    private GeminiLiveConnection? _gemini;
    private readonly CancellationTokenSource _cts = new();
    private bool _micMuted = false;

    private static DateTime? _lastDisconnectTime = null;
    private static int _spamCount = 0;

    private readonly byte[] _clientBuffer = new byte[64 * 1024];
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public VoiceSession(
        System.Net.WebSockets.WebSocket clientWs,
        ILoggerFactory loggerFactory)
    {
        _clientWs = clientWs;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<VoiceSession>();
        _toolExecutor = new ToolExecutor(loggerFactory.CreateLogger<ToolExecutor>());
    }

    /// <summary>
    /// Main loop: reads messages from the client, routes audio to Gemini,
    /// and handles session lifecycle.
    /// </summary>
    public async Task RunAsync()
    {
        _logger.LogInformation("Voice session started");

        try
        {
            while (_clientWs.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await _clientWs.ReceiveAsync(_clientBuffer, _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Client requested close");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Client no longer sends mic audio — NAudio captures it server-side.
                    // Binary frames from the client are simply ignored.
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // Read full text message (may span frames)
                    using var ms = new MemoryStream();
                    ms.Write(_clientBuffer, 0, result.Count);
                    while (!result.EndOfMessage)
                    {
                        result = await _clientWs.ReceiveAsync(_clientBuffer, _cts.Token);
                        ms.Write(_clientBuffer, 0, result.Count);
                    }

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    await HandleClientMessage(json);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogWarning("Client disconnected abruptly");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice session error");
        }
        finally
        {
            await DisposeAsync();
            _logger.LogInformation("Voice session ended");
        }
    }

    // ── Client message handlers ──────────────────────────────────────────────

    private async Task HandleClientMessage(string json)
    {
        // Parse the "type" field manually since System.Text.Json polymorphic
        // deserialization needs a concrete type hint.
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "session.start":
                var start = JsonSerializer.Deserialize<SessionStartMessage>(json, JsonOpts);
                if (start is not null)
                    await StartGeminiSession(start.ApiKey, start.Model);
                break;

            case "session.end":
                _logger.LogInformation("Client ended session");
                _cts.Cancel();
                await SendToClientAsync(new SessionEndedMessage { Reason = "user" });
                break;

            case "session.interrupt":
                _logger.LogDebug("Client interrupt (barge-in)");
                // Gemini handles barge-in automatically when new audio arrives
                // while it's still generating, so we just log it here.
                break;

            case "mic.mute":
                if (doc.RootElement.TryGetProperty("muted", out var mutedProp))
                {
                    _micMuted = mutedProp.GetBoolean();
                    _logger.LogInformation("Mic {State}", _micMuted ? "MUTED" : "UNMUTED");
                }
                break;

            default:
                _logger.LogWarning("Unknown client message type: {Type}", type);
                break;
        }
    }



    private int _audioChunkCount = 0;

    private void StartMicCapture()
    {
        var thread = new Thread(() =>
        {
            unsafe
            {
                try
                {
                    var alc = ALContext.GetApi(true); // true = soft
                    if (!alc.TryGetExtension(null, out Capture captureAPI))
                    {
                        _logger.LogError("Silk.NET.OpenAL: Capture extension not supported by the audio driver.");
                        return;
                    }



                    // Device name null = default device.
                    // 16000Hz, Mono 16-bit, buffer size for 2 seconds (32000 samples)
                    var device = captureAPI.CaptureOpenDevice(null, 16000, Silk.NET.OpenAL.BufferFormat.Mono16, 32000);
                    
                    if (device == null)
                    {
                        _logger.LogError("Silk.NET.OpenAL: Failed to open capture device.");
                        return;
                    }

                    captureAPI.CaptureStart(device);
                    _logger.LogInformation("OpenAL mic capture started at 16kHz mono");

                    while (!_cts.IsCancellationRequested)
                    {
                        int samplesAvailable = captureAPI.GetAvailableSamples(device);

                        if (samplesAvailable >= 1600) // Read in ~100ms chunks (1600 samples)
                        {
                            if (!_micMuted && _gemini is not null && _gemini.IsConnected)
                            {
                                short[] buffer = new short[samplesAvailable];
                                fixed (short* pBuffer = buffer)
                                {
                                    captureAPI.CaptureSamples(device, pBuffer, samplesAvailable);
                                }

                                byte[] pcm = new byte[samplesAvailable * 2];
                                Buffer.BlockCopy(buffer, 0, pcm, 0, pcm.Length);

                                _audioChunkCount++;
                                if (_audioChunkCount % 50 == 1)
                                {
                                    double rms = 0;
                                    for (int i = 0; i < buffer.Length; i++)
                                    {
                                        rms += buffer[i] * buffer[i];
                                    }
                                    rms = Math.Sqrt(rms / buffer.Length);
                                    _logger.LogInformation(
                                        "OpenAL chunk #{Count}: {Bytes} bytes, RMS={Rms:F0}",
                                        _audioChunkCount, pcm.Length, rms);
                                }

                                _gemini.SendAudioAsync(pcm).GetAwaiter().GetResult();
                            }
                            else
                            {
                                // Drain buffer if muted or disconnected
                                short[] drop = new short[samplesAvailable];
                                fixed (short* pDrop = drop)
                                {
                                    captureAPI.CaptureSamples(device, pDrop, samplesAvailable);
                                }
                            }
                        }

                        Thread.Sleep(10);
                    }

                    captureAPI.CaptureStop(device);
                    captureAPI.CaptureCloseDevice(device);
                    _logger.LogInformation("OpenAL mic capture stopped.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenAL mic capture failed — is a microphone connected?");
                }
            }
        });
        
        thread.IsBackground = true;
        thread.Name = "MicCaptureThread";
        thread.Start();
    }

    // ── Gemini session lifecycle ─────────────────────────────────────────────

    private async Task StartGeminiSession(string apiKey, string modelName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await SendToClientAsync(new ErrorMessage { Message = "API key is required" });
            return;
        }

        try
        {
            _gemini = new GeminiLiveConnection(
                _loggerFactory.CreateLogger<GeminiLiveConnection>());

            // Wire up Gemini events → client
            _gemini.OnAudioReceived += OnGeminiAudio;
            _gemini.OnTranscriptReceived += OnGeminiTranscript;
            _gemini.OnTurnComplete += OnGeminiTurnComplete;
            _gemini.OnInterrupted += OnGeminiInterrupted;
            _gemini.OnToolCall += OnGeminiToolCall;
            _gemini.OnError += OnGeminiError;

            await _gemini.ConnectAsync(apiKey, modelName);
            _gemini.StartReceiveLoop();
            StartMicCapture();

            // Kickstart the conversation contextually based on time since last call
            string prompt = "Halo Bubu! Aku sudah terhubung. Tolong sapa aku dengan suaramu yang imut dan singkat saja!";
            if (_lastDisconnectTime.HasValue)
            {
                var diff = DateTime.Now - _lastDisconnectTime.Value;
                if (diff.TotalMinutes < 2)
                {
                    _spamCount++;
                    if (_spamCount >= 3)
                    {
                        prompt = $"Halo Bubu! Ini udah ke-{_spamCount} kalinya aku nelpon kamu berturut-turut dalam waktu dekat (aku spam nelpon kamu). Tolong bereaksi kesal/judes dong, bilang 'kamu ngapain sih spam nelpon terus, gabut ya?!' pake nada menghela napas [sighs], lalu ancam mau matiin telpon [HANG_UP] kalau aku emang cuma iseng!";
                    }
                    else
                    {
                        prompt = "Halo Bubu! Kita baru aja matiin telpon beberapa detik/menit yang lalu tapi aku udah telpon lagi. Godain aku dong, bilang 'ciee kangen ya baru sebentar udah nelpon lagi' dengan nada manja dan imut!";
                    }
                }
                else if (diff.TotalHours < 2)
                {
                    _spamCount = 0;
                    prompt = "Halo Bubu! Aku telpon lagi nih padahal belum lama kita telponan. Sapa aku dengan nada manja karena kangen ya!";
                }
                else
                {
                    _spamCount = 0;
                    prompt = "Halo Bubu! Akhirnya kita telponan lagi setelah beberapa jam. Sapa aku dong, bilang kangen banget!";
                }
            }
            await _gemini.SendTextAsync(prompt);

            await SendToClientAsync(new SessionReadyMessage());
            _logger.LogInformation("Gemini session connected and ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Gemini");
            await SendToClientAsync(new ErrorMessage { Message = $"Gemini connection failed: {ex.Message}" });
        }
    }

    // ── Gemini event handlers → forward to client ────────────────────────────

    private void OnGeminiAudio(byte[] pcmBytes)
    {
        // Send raw PCM audio bytes to the client as a binary WebSocket frame
        _ = SendBinaryToClientAsync(pcmBytes);
    }

    private string _turnTranscriptBuffer = "";

    private void OnGeminiTranscript(string text)
    {
        _turnTranscriptBuffer += text;
        
        bool isHangup = _turnTranscriptBuffer.Contains("[HANG_UP]", StringComparison.OrdinalIgnoreCase) || 
                        _turnTranscriptBuffer.Contains("[HANG UP]", StringComparison.OrdinalIgnoreCase);

        string display = text;
        if (isHangup && display.Contains("["))
        {
            // Just a quick cleanup so the bracket doesn't look ugly if it was sent in this chunk
            display = display.Replace("[HANG_UP]", "", StringComparison.OrdinalIgnoreCase).Replace("[", "").Replace("]", "").Trim();
        }

        if (!string.IsNullOrEmpty(display))
        {
            _ = SendToClientAsync(new TranscriptMessage { Role = "assistant", Text = display });
        }

        if (isHangup)
        {
            _logger.LogInformation("Bubu initiated HANG_UP! Ending session.");
            _turnTranscriptBuffer = ""; // Reset to avoid triggering multiple times
            
            // Give Bubu a few seconds to finish speaking the final audio frame
            _ = Task.Run(async () => {
                await Task.Delay(3000); 
                await SendToClientAsync(new SessionEndedMessage { Reason = "bubu_hangup" });
                _cts.Cancel();
            });
        }
    }

    private void OnGeminiTurnComplete()
    {
        _turnTranscriptBuffer = "";
        _ = SendToClientAsync(new TurnCompleteMessage());
    }

    private void OnGeminiInterrupted()
    {
        _ = SendToClientAsync(new InterruptedMessage());
    }

    private void OnGeminiToolCall(FunctionCall call)
    {
        _ = HandleToolCallAsync(call);
    }

    private void OnGeminiError(string error)
    {
        _logger.LogError("Gemini error: {Error}", error);
        _ = SendToClientAsync(new ErrorMessage { Message = error });
    }

    /// <summary>
    /// Executes a tool call locally and sends the result back to Gemini
    /// so it can continue the conversation.
    /// </summary>
    private async Task HandleToolCallAsync(FunctionCall call)
    {
        // Notify the client that a tool is being executed
        await SendToClientAsync(new ToolExecutingMessage
        {
            Name = call.Name,
            Args = call.Args
        });

        // Execute the tool
        var result = await _toolExecutor.ExecuteAsync(call.Name, call.Args);

        // Notify the client of the result
        var success = result.TryGetValue("success", out var s) && s is true;
        var message = result.TryGetValue("message", out var m) ? m?.ToString() ?? "" : "";
        await SendToClientAsync(new ToolResultMessage
        {
            Name = call.Name,
            Success = success,
            Message = message
        });

        // Send the result back to Gemini so it can acknowledge vocally
        if (_gemini is not null)
        {
            await _gemini.SendToolResponseAsync(call.Name, call.Id, result);
        }
    }

    // ── WebSocket send helpers ───────────────────────────────────────────────

    private async Task SendToClientAsync<T>(T message)
    {
        if (_clientWs.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync();
        try
        {
            await _clientWs.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send to client");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendBinaryToClientAsync(byte[] data)
    {
        if (_clientWs.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync();
        try
        {
            await _clientWs.SendAsync(data, WebSocketMessageType.Binary, true, _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send audio to client");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lastDisconnectTime = DateTime.Now;

        if (_gemini is not null)
        {
            await _gemini.DisposeAsync();
            _gemini = null;
        }
        _cts.Dispose();
        _sendLock.Dispose();
    }
}
