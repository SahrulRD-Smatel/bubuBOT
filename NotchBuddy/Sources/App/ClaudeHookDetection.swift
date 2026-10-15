import Foundation

/// True when a parsed ~/.claude/settings.json routes Claude Code SessionStart events to Bubu.
///
/// Kept free of file access so it can be tested on fixtures instead of the real settings file.
/// Both the installed app and earlier builds matched on the command text: a hook written by
/// Bubu runs ~/.claude/bubu/nb-hook, and the App Store build names NotchBuddy instead.
func bubuHooksPresent(inSettings settings: [String: Any]) -> Bool {
    guard let hooks = settings["hooks"] as? [String: Any],
          let sessionStart = hooks["SessionStart"] as? [[String: Any]] else { return false }
    return sessionStart.contains { group in
        (group["hooks"] as? [[String: Any]])?.contains { hook in
            guard let command = hook["command"] as? String else { return false }
            let lower = command.lowercased()
            return lower.contains("notchbuddy") || lower.contains("bubu") || lower.contains("coucou")
        } ?? false
    }
}
