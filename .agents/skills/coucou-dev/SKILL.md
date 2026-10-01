---
name: coucou-dev
description: >
  Complete development guide for building, upgrading, and extending the Coucou
  (Mochi notch companion) project. Covers both macOS (Swift 6/SwiftUI) and
  Windows (Tauri 2/Rust/TypeScript) platforms, including architecture,
  conventions, build instructions, and templates for new features.
---

# Coucou Development Skill

## Project Identity

- **Name**: Coucou (character: Mochi)
- **Purpose**: Animated notch companion for Claude Code sessions + integrations
- **macOS**: Native Swift 6 / SwiftUI / AppKit — ZERO third-party dependencies
- **Windows**: Tauri 2 (Rust backend + TypeScript frontend, no UI framework)
- **License**: MIT (code), All Rights Reserved (name, character, sounds, media)

---

## Architecture Overview

### macOS Stack
```
NotchBuddy/
  Sources/App/            <- All Swift code
    NotchBuddyApp.swift   <- @main entry
    AppDelegate.swift     <- Menu bar, window setup
    AppState.swift        <- @MainActor global state (ObservableObject)
    IslandStateMachine.swift <- FSM: hidden -> petit -> home -> coucou
    IslandWindowController.swift <- NSPanel management, mouse tracking
    IslandRootView.swift  <- Root SwiftUI view
    IslandViewContent.swift <- ALL island views (overview, approval, chat, etc.)
    BotEngine.swift       <- Mochi renderer (Canvas + TimelineView @60fps)
    BotCanvasView.swift   <- Canvas wrapper
    GreetingCanvasView.swift <- Launch greeting animation
    HookServer.swift      <- Unix socket server + nb-hook installer
    ClaudeService.swift   <- Anthropic API chat
    SoundEngine.swift     <- 28 preloaded AVAudioPlayers
    SettingsView.swift    <- Settings window
    *Poller.swift         <- Integration pollers (Stripe, GitHub, Vercel, etc.)
    UploadCanvasView.swift <- File upload animation
    UploadSequenceEngine.swift <- Upload choreography
    WindowContextCapture.swift <- Window drag-attach
    IslandTypes.swift     <- Shared types (BotState, IslandView, etc.)
  Resources/
    sounds/               <- 28 WAV files
    Info.plist
    Coucou.entitlements
  Assets.xcassets/        <- App icon
  project.yml             <- XcodeGen project definition
```

### Windows Stack
```
windows/
  src/                    <- Island frontend (TypeScript, no framework)
    main.ts               <- Entry, Vite setup
    style.css             <- All CSS (~24KB)
    core/
      state.ts            <- Global state
      bridge.ts           <- Tauri IPC wrapper
      layout.ts           <- Window geometry
      anim.ts             <- Spring/ease helpers
      sound.ts            <- Sound manager
    mochi/
      engine.ts           <- Mochi renderer (Canvas 2D)
      greeting.ts         <- Launch greeting
      minibots.ts         <- Mini integration pills
    island/
      fsm.ts              <- State machine (port of Swift FSM)
      island.ts           <- Island orchestration
      hooks.ts            <- Hook event handling
      integrations.ts     <- Integration event handling
    views/
      views.ts            <- All island views
      chat.ts             <- Chat UI
      upload.ts           <- File upload UI
      ticker.ts           <- Scrolling task list
      icons.ts            <- SVG icons
      dom.ts              <- DOM helpers
      integrations.ts     <- Integration cards
    settings/             <- Settings window
    upload/               <- Upload preview (dev only)
  src-tauri/              <- Rust backend
    src/
      main.rs             <- Binary entry
      lib.rs              <- App wiring, all #[tauri::command]s
      pipe.rs             <- Named pipe server
      hooks.rs            <- ~/.claude/settings.json manager
      claude.rs           <- Anthropic API client
      integrations.rs     <- All API pollers
      secrets.rs          <- Credential Manager wrapper
      island.rs           <- Window management, cursor poll
      settings.rs         <- Settings persistence
      tray.rs             <- System tray
      win_user.rs         <- Windows SID helper
      files.rs            <- File ingestion
      log.rs              <- Logging
    Cargo.toml            <- Dependencies
    tauri.conf.json       <- Tauri config
    capabilities/         <- Tauri permissions
  hook/                   <- coucou-hook.exe (Rust relay)
    src/
      main.rs             <- Hook relay logic
      win.rs              <- Pipe security (SID check)
    Cargo.toml
  Cargo.toml              <- Workspace (members: src-tauri, hook)
  package.json            <- npm scripts
  vite.config.ts          <- Vite config
  tsconfig.json
```

