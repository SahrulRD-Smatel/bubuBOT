// Process scanner — polls the Windows process list every 3 seconds and emits
// `app-detected` / `app-closed` events when known applications start or stop.
//
// Uses CreateToolhelp32Snapshot (Win32 ToolHelp API) for lightweight, zero-GPU
// enumeration.  The poll is fire-and-forget on a background thread; the only
// interaction with the rest of the app is through Tauri events.

use std::collections::{HashMap, HashSet};
use std::ffi::OsString;
use std::os::windows::ffi::OsStringExt;
use std::time::Duration;

use serde::Serialize;
use tauri::{AppHandle, Emitter};
use windows::Win32::Foundation::CloseHandle;
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
};

use crate::island::WINDOW_LABEL;
use crate::log;

/// Interval between scans.  3 s keeps CPU usage negligible.
const POLL_INTERVAL: Duration = Duration::from_secs(3);

/// A known application the scanner looks for.
struct KnownApp {
    /// The exe name to match (case-insensitive).
    exe: &'static str,
    label: &'static str,
    color: &'static str,
}

/// What the frontend receives.
#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct AppEvent {
    pub id: String,
    pub label: String,
    pub color: String,
}

const KNOWN_APPS: &[KnownApp] = &[
    KnownApp { exe: "Code.exe",            label: "VS Code",    color: "#007ACC" },
    KnownApp { exe: "antigravity.exe",      label: "Antigravity", color: "#8B5CF6" },
    KnownApp { exe: "docker.exe",           label: "Docker",     color: "#2496ED" },
    KnownApp { exe: "Ssms.exe",             label: "SQL Server", color: "#CC2927" },
    KnownApp { exe: "chrome.exe",           label: "Chrome",     color: "#4285F4" },
    KnownApp { exe: "firefox.exe",          label: "Firefox",    color: "#FF7139" },
    KnownApp { exe: "Postman.exe",          label: "Postman",    color: "#FF6C37" },
    KnownApp { exe: "figma.exe",            label: "Figma",      color: "#F24E1E" },
    KnownApp { exe: "GitHubDesktop.exe",    label: "GitHub",     color: "#24292F" },
    KnownApp { exe: "WindowsTerminal.exe",  label: "Terminal",   color: "#4D4D4D" },
    KnownApp { exe: "slack.exe",            label: "Slack",      color: "#4A154B" },
    KnownApp { exe: "Discord.exe",          label: "Discord",    color: "#5865F2" },
    KnownApp { exe: "node.exe",             label: "Node.js",    color: "#339933" },
    KnownApp { exe: "mongod.exe",           label: "MongoDB",    color: "#47A248" },
];

/// Build a lookup table: lowercase exe name → index into KNOWN_APPS.
fn build_lookup() -> HashMap<String, usize> {
    KNOWN_APPS
        .iter()
        .enumerate()
        .map(|(i, app)| (app.exe.to_lowercase(), i))
        .collect()
}

/// Snapshot the current process list and return the set of lowercase exe names.
fn snapshot_processes() -> HashSet<String> {
    let mut names = HashSet::new();
    unsafe {
        let Ok(snap) = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) else {
            return names;
        };
        let mut entry = PROCESSENTRY32W {
            dwSize: std::mem::size_of::<PROCESSENTRY32W>() as u32,
            ..Default::default()
        };
        if Process32FirstW(snap, &mut entry).is_ok() {
            loop {
                let len = entry
                    .szExeFile
                    .iter()
                    .position(|&c| c == 0)
                    .unwrap_or(entry.szExeFile.len());
                let name = OsString::from_wide(&entry.szExeFile[..len])
                    .to_string_lossy()
                    .to_lowercase();
                names.insert(name);
                if Process32NextW(snap, &mut entry).is_err() {
                    break;
                }
            }
        }
        let _ = CloseHandle(snap);
    }
    names
}

/// Starts the background scanner thread.  Called once from `lib.rs` setup.
pub fn start(app: AppHandle) {
    let lookup = build_lookup();

    std::thread::spawn(move || {
        log::line("process_scanner: started");
        let mut active: HashSet<String> = HashSet::new();

        loop {
            let processes = snapshot_processes();

            // Detect newly appeared apps.
            for (exe, &idx) in &lookup {
                if processes.contains(exe) && !active.contains(exe) {
                    let app_info = &KNOWN_APPS[idx];
                    let event = AppEvent {
                        id: format!("app_{}", app_info.label.to_lowercase().replace(' ', "_")),
                        label: app_info.label.to_string(),
                        color: app_info.color.to_string(),
                    };
                    log::line(format!("process_scanner: detected {}", app_info.label));
                    let _ = app.emit_to(WINDOW_LABEL, "app-detected", event);
                    active.insert(exe.clone());
                }
            }

            // Detect closed apps.
            let closed: Vec<String> = active
                .iter()
                .filter(|exe| !processes.contains(*exe))
                .cloned()
                .collect();
            for exe in closed {
                if let Some(&idx) = lookup.get(&exe) {
                    let app_info = &KNOWN_APPS[idx];
                    let event = AppEvent {
                        id: format!("app_{}", app_info.label.to_lowercase().replace(' ', "_")),
                        label: app_info.label.to_string(),
                        color: app_info.color.to_string(),
                    };
                    log::line(format!("process_scanner: closed {}", app_info.label));
                    let _ = app.emit_to(WINDOW_LABEL, "app-closed", event);
                }
                active.remove(&exe);
            }

            std::thread::sleep(POLL_INTERVAL);
        }
    });
}
