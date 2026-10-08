# Bubu Voice Relay

.NET 10 backend relay that bridges Bubu clients with the **Gemini Live API** for real-time voice conversations.

## Architecture

```
Bubu (Tauri) ←→ WebSocket ←→ .NET Relay ←→ WebSocket ←→ Gemini Live API
   (mic/speaker)              (localhost:5123)            (Google Cloud)
```

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Gemini API key (from Google One AI Premium or [aistudio.google.com](https://aistudio.google.com))

## Quick Start

```powershell
cd voice-relay/src/BubuVoiceRelay.Server
dotnet run
```

The relay starts on `http://localhost:5123`. Bubu connects to `ws://localhost:5123/ws/voice`.

## Setting Your Gemini API Key

In Bubu's Settings window, set the `gemini-api-key` in the Credential Manager. The key is retrieved securely at voice call start and passed to the relay.

## Features

- **Real-time voice** via Gemini Live API (WebSocket, native audio-to-audio)
- **Model**: `gemini-3.8-live` with voice `Aoede`
- **Function calling**: open apps, search web, control system, open URLs, type text
- **Barge-in**: interrupt Bubu while she's speaking
- **Transcript**: subtitles of the conversation
- **Trilingual**: Indonesian, English, Mandarin mixed naturally

## Protocol

### Client → Relay

| Frame Type | Content |
|-----------|---------|
| Text | `{"type":"session.start","apiKey":"..."}` |
| Binary | Raw 16-bit PCM, 16kHz, mono |
| Text | `{"type":"session.end"}` |
| Text | `{"type":"session.interrupt"}` |

### Relay → Client

| Frame Type | Content |
|-----------|---------|
| Text | `{"type":"session.ready"}` |
| Binary | Raw 16-bit PCM, 24kHz, mono (from Gemini) |
| Text | `{"type":"transcript","role":"assistant","text":"..."}` |
| Text | `{"type":"tool.executing","name":"..."}` |
| Text | `{"type":"tool.result","name":"...","success":true}` |
| Text | `{"type":"turn.complete"}` |
| Text | `{"type":"interrupted"}` |
| Text | `{"type":"error","message":"..."}` |
