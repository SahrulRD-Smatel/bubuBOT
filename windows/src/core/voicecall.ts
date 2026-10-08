// Voice call client — connects to the .NET Voice Relay via WebSocket,
// captures mic audio as 16kHz PCM, plays back Gemini audio at 24kHz.

import { State } from "./state";

const RELAY_URL = "ws://localhost:5123/ws/voice";
const MIC_SAMPLE_RATE = 16000;
const PLAYBACK_SAMPLE_RATE = 24000;

export type VoiceCallState = "idle" | "connecting" | "active" | "error";

export interface VoiceCallEvents {
  onStateChange: (state: VoiceCallState) => void;
  onTranscript: (role: "user" | "assistant", text: string) => void;
  onToolExecuting: (name: string, args: Record<string, unknown>) => void;
  onToolResult: (name: string, success: boolean, message: string) => void;
  onTurnComplete: () => void;
  onInterrupted: () => void;
  onError: (message: string) => void;
}

export class VoiceCallClient {
  private ws: WebSocket | null = null;
  private audioCtx: AudioContext | null = null;
  private micStream: MediaStream | null = null;
  private scriptNode: ScriptProcessorNode | null = null;
  private sourceNode: MediaStreamAudioSourceNode | null = null;

  // Playback queue
  private playbackQueue: Float32Array[] = [];
  private activeSources: AudioBufferSourceNode[] = [];
  private nextPlayTime = 0;

  private state: VoiceCallState = "idle";
  private events: VoiceCallEvents;

  constructor(events: VoiceCallEvents) {
    this.events = events;
  }

  get currentState(): VoiceCallState {
    return this.state;
  }

  private setState(s: VoiceCallState) {
    this.state = s;
    this.events.onStateChange(s);
  }

  // ── Public API ──────────────────────────────────────────────────────────

