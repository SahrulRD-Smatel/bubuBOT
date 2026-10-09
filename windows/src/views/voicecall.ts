// Voice Call view — full-screen voice call overlay with waveform animation,
// transcript display, and call controls.

import { h, svg } from "./dom";
import { ICONS } from "./icons";
import { Bridge } from "../core/bridge";
import { Sound } from "../core/sound";
import { State } from "../core/state";
import { VoiceCallClient, type VoiceCallState } from "../core/voicecall";
import type { ViewHost, ViewActions } from "./views";

let voiceClient: VoiceCallClient | null = null;
let callState: VoiceCallState = "idle";
let isMuted = false;

export function buildVoiceCall(actions: ViewActions, onHeightChange: () => void): ViewHost {
  // ── DOM structure ──────────────────────────────────────────────────────
  const statusText = h("div", { class: "vc-status", text: "Menghubungkan..." });
  const transcriptEl = h("div", { class: "vc-transcript" });
  const waveformEl = h("div", { class: "vc-waveform" });

  // Create waveform bars
  for (let i = 0; i < 24; i++) {
    waveformEl.append(h("i", { class: "vc-bar" }));
  }

  const muteBtn = h(
    "button",
    { class: "vc-btn vc-mute", title: "Mute" },
    svg(ICONS.mic, 16),
  );
  const endBtn = h(
    "button",
    { class: "vc-btn vc-end", title: "End Call" },
    h("span", { text: "✕" }),
  );

  const controls = h("div", { class: "vc-controls" }, muteBtn, endBtn);

  const card = h(
    "div",
    { class: "card wash vc-card" },
    h("div", { class: "vc-top-section" }, statusText),
    waveformEl,
    transcriptEl,
    controls,
  );
  card.style.setProperty("--wash", "rgba(139,92,246,0.3)"); // Subtle modern wash

  const el = h("div", { class: "view" }, card);

  // ── Voice client setup ─────────────────────────────────────────────────

  function ensureClient() {
    if (voiceClient) return voiceClient;

    voiceClient = new VoiceCallClient({
      onStateChange(state) {
        callState = state;
        updateUI();
        onHeightChange();

        if (state === "active") {
          Sound.play("open");
          statusText.textContent = "Bubu mendengarkan... 💜";
          waveformEl.classList.add("active");
        } else if (state === "idle") {
          waveformEl.classList.remove("active");
          transcriptEl.textContent = "";
          statusText.textContent = "";
          Sound.play("close");
          actions.collapse();
        }
      },

      onTranscript(role, text) {
        if (role === "assistant") {
          transcriptEl.textContent = text;
          statusText.textContent = "Bubu bicara... 🎀";
          waveformEl.classList.add("speaking");
        } else {
          statusText.textContent = "Bubu mendengarkan... 💜";
          waveformEl.classList.remove("speaking");
        }
      },

      onToolExecuting(name, _args) {
        statusText.textContent = `Menjalankan: ${name}...`;
      },

      onToolResult(_name, success, message) {
        statusText.textContent = success ? "Selesai! 🚀" : `Error: ${message}`;
        setTimeout(() => {
          if (callState === "active") {
            statusText.textContent = "Bubu mendengarkan... 💜";
          }
        }, 2000);
      },

      onTurnComplete() {
        waveformEl.classList.remove("speaking");
        statusText.textContent = "Bubu mendengarkan... 💜";
      },

      onInterrupted() {
        waveformEl.classList.remove("speaking");
        statusText.textContent = "Bubu mendengarkan... 💜";
      },

      onError(message) {
        statusText.textContent = `⚠️ ${message}`;
        Sound.play("error");
      },
    });

    return voiceClient;
  }

  // ── Event handlers ─────────────────────────────────────────────────────

  async function startCall() {
    const client = ensureClient();
    // Get the Gemini API key from Credential Manager via Rust bridge
    const hasKey = await Bridge.secretPresent("gemini-api-key");
    if (!hasKey) {
      statusText.textContent = "API key Gemini belum di-set. Buka Settings.";
      Sound.play("error");
      return;
    }
    // We can't read the key from JS (by design), so we need to pass it through
    // For now, use a placeholder — the actual key will be fetched via a new Rust command
    const key = await Bridge.getGeminiApiKey();
    if (!key) {
      statusText.textContent = "API key Gemini tidak ditemukan.";
      Sound.play("error");
      return;
    }
    await client.start(key);
  }

  function endCall() {
    voiceClient?.stop();
    callState = "idle";
    isMuted = false;
    transcriptEl.textContent = "";
    statusText.textContent = "";
    Sound.play("close");
    actions.collapse();
  }

  endBtn.addEventListener("click", endCall);

  muteBtn.addEventListener("click", () => {
    isMuted = !isMuted;
    muteBtn.classList.toggle("muted", isMuted);
    muteBtn.title = isMuted ? "Unmute" : "Mute";
    voiceClient?.setMuted(isMuted);
  });

  function updateUI() {
    card.classList.toggle("vc-connecting", callState === "connecting");
    card.classList.toggle("vc-active", callState === "active");
    card.classList.toggle("vc-error", callState === "error");
  }

  return {
    el,
    sync() {
      if (State.view === "voicecall") {
        // Auto-start the call when the view is shown
        if (callState === "idle") {
          void startCall();
        }
      } else {
        // Automatically stop the call if the user navigates away to another tab
        if (callState !== "idle") {
          voiceClient?.stop();
          callState = "idle";
          isMuted = false;
          transcriptEl.textContent = "";
          statusText.textContent = "";
          updateUI();
        }
      }
    },
    focus() {
      // Nothing to focus in voice call view
    },
  };
}