---

## Build Instructions

### macOS
```bash
# Prerequisites: macOS 15+, Xcode 16+, XcodeGen
brew install xcodegen
cd NotchBuddy
xcodegen                          # generates .xcodeproj from project.yml
open NotchBuddy.xcodeproj        # then Cmd+R to run

# CLI build
xcodegen && xcodebuild -scheme NotchBuddy -configuration Debug build
```

### Windows
```powershell
# Prerequisites: Rust (rustup.rs), Node 20+, MSVC build tools
cd windows
npm install

# Development (live-reloading)
npm run tauri dev

# Frontend only (no Rust, in browser)
npm run dev

# Production build (installer in windows/release/)
npm run pack

# Regenerate icons
npm run icons
```

> **Important**: `predev` and `prebuild` scripts automatically build `coucou-hook.exe` first.

---

## Inviolable Rules (from CLAUDE.md)

These rules MUST be followed in every change:

1. **Swift 6** with strict concurrency (`-strict-concurrency=complete`)
2. **Zero third-party dependencies** on macOS unless truly unavoidable
3. **Character drawn in code** — Canvas + TimelineView, no Rive/Lottie/images
4. **Secrets in Keychain / Credential Manager** — never on disk or in git
5. **No telemetry** — network calls only to user-configured services
6. **Never block Claude Code** — hook exits immediately if app is down
7. **Never overwrite ~/.claude/settings.json** — backup, preview diff, confirm click
8. **Never approve permission or send email** without explicit user click
9. **0% CPU when island is hidden**
10. **Bundle ID stays fr.louisraille.NotchBuddy** — Keychain items depend on it
11. **Visual changes must match prototype** in `design/captures/`

---

## State Machine (FSM)

Both platforms use the same 4-state FSM:

| State | Description | Transitions |
|---|---|---|
| `hidden` | Invisible behind notch | -> `petit` (mouseEntered/reveal), -> `coucou` (launch) |
| `petit` | Compact, Mochi visible | -> `hidden` (timeout 60s), -> `home` (click) |
| `home` | Expanded, full UI | -> `petit` (timeout 15s / mouseLeft) |
| `coucou` | Greeting animation | -> `petit` (greetComplete / mouseLeft) |

Key behaviors:
- `pinned = true` (during permission request) prevents auto-close
- Alerts force-open the island
- 0 CPU when hidden — cursor poll pauses

---

## Hook System

### 12 Hook Events
```
SessionStart, SessionEnd, UserPromptSubmit, PreToolUse, PostToolUse,
PostToolUseFailure, PermissionRequest, Notification, Stop, StopFailure,
SubagentStart, SubagentStop
```

### macOS: Unix Socket
- Path: `~/Library/Application Support/NotchBuddy/nb.sock`
- Relay: shell wrapper -> Python script -> socket
- Filter: only VS Code sessions (TERM_PROGRAM / __CFBundleIdentifier)

### Windows: Named Pipe
- Path: `\\.\pipe\coucou-{SID}` (Windows Security ID)
- Relay: `coucou-hook.exe` (compiled Rust binary)
- Security: verify server process SID matches client SID
- Accepts ALL terminals (not just VS Code)

### PermissionRequest Flow
1. Claude Code triggers hook with JSON on stdin
2. Relay connects to pipe/socket (300ms timeout)
3. App shows approval card (Allow/Deny buttons)
4. User clicks -> decision sent back through pipe/socket
5. Relay outputs hookSpecificOutput JSON to stdout
6. Claude Code reads the decision
7. If no answer: relay prints nothing -> Claude Code asks in terminal

---

## Adding New Features

### New Integration (e.g., "Linear")

**macOS:**
1. Create `LinearPoller.swift` following pattern from `StripePoller.swift`
2. Add task in `AppState.swift` `integrationAgents` array
3. Add API key to Keychain handling
4. Add UI card in `IslandViewContent.swift`
5. Add toggle in `SettingsView.swift`

**Windows:**
1. Add poller in `integrations.rs` (follow existing pattern)
2. Add key to `KNOWN_KEYS` in `secrets.rs`
3. Register poller in `start()` function
4. Add card renderer in `views/integrations.ts`
5. Add settings UI for key input

**Both:**
- Give the integration a unique color
- Follow polling pattern: initial delay -> fixed interval
- Emit events for badge/sound only on actual changes
- Respect PAUSED flag

### New Mochi Emote/Animation

**macOS:** Add to `BotEngine.swift` — all rendering is in the `draw()` method
**Windows:** Add to `mochi/engine.ts` — match the same shapes, timings, springs

