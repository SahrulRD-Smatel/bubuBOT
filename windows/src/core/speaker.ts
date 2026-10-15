// VoiceSpeaker — Text-to-speech speaker for Bubu using Web Speech API (speechSynthesis).
// Gives Bubu the ability to respond and speak back results aloud on Windows & Linux.

import { State } from "./state";

export class VoiceSpeaker {
  private static synth: SpeechSynthesis | null =
    typeof window !== "undefined" && "speechSynthesis" in window ? window.speechSynthesis : null;
  private static currentUtterance: SpeechSynthesisUtterance | null = null;
  public static get utterance(): SpeechSynthesisUtterance | null {
    return this.currentUtterance;
  }
  public static isSpeaking = false;
  public static onSpeakingChange?: (speaking: boolean) => void;

  /** Speak text aloud using the best matching system voice. */
  public static speak(text: string, lang = "id-ID") {
    if (!this.synth || !State.settings.soundEnabled) return;

    // Clean text for speech synthesis: strip code blocks, markdown symbols, and URLs
    const cleanText = text
      .replace(/```[\s\S]*?```/g, "") // remove large code blocks
      .replace(/`([^`]+)`/g, "$1") // inline code
      .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1") // markdown links
      .replace(/https?:\/\/\S+/g, "") // bare URLs
      .replace(/[*_~#>]/g, "") // markdown formatting
      .replace(/\[COMMAND:[^\]]+\]/gi, "") // internal command tags
      .replace(/\s+/g, " ")
      .trim();

    if (!cleanText) return;

    this.stop();

    try {
      const utterance = new SpeechSynthesisUtterance(cleanText);
      utterance.lang = lang;
      utterance.rate = 1.05;
      utterance.pitch = 1.1; // Friendly lively pitch for Bubu
      utterance.volume = Math.max(0, Math.min(1, State.settings.soundVolume));

      const voices = this.synth.getVoices();
      const prefix = lang.slice(0, 2).toLowerCase();
      // Prefer natural/online voices if available in Windows
      const matchedVoice =
        voices.find((v) => v.lang.toLowerCase().startsWith(prefix) && (v.name.includes("Natural") || v.name.includes("Online"))) ||
        voices.find((v) => v.lang.toLowerCase().startsWith(prefix)) ||
        voices.find((v) => v.lang.toLowerCase().startsWith("en")) ||
        voices[0];

      if (matchedVoice) {
        utterance.voice = matchedVoice;
      }

      utterance.onstart = () => {
        this.isSpeaking = true;
        this.onSpeakingChange?.(true);
      };

      utterance.onend = () => {
        this.isSpeaking = false;
        this.currentUtterance = null;
        this.onSpeakingChange?.(false);
      };

      utterance.onerror = () => {
        this.isSpeaking = false;
        this.currentUtterance = null;
        this.onSpeakingChange?.(false);
      };

      this.currentUtterance = utterance;
      this.synth.speak(utterance);
    } catch (e) {
      console.warn("[VoiceSpeaker] Speech synthesis error:", e);
      this.isSpeaking = false;
    }
  }

  /** Immediately stops any current speech. */
  public static stop() {
    if (this.synth) {
      try {
        this.synth.cancel();
      } catch {
        // Ignore cancel errors
      }
    }
    this.isSpeaking = false;
    this.currentUtterance = null;
    this.onSpeakingChange?.(false);
  }
}
