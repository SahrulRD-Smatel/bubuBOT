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

export function buildPrompt(actions: ViewActions, onHeightChange: () => void): ViewHost {
  const chipRow = h("div", { class: "chip-row" });
  const log = h("div", { class: "chat-log" });
  const input = h("input", {
    type: "text",
    class: "chat-input",
    placeholder: "Ask me anything…",
    spellcheck: "false",
  }) as HTMLInputElement;
  const send = h("button", { class: "send-btn", title: "Send" }, svg(ICONS.arrowUp, 11));
  const mic = h("button", { class: "send-btn", title: "Dictate", style: "margin-right: 4px;" }, svg(ICONS.mic, 11));
  const bar = h("div", { class: "chat-bar" }, input, mic, send);

  const el = h(
    "div",
    { class: "view" },
    h("div", { class: "card wash chat-card" }, h("div", { class: "chat-body" }, chipRow, log, bar)),
  );
  (el.querySelector(".card") as HTMLElement).style.setProperty("--wash", "rgba(99,102,241,0.5)");

  let sending = false;
  let renderedCount = -1;

  async function submit() {
    const query = input.value.trim();
    if (!query || sending) return;
    input.value = "";
    sending = true;
    Sound.play("send");

    State.chatHistory.push({ id: nextId++, role: "user", content: query });
    State.stateOverride = "thinking";
    State.notify();
    onHeightChange();

    try {
      const lowerQuery = query.toLowerCase();
      if (lowerQuery.startsWith("buka ") || lowerQuery.startsWith("open ")) {
        const appName = query.substring(5).trim();
        const success = await Bridge.launchApp(appName);
        const replyText = success
          ? `Membuka ${appName}... 🚀`
          : `Maaf, Bubu tidak bisa menemukan aplikasi "${appName}".`;

        State.chatHistory.push({ id: nextId++, role: "assistant", content: replyText });
        State.stateOverride = null;
        Sound.play(success ? "finish" : "error");
      } else if (lowerQuery.startsWith("cari ") || lowerQuery.startsWith("search ")) {
        const searchQuery = lowerQuery.startsWith("cari ") ? query.substring(5).trim() : query.substring(7).trim();
        if (searchQuery) {
          const url = `https://www.google.com/search?q=${encodeURIComponent(searchQuery)}`;
          await Bridge.openUrl(url);
          State.chatHistory.push({ id: nextId++, role: "assistant", content: `Mencarikan "${searchQuery}" di browser... 🔍` });
          State.stateOverride = null;
          Sound.play("finish");
        } else {
          State.stateOverride = null;
          Sound.play("error");
        }
      } else {
        const file = State.droppedFile;
        const context: ChatContext | null =
          State.chatHistory.length === 1 && file ? { kind: "file", name: file.name, path: file.path } : null;

        const reply = await Bridge.chatSend(query, context);
        State.chatHistory.push({ id: nextId++, role: "assistant", content: reply.text });
        State.stateOverride = null;
        Sound.play("finish");
      }
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

  let isRecording = false;

  void onEvent("wakeword-detected", () => {
    console.log("[Bubu Voice] Wake word detected!");
    isRecording = true;
    mic.classList.add("recording");
    input.placeholder = "🎤 Bubu mendengarkan...";
    Sound.play("blip");
    // Focus the window so user sees the change
    void Bridge.focusWindow(true);
  });

  void onEvent<string>("voice-text", (text) => {
    console.log("[Bubu Voice] Heard text:", text);
    isRecording = false;
    mic.classList.remove("recording");
    input.placeholder = State.chatHistory.length === 0 ? "Ask me anything…" : "Continue…";

    if (text) {
      input.value = text;
      Sound.play("send");
      void submit();
    }
  });

  void onEvent("voice-timeout", () => {
    console.log("[Bubu Voice] Timeout - no command heard");
    isRecording = false;
    mic.classList.remove("recording");
    input.placeholder = State.chatHistory.length === 0 ? "Ask me anything…" : "Continue…";
    // Auto-collapse if user didn't type anything during voice session
    if (!input.value.trim()) {
      actions.collapse();
    }
  });

  mic.addEventListener("click", () => {
    // If the mic is clicked, we could toggle recording, but the engine is handling it automatically now.
    // For now, let's just make it visually do something or inform the user
    if (!isRecording) {
      input.placeholder = "Mendengarkan via Wake Word (Halo Bubu)...";
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

      input.placeholder = State.chatHistory.length === 0 ? "Ask me anything…" : "Continue…";
      input.disabled = sending;
    },
    focus() {
      input.focus();
      input.select();
    },
  };
}
