#![allow(dead_code)]
// Linux: XDG paths, xdotool-free cursor, no click-through poll.
//
// The island runs under Wayland (most modern desktops) or X11 via XWayland.
// Wayland does not expose the global cursor position to non-compositors, so
// the frontend tracks the cursor with page events (Island.followPageCursor)
// and click-through is set via an input region instead of the poll loop.

use std::path::PathBuf;
use std::process::Command;

use tauri::WebviewWindow;

use super::LocalTime;

/// File name of the Claude Code relay.
pub const HOOK_EXE: &str = "bubu-hook";

/// Environment variable holding the home directory.
pub const HOME_VAR: &str = "HOME";

// ── Files ─────────────────────────────────────────────────────────────────────

/// ~/.config/bubu — preferences (XDG_CONFIG_HOME).
pub fn config_dir() -> PathBuf {
    if let Some(xdg) = std::env::var_os("XDG_CONFIG_HOME") {
        return PathBuf::from(xdg).join("bubu");
    }
    super::home_dir().join(".config").join("bubu")
}

/// ~/.local/share/bubu — log, inbox, hook binary (XDG_DATA_HOME).
pub fn local_dir() -> PathBuf {
    if let Some(xdg) = std::env::var_os("XDG_DATA_HOME") {
        return PathBuf::from(xdg).join("bubu");
    }
    super::home_dir().join(".local").join("share").join("bubu")
}

/// On Linux the data dir is already user-private (0700 on the XDG dirs), but
/// we ensure the directory exists and restrict permissions to 0700 explicitly.
pub fn ensure_private_dir(dir: &std::path::Path) -> std::io::Result<()> {
    use std::os::unix::fs::DirBuilderExt;
    std::fs::DirBuilder::new().recursive(true).mode(0o700).create(dir)
}

/// Sets $WEBKIT_DISABLE_COMPOSITING_MODE=1 so the transparent WebKitGTK window
/// actually composites against the desktop rather than showing black. Must be
/// called before any webview is created.
pub fn prepare_environment() {
    std::env::set_var("WEBKIT_DISABLE_COMPOSITING_MODE", "1");
}

pub fn local_time() -> LocalTime {
    use std::time::{SystemTime, UNIX_EPOCH};
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    // Cheap UTC decomposition — no chrono dependency.
    let days = secs / 86400;
    let time = secs % 86400;
    let hour = (time / 3600) as u32;
    let minute = ((time % 3600) / 60) as u32;
    let second = (time % 60) as u32;

    // Day → date (adapted from Howard Hinnant's civil_from_days).
    let z = days as i64 + 719468;
    let era = if z >= 0 { z } else { z - 146096 } / 146097;
    let doe = (z - era * 146097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
    let y = yoe as i64 + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };

    LocalTime {
        year: y as u32,
        month: m as u32,
        day: d as u32,
        hour,
        minute,
        second,
    }
}

// ── Processes ─────────────────────────────────────────────────────────────────

/// No console hiding needed on Linux (no equivalent of CREATE_NO_WINDOW).
pub fn no_console(cmd: &mut Command) -> &mut Command {
    cmd
}

pub fn open_url(url: &str) {
    let _ = Command::new("xdg-open").arg(url).spawn();
}

pub fn reveal_folder(path: &str) {
    let _ = Command::new("xdg-open").arg(path).spawn();
}

/// Walks $PATH for `<stem>` (no extension on Linux).
pub fn find_on_path(stem: &str) -> Option<PathBuf> {
    let dirs = std::env::var_os("PATH")?;
    for dir in std::env::split_paths(&dirs) {
        let candidate = dir.join(stem);
        if candidate.is_file() {
            return Some(candidate);
        }
    }
    None
}

// ── Who we are ────────────────────────────────────────────────────────────────

/// The UID as a string, for the pipe/socket name. On Linux every user already
/// has a private $XDG_RUNTIME_DIR, but matching the naming lets the relay share
/// the same `pipe_path()` logic.
pub fn current_user_sid() -> Option<String> {
    Some(format!("{}", unsafe { libc::getuid() }))
}

// ── Cursor ────────────────────────────────────────────────────────────────────

/// Wayland does not expose the global cursor to non-compositors. The island
/// tracks it from page events instead (Island.followPageCursor). Setting this
/// to false tells the frontend not to expect Rust cursor events and to install
/// the page-level handler instead.
pub const CURSOR_POLL: bool = false;

/// Not available on Wayland. Returns None so the poll loop idles.
pub fn cursor_physical() -> Option<(f64, f64)> {
    None
}

/// No Win32-style button query on Linux. The page DOM handles it.
pub fn left_button_down() -> bool {
    false
}

// ── Island window ─────────────────────────────────────────────────────────────

/// No Chrome_RenderWidgetHostHWND on Linux — WebKitGTK handles drops natively.
pub fn unblock_webview_drops(_app: &tauri::AppHandle) {}

/// WebKitGTK does not need WS_EX_NOACTIVATE; the Tauri window is created with
/// the right hints already. This is a no-op.
pub fn make_non_activating(_win: &WebviewWindow) {}

/// Focus / unfocus is handled by Tauri's set_focus / the compositor itself.
pub fn set_activating(_win: &WebviewWindow, _activating: bool) {}

/// On Linux without a cursor poll, click-through is set via an input region.
/// `rect`: Some((x, y, w, h)) to accept input in that area, None to accept
/// everywhere (used when the window is the tiny wake strip).
pub fn set_input_region(_win: &WebviewWindow, _rect: Option<(f64, f64, f64, f64)>) {
    // TODO: implement via gtk4::Window::set_input_region or wl_surface.set_input_region
    // when Tauri's Linux backend exposes the necessary handles.
    // For now, the window accepts input everywhere, which is correct for the
    // wake strip and close enough for the panel (a few extra pixels at the
    // corners take clicks, which is better than missing them).
}