  /** Start a voice call session. */
  async start(apiKey: string): Promise<void> {
    if (this.state !== "idle") return;
    this.setState("connecting");

    try {
      // 1. Get mic access
      this.micStream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          noiseSuppression: true,
        },
      });
      console.log("[VoiceCall] USING MICROPHONE:", this.micStream.getAudioTracks()[0]?.label);

      // 2. Set up AudioContext for mic capture
      this.audioCtx = new AudioContext({ sampleRate: MIC_SAMPLE_RATE });
      if (this.audioCtx.state === "suspended") {
        await this.audioCtx.resume();
      }
      console.log(`[VoiceCall] AudioContext state is: ${this.audioCtx.state}`);
      this.sourceNode = this.audioCtx.createMediaStreamSource(this.micStream);

      // ScriptProcessorNode for capturing raw PCM (deprecated but works everywhere)
      // Buffer size: 4096 samples at 16kHz ≈ 256ms chunks
      this.scriptNode = this.audioCtx.createScriptProcessor(4096, 1, 1);
      this.scriptNode.onaudioprocess = (e) => this.onMicAudio(e);
      this.sourceNode.connect(this.scriptNode);
      this.scriptNode.connect(this.audioCtx.destination); // required for it to process

      // 3. Connect WebSocket to relay
      this.ws = new WebSocket(RELAY_URL);
      this.ws.binaryType = "arraybuffer";

      this.ws.onopen = () => {
        // Send session.start with the API key and chosen model
        this.ws!.send(
          JSON.stringify({
            type: "session.start",
            apiKey,
            language: "id",
            model: State.settings.geminiModel || "models/gemini-3.8-live",
          }),
        );
      };

      this.ws.onmessage = (e) => this.onRelayMessage(e);

      this.ws.onerror = () => {
        this.events.onError("Koneksi ke Voice Relay gagal. Pastikan relay sedang berjalan.");
        this.stop();
      };

      this.ws.onclose = () => {
        if (this.state === "active") {
          this.setState("idle");
        }
      };
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      this.events.onError(`Gagal mulai voice call: ${msg}`);
      this.setState("error");
      this.cleanup();
    }
  }

  /** Stop the voice call session. */
  stop(): void {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify({ type: "session.end" }));
    }
    this.cleanup();
    this.setState("idle");
  }

  /** Mute or unmute the microphone. */
  setMuted(muted: boolean): void {
    if (!this.micStream) return;
    this.micStream.getAudioTracks().forEach(track => {
      track.enabled = !muted;
    });
  }

  // ── Mic capture ─────────────────────────────────────────────────────────

  private onMicAudio(e: AudioProcessingEvent): void {
    if (this.state !== "active" || !this.ws || this.ws.readyState !== WebSocket.OPEN) return;

    const inputData = e.inputBuffer.getChannelData(0);

    // Convert Float32 [-1,1] → Int16 PCM
    const pcm = new Int16Array(inputData.length);
    let isSilent = true;
    for (let i = 0; i < inputData.length; i++) {
      const s = Math.max(-1, Math.min(1, inputData[i]));
      if (Math.abs(s) > 0.01) isSilent = false;
      pcm[i] = s < 0 ? s * 0x8000 : s * 0x7fff;
    }

    if (isSilent && Math.random() < 0.05) {
      console.warn("[VoiceCall] MIC IS SILENT! WebView2 might not have permission or mic is muted in hardware.");
    }

    // Send as binary WebSocket frame
    this.ws.send(pcm.buffer);
  }

  // ── Relay message handling ──────────────────────────────────────────────

  private onRelayMessage(e: MessageEvent): void {
    if (e.data instanceof ArrayBuffer) {
      // Binary frame = audio from Gemini (24kHz PCM Int16)
      this.enqueueAudio(e.data);
      return;
    }

    // Text frame = JSON message
    try {
      const msg = JSON.parse(e.data as string);
      switch (msg.type) {
        case "session.ready":
          this.setState("active");
          break;

        case "transcript":
          this.events.onTranscript(msg.role, msg.text);
          break;

        case "tool.executing":
          this.events.onToolExecuting(msg.name, msg.args ?? {});
          break;

        case "tool.result":
          this.events.onToolResult(msg.name, msg.success, msg.message);
          break;

        case "turn.complete":
          this.events.onTurnComplete();
          break;

        case "interrupted":
          // Clear playback queue and stop all currently scheduled audio
          this.playbackQueue = [];
          this.activeSources.forEach(s => {
            try { s.stop(); } catch {}
          });
          this.activeSources = [];
          if (this.audioCtx) {
            this.nextPlayTime = this.audioCtx.currentTime + 0.05;
          }
          this.events.onInterrupted();
          break;

        case "session.ended":
          this.cleanup();
          this.setState("idle");
          break;

        case "error":
          this.events.onError(msg.message);
          break;
      }
    } catch {
      console.warn("[VoiceCall] Failed to parse relay message");
    }
  }

  // ── Audio playback ──────────────────────────────────────────────────────

  /** Enqueue PCM audio (Int16, 24kHz) for smooth playback. */
  private enqueueAudio(data: ArrayBuffer): void {
    const int16 = new Int16Array(data);
    const float32 = new Float32Array(int16.length);
    for (let i = 0; i < int16.length; i++) {
      float32[i] = int16[i] / 32768;
    }
    this.playbackQueue.push(float32);
    this.drainPlaybackQueue();
  }

  private drainPlaybackQueue(): void {
    if (!this.audioCtx || this.playbackQueue.length === 0) return;

    // Create a separate context for playback at 24kHz if needed,
    // or resample to the mic context's sample rate.
    const ctx = this.audioCtx;
    const currentTime = ctx.currentTime;

    if (this.nextPlayTime < currentTime) {
      this.nextPlayTime = currentTime + 0.05; // 50ms buffer head start
    }

    while (this.playbackQueue.length > 0) {
      const samples = this.playbackQueue.shift()!;

      // We need to resample from 24kHz to the AudioContext's sample rate (16kHz)
      // Simple linear interpolation resampling
      const ratio = PLAYBACK_SAMPLE_RATE / ctx.sampleRate;
      const outputLen = Math.floor(samples.length / ratio);
      
      if (outputLen <= 0) continue; // Prevent crash on empty chunks

      const buffer = ctx.createBuffer(1, outputLen, ctx.sampleRate);
      const output = buffer.getChannelData(0);

      for (let i = 0; i < outputLen; i++) {
        const srcIdx = i * ratio;
        const srcIdxFloor = Math.floor(srcIdx);
        const frac = srcIdx - srcIdxFloor;
        const s0 = samples[srcIdxFloor] ?? 0;
        const s1 = samples[Math.min(srcIdxFloor + 1, samples.length - 1)] ?? 0;
        output[i] = s0 + frac * (s1 - s0);
      }

      const source = ctx.createBufferSource();
      source.buffer = buffer;
      source.connect(ctx.destination);
      source.start(this.nextPlayTime);
      
      this.activeSources.push(source);
      source.onended = () => {
        this.activeSources = this.activeSources.filter(s => s !== source);
      };

      this.nextPlayTime += buffer.duration;
    }
  }

  // ── Cleanup ─────────────────────────────────────────────────────────────

  private cleanup(): void {
    this.scriptNode?.disconnect();
    this.sourceNode?.disconnect();
    this.scriptNode = null;
    this.sourceNode = null;

    this.micStream?.getTracks().forEach((t) => t.stop());
    this.micStream = null;

    if (this.audioCtx?.state !== "closed") {
      void this.audioCtx?.close();
    }
    this.audioCtx = null;

    if (this.ws) {
      this.ws.onmessage = null;
      this.ws.onerror = null;
      this.ws.onclose = null;
      if (this.ws.readyState === WebSocket.OPEN) {
        this.ws.close();
      }
      this.ws = null;
    }

    this.activeSources.forEach(s => {
      try { s.stop(); } catch {}
    });
    this.activeSources = [];
    this.playbackQueue = [];
    this.nextPlayTime = 0;
  }
}
