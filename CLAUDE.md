# Bubu — guide for AI coding agents

Bubu is a native macOS app: Mochi, a small animated character living in the MacBook notch, shows AI coding agent sessions (Claude Code, Gemini CLI, Antigravity and more) and a few integrations, and lets the user approve, answer, chat and drop files from the notch.

## Where things are
- `NotchBuddy/Sources/App/` — Mac-only Swift code. `NotchBuddy/Sources/BubuKit/` — code shared with the iPhone app (BotEngine, outfits, pills, diff, models). `NotchBuddy/Sources/Phone/` — iPhone app. `NotchBuddy/Resources/sounds/` — the 28 WAV sounds. `NotchBuddy/project.yml` — XcodeGen project (never edit the `.xcodeproj` by hand).
- `docs/SPEC.md`, `docs/INTEGRATIONS.md` — behaviour, views, states, integrations (in French).
- `design/prototype/notch-buddy.html` — original prototype, the visual source of truth. `design/captures/` — target screenshots.
- `docs/*.html` — the GitHub Pages site (privacy, terms, support, legal notice).
- `.agents/skills/bubu-dev/SKILL.md` — complete development guide (architecture, patterns, templates).
*(Note for local agents: if a local `AGENTS.md` file exists in the root, read it for internal workflow rules).*

## Build
```
cd NotchBuddy && xcodegen && xcodebuild -scheme NotchBuddy -configuration Debug build
```
To run it day to day on the Mac (signed, iPhone link, permissions kept across builds): `./scripts/dev-install.sh` — it installs the only Bubu in /Applications and removes the copies earlier builds left.
Windows and Linux: `cd windows && npm install && npm run tauri dev`

## Rules
- Swift 6, SwiftUI + AppKit. No third-party dependencies unless truly unavoidable. The character is drawn in code (`Canvas` + `TimelineView`), no Rive/Lottie/images.
- Secrets live in the Keychain, never on disk or in git.
- No telemetry. Network calls only to services the user configured.
- Never block Claude Code: if the app doesn't answer, the hook exits immediately.
- Never overwrite `~/.claude/settings.json`: dated backup, merge, show the diff, write only after the user confirms.
- Never send an email or approve a Claude Code or Codex permission without an explicit click.
- Performance: 0 % CPU when the island is hidden.
- Keep the bundle identifier `fr.sahrul.NotchBuddy` (Keychain items, preferences and permissions depend on it).
- Never restyle what already ships: existing views stay as they are. Change only when explicitly asked.
- Pill IDs are stable contract values (Keychain, UserDefaults, hook routing): never rename an existing pill ID.
- New views follow the existing app style. `design/prototype/notch-buddy.html` and `design/captures/` are references for new work, not a reason to change existing views.
- Every release adds its CHANGELOG.md section, a row in the README Versions table, and commits the regenerated Info.plist with the new version.
- Every release also adds its What's new, shown once in the notch or the island after Bubu updates itself: an entry for the new version in `NotchBuddy/Resources/whats-new.json` (Mac, the 14 languages) or `windows/src/whats-new/whats-new.json` (Windows and Linux, the 12 languages). 1 to 4 items picked from the CHANGELOG section, each with an icon from `scripts/test-whats-new.py`, a short title and one line, translated in every language. `python3 scripts/test-whats-new.py` checks it; CI and `release.sh` fail without it.