### New Island View

**macOS:** Add case to `IslandView` enum in `IslandTypes.swift`, add content in `IslandViewContent.swift`
**Windows:** Add in `views/views.ts`, wire up in `island/island.ts`

### New Sound

1. Add WAV file to `NotchBuddy/Resources/sounds/`
2. macOS: register in `SoundEngine.swift`
3. Windows: sounds are shared from macOS dir (path in `vite.config.ts` `SOUNDS_DIR`)

---

## Version Upgrade Checklist

When releasing a new version:

1. **Update version numbers** in ALL of these files:
   - `NotchBuddy/project.yml` -> `CFBundleShortVersionString` (BOTH targets) + `CFBundleVersion`
   - `windows/Cargo.toml` -> workspace `version`
   - `windows/package.json` -> `version`
   - `windows/src-tauri/tauri.conf.json` -> `version`

2. **Run tests:**
   - macOS: `xcodegen && xcodebuild test -scheme NotchBuddy`
   - Windows: `cd windows && cargo test` (tests in hooks.rs, claude.rs, hook/main.rs)

3. **Build release:**
   - macOS: Archive in Xcode -> export .app -> zip -> upload to GitHub Releases
   - Windows: `npm run pack` -> installer in `windows/release/`

4. **Update hooks:** If hook events changed, update `HOOK_EVENTS` in both:
   - `NotchBuddy/Sources/App/HookServer.swift` (line ~554)
   - `windows/src-tauri/src/hooks.rs` (line ~23)

5. **Release script:** `scripts/release.sh` automates tagging and release creation

---

## Key Coding Patterns

### macOS Patterns
- `@MainActor` for all UI state
- `@unchecked Sendable` for thread-safe singletons (HookServer)
- `DispatchWorkItem` for cancellable timers
- `Canvas` + `TimelineView` for 60fps animation
- `NSPanel` with `nonactivatingPanel` for overlay
- Polling `NSEvent.mouseLocation` each frame (no permission needed)

### Windows Patterns
- Tauri `#[tauri::command]` for IPC
- `tokio::sync::mpsc` for permission request channels
- `AtomicBool` / `Mutex` for shared state
- `reqwest` with explicit timeout for API calls
- Canvas 2D `requestAnimationFrame` loop
- `CREATE_NO_WINDOW` flag (0x0800_0000) on all spawned processes

### Shared Patterns
- Fire-and-forget for non-permission events
- Strict timeout budgets at every layer
- Fingerprint verification before writing settings
- Atomic file writes (temp -> rename)
- Graceful degradation (app down -> hook exits cleanly)

---

## Dependencies

### macOS: ZERO (all Apple frameworks)

### Windows Rust:
- tauri 2.x (app framework)
- serde / serde_json 1.x (serialization)
- tokio 1.x (async runtime with net, io-util, sync, time, rt)
- reqwest 0.12 (HTTP, rustls-tls, json)
- keyring 3.x (Credential Manager, windows-native)
- windows 0.61 (Win32 API: Foundation, Security, Threading, etc.)
- tauri-plugin-single-instance 2.x
- tauri-plugin-autostart 2.x

### Windows Frontend:
- @tauri-apps/api 2.x (IPC bridge)
- @tauri-apps/cli 2.x (dev)
- typescript 5.6+ (dev)
- vite 6.x (dev)

---

## Documentation & References

| Document | Location | Content |
|---|---|---|
| SPEC.md | `docs/` | Full spec (in French) — views, animations, measurements |
| INTEGRATIONS.md | `docs/` | Integration API details |
| CLAUDE.md | root | Agent guide — where things are, build, rules |
| CONTRIBUTING.md | root | Contribution guidelines |
| Prototype | `design/prototype/notch-buddy.html` | Visual source of truth |
| Captures | `design/captures/` | Target screenshots |

---

## Troubleshooting

| Problem | Cause | Fix |
|---|---|---|
| Hook events not received | nb.sock / pipe doesn't exist | Restart app, check logs |
| Windows Defender flags it | Unsigned Rust binary + named pipe = ML false positive | Build from source, or submit to Microsoft for analysis |
| Mochi not visible | Island window off-screen | Check monitor setup, call `reposition` |
| Settings lost after install | Different bundle ID | Keep `fr.louisraille.NotchBuddy` |
| Hook blocks Claude Code | Bug — should never happen | Check timeout constants, fix and test |
| WebView2 second window blank | Browser args mismatch | Both windows must have same `additionalBrowserArgs` |
| coucou-hook.exe not found | NSIS resource path wrong | Check `ensure_hook_exe()` candidates in hooks.rs |
