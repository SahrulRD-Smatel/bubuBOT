// Chat view — DOM port of PromptView / ChatBubble / TypingDotsView from
// IslandViewContent.swift.

import { h, svg, clear } from "./dom";
import { ICONS } from "./icons";
import { Bridge, type ChatContext, onEvent } from "../core/bridge";
import { Sound } from "../core/sound";
import { State, type ChatMessage } from "../core/state";
import type { ViewHost, ViewActions } from "./views";

let nextId = 1;

function bubble(message: ChatMessage): HTMLElement {
  if (message.role === "user") {
    return h(
      "div",
      { class: "chat-row user" },
      h("div", { class: "bubble", text: message.content }),
    );
  }
  return h("div", { class: "chat-row" }, h("div", { class: "reply", text: message.content }));
}

function typingDots(): HTMLElement {
  return h(
    "div",
    { class: "chat-row" },
    h("div", { class: "typing" }, h("i"), h("i"), h("i")),
  );
}

/** The coloured chip showing what the question is about (a dropped file). */
function contextChip(label: string): HTMLElement {
  const chip = h("div", { class: "chip" }, h("i", { class: "chip-dot" }), h("span", { text: label }));
  requestAnimationFrame(() => chip.classList.add("settled"));
  return chip;
}

export function buildPrompt(_actions: ViewActions, onHeightChange: () => void): ViewHost {
  const chipRow = h("div", { class: "chip-row" });
  const log = h("div", { class: "chat-log" });
  const input = h("input", {
    type: "text",
    class: "chat-input",
    placeholder: "Ask me anything…",
    spellcheck: "false",
  }) as HTMLInputElement;
  const micBtn = h("button", { class: "icon-btn", title: "Dictate", style: "background:transparent;border:none;cursor:pointer;opacity:0.7;padding:0 8px;" }, svg(ICONS.mic, 14));
  const send = h("button", { class: "send-btn", title: "Send" }, svg(ICONS.arrowUp, 11));
  const bar = h("div", { class: "chat-bar" }, micBtn, input, send);

  const el = h(
    "div",
    { class: "view" },
    h("div", { class: "card wash chat-card" }, h("div", { class: "chat-body" }, chipRow, log, bar)),
  );
  (el.querySelector(".card") as HTMLElement).style.setProperty("--wash", "rgba(99,102,241,0.5)");

  let sending = false;
  let renderedCount = -1;

  let recognition: any = null;
  let originalPlaceholder = input.placeholder;

  let isListeningWithWebSpeech = false;

  if ("webkitSpeechRecognition" in window) {
    const SpeechRecognition = (window as any).webkitSpeechRecognition;
    recognition = new SpeechRecognition();
    recognition.lang = "id-ID";
    recognition.interimResults = true;
    recognition.continuous = false;

    recognition.onstart = () => {
      isListeningWithWebSpeech = true;
    };

    recognition.onresult = (e: any) => {
      let finalTranscript = "";
      for (let i = e.resultIndex; i < e.results.length; ++i) {
        if (e.results[i].isFinal) {
          finalTranscript += e.results[i][0].transcript;
        }
      }
      if (finalTranscript) {
        const current = input.value.trim();
        input.value = current ? current + " " + finalTranscript : finalTranscript;
      }
    };

    recognition.onerror = () => {
      isListeningWithWebSpeech = false;
      input.placeholder = originalPlaceholder;
      micBtn.style.color = "";
      micBtn.style.opacity = "0.7";
    };
    recognition.onend = () => {
      isListeningWithWebSpeech = false;
      input.placeholder = originalPlaceholder;
      micBtn.style.color = "";
      micBtn.style.opacity = "0.7";
      // Auto-submit if there is text!
      if (input.value.trim() && !sending) {
        void submit();
      }
    };

    micBtn.addEventListener("click", () => {
      if (micBtn.style.color === "red") {
        recognition.stop();
      } else {
        originalPlaceholder = input.placeholder;
        input.placeholder = "🎤 Sedang mendengarkan...";
        micBtn.style.color = "red";
        micBtn.style.opacity = "1";
        try { recognition.start(); } catch(e) {}
      }
    });
  }

  // Allow wake-word to trigger the high-quality Web Speech API
  void onEvent("wakeword-detected", () => {
    if (State.view === "voicecall") return;
    if (recognition && micBtn.style.color !== "red") {
      // Start directly instead of synthetic click to bypass gesture requirements
      originalPlaceholder = input.placeholder;
      input.placeholder = "🎤 Mendengarkan...";
      micBtn.style.color = "red";
      micBtn.style.opacity = "1";
      try { 
        recognition.start(); 
        // If it throws here synchronously, it means gesture requirement blocked it
      } catch(e) {
        console.error("Web Speech API blocked by browser", e);
        input.placeholder = "Pencet tombol Mic 🎤 biar aku dengar jelas!";
        micBtn.style.color = "";
        micBtn.style.opacity = "0.7";
        Sound.play("blip"); // play a sound to notify user
      }
    }
  });

  async function submit() {
    const query = input.value.trim();
    if (!query || sending) return;

    // Smart intent routing:
    // If user wants to talk/chat/call, switch to the full Gemini Live Voice Call UI
    const lowerQuery = query.toLowerCase();
    if (lowerQuery.includes("ngobrol") || lowerQuery.includes("telepon") || lowerQuery.includes("bicara") || lowerQuery.includes("curhat") || lowerQuery.includes("call")) {
      input.value = "";
      Sound.play("blip");
      State.view = "voicecall";
      State.notify();
      return;
    }

    input.value = "";
    input.placeholder = ""; // Force render() to re-evaluate it
    sending = true;
    Sound.play("send");

    State.chatHistory.push({ id: nextId++, role: "user", content: query });
    State.stateOverride = "thinking";
    State.notify();
    onHeightChange();

    try {
      const file = State.droppedFile;
      const context: ChatContext | null =
        State.chatHistory.length === 1 && file ? { kind: "file", name: file.name, path: file.path } : null;

      const reply = await Bridge.chatSend(query, context);
      let replyText = reply.text;

      // Extract and execute commands from the AI's response
      const openAppMatch = replyText.match(/\[COMMAND:\s*OPEN_APP,\s*([^\]]+)\]/i);
      const searchWebMatch = replyText.match(/\[COMMAND:\s*SEARCH_WEB,\s*([^\]]+)\]/i);

      if (openAppMatch) {
        const appName = openAppMatch[1].trim();
        const success = await Bridge.launchApp(appName);
        replyText = replyText.replace(openAppMatch[0], "").trim();
        if (!success) replyText += `\n\n(Maaf, Bubu gagal membuka aplikasi ${appName} di komputermu)`;
        Sound.play(success ? "finish" : "error");
      } else if (searchWebMatch) {
        const searchQuery = searchWebMatch[1].trim();
        const url = `https://www.google.com/search?q=${encodeURIComponent(searchQuery)}`;
        await Bridge.openUrl(url);
        replyText = replyText.replace(searchWebMatch[0], "").trim();
        Sound.play("finish");
      } else {
        Sound.play("finish");
      }

      State.chatHistory.push({ id: nextId++, role: "assistant", content: replyText });
      State.stateOverride = null;
    } catch (err) {
      State.stateOverride = null;
      State.noteMessage = String(err).replace(/^Error:\s*/, "");
      State.view = "note";
      Sound.play("error");
    } finally {
      sending = false;
      State.notify();
      onHeightChange();
      input.focus();
    }
  }

  send.addEventListener("click", () => void submit());
  input.addEventListener("keydown", (e) => {
    if ((e as KeyboardEvent).key === "Enter") {
      e.preventDefault();
      void submit();
    }
    e.stopPropagation(); // Escape closes the island, not the chat
  });


  void onEvent<string>("voice-text", (text) => {
    if (State.view === "voicecall") return;

    let currentModelName = "Gemini";
    if (State.settings.chatProvider === "openai") {
      currentModelName = "ChatGPT";
    } else if (State.settings.chatProvider === "claude") {
      currentModelName = (State.settings.model || "Claude").split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
    } else {
      currentModelName = (State.settings.geminiChatModel || "Gemini").replace("models/", "").split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
    }

    input.placeholder = State.chatHistory.length === 0 ? `Tanya Bubu (${currentModelName})...` : `Lanjut ngobrol (${currentModelName})...`;

    if (text) {
      const lowerText = text.toLowerCase();
      if (lowerText.includes("ngobrol") || lowerText.includes("telepon") || lowerText.includes("bicara") || lowerText.includes("curhat") || lowerText.includes("call")) {
        Sound.play("blip");
        State.view = "voicecall";
        State.notify();
      } else {
        // We use the local voice engine text if Web Speech API didn't actively listen.
        // We rely on the AI's phonetics understanding to decipher "fans will" etc.
        if (!isListeningWithWebSpeech) {
          input.value = text;
          Sound.play("send");
          void submit();
        }
      }
    }
  });

  void onEvent("voice-timeout", () => {
    if (State.view === "voicecall") return;

    input.placeholder = State.chatHistory.length === 0 ? "Ask me anything…" : "Continue…";
    if (!input.value.trim()) {
      _actions.collapse();
    }
  });

  return {
    el,
    sync() {
      const file = State.droppedFile;
      const wantChip = file?.name ?? "";
      if (chipRow.dataset.label !== wantChip) {
        chipRow.dataset.label = wantChip;
        clear(chipRow);
        if (wantChip) chipRow.append(contextChip(wantChip));
      }

      const thinking = State.stateOverride === "thinking";
      const count = State.chatHistory.length + (thinking ? 0.5 : 0);
      if (count !== renderedCount) {
        renderedCount = count;
        clear(log);
        for (const m of State.chatHistory) log.append(bubble(m));
        if (thinking) log.append(typingDots());
        log.scrollTop = log.scrollHeight;
      }

      let currentModelName = "Gemini";
      if (State.settings.chatProvider === "openai") {
        currentModelName = "ChatGPT";
      } else if (State.settings.chatProvider === "claude") {
        currentModelName = (State.settings.model || "Claude").split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
      } else {
        currentModelName = (State.settings.geminiChatModel || "Gemini").replace("models/", "").split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
      }

      // Don't overwrite the listening indicator!
      if (!input.placeholder.includes("mendengarkan")) {
        input.placeholder = State.chatHistory.length === 0 ? `Tanya Bubu (${currentModelName})...` : `Lanjut ngobrol (${currentModelName})...`;
      }
      input.disabled = sending;
    },
    focus() {
      input.focus();
      input.select();
    },
  };
}
