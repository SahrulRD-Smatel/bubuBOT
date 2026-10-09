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
#[cfg(target_os = "windows")]
use windows::Win32::Foundation::CloseHandle;
#[cfg(target_os = "windows")]
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, Process32FirstW, Process32NextW, PROCESSENTRY32W, TH32CS_SNAPPROCESS,
};

use crate::log;

/// Interval between scans.  3 s keeps CPU usage negligible.
const POLL_INTERVAL: Duration = Duration::from_secs(3);

/// A known application the scanner looks for.
struct KnownApp {
    /// The exe names to match (case-insensitive).
    exes: &'static [&'static str],
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
    KnownApp { exes: &["code.exe", "code"],            label: "VS Code",    color: "#007ACC" },
    KnownApp { exes: &[ "antigravity ide.exe"], label: "Antigravity", color: "#8B5CF6" },
    KnownApp { exes: &["docker.exe", "docker desktop.exe", "dockerd", "docker"], label: "Docker",     color: "#2496ED" },
    KnownApp { exes: &["ssms.exe"],             label: "SQL Server", color: "#CC2927" },
    KnownApp { exes: &["chrome.exe", "chrome", "google-chrome"], label: "Chrome",     color: "#4285F4" },
    KnownApp { exes: &["firefox.exe", "firefox", "firefox-bin"], label: "Firefox",    color: "#FF7139" },
    KnownApp { exes: &["postman.exe", "postman"],          label: "Postman",    color: "#FF6C37" },
    KnownApp { exes: &["figma.exe", "figma"],            label: "Figma",      color: "#F24E1E" },
    KnownApp { exes: &["githubdesktop.exe", "githubdesktop"],    label: "GitHub",     color: "#24292F" },
    KnownApp { exes: &["windowsterminal.exe", "gnome-terminal", "alacritty", "kitty", "konsole", "terminal"],  label: "Terminal",   color: "#4D4D4D" },
    KnownApp { exes: &["slack.exe", "slack"],            label: "Slack",      color: "#4A154B" },
    KnownApp { exes: &["discord.exe", "discord"],          label: "Discord",    color: "#5865F2" },
    KnownApp { exes: &["node.exe", "node"],             label: "Node.js",    color: "#339933" },
    KnownApp { exes: &["mongod.exe", "mongod"],           label: "MongoDB",    color: "#47A248" },
    KnownApp { exes: &["winword.exe", "word"],            label: "Word",       color: "#55b5f5ff" },
    KnownApp { exes: &["excel.exe", "excel"],             label: "Excel",      color: "#42a16dff" },
    KnownApp { exes: &["powerpnt.exe", "powerpoint"],     label: "PowerPoint", color: "#be614aff" },
];

/// Build a lookup table: lowercase exe name → index into KNOWN_APPS.
fn build_lookup() -> HashMap<String, usize> {
    let mut map = HashMap::new();
    for (i, app) in KNOWN_APPS.iter().enumerate() {
        for exe in app.exes {
            map.insert(exe.to_string(), i);
        }
    }
    map
}

/// Snapshot the current process list and return the set of lowercase exe names.
#[cfg(target_os = "windows")]
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

#[cfg(not(target_os = "windows"))]
fn snapshot_processes() -> HashSet<String> {
    let mut names = HashSet::new();
    if let Ok(output) = std::process::Command::new("ps").args(&["-e", "-o", "comm="]).output() {
        let stdout = String::from_utf8_lossy(&output.stdout);
        for line in stdout.lines() {
            let name = line.trim().to_lowercase();
            // Optional: extract just the filename if it's a full path
            let file_name = std::path::Path::new(&name)
                .file_name()
                .map(|s| s.to_string_lossy().to_string())
                .unwrap_or(name);
            names.insert(file_name);
        }
    }
    names
}

/// Starts the background scanner thread.  Called once from `lib.rs` setup.
pub fn start(app: AppHandle) {
    let lookup = build_lookup();

    std::thread::spawn(move || {
        log::line("process_scanner: started");

        loop {
            let processes = snapshot_processes();
            let mut current_apps = Vec::new();
            let mut seen_ids = std::collections::HashSet::new();

            for (exe, &idx) in &lookup {
                if processes.contains(exe) {
                    let app_info = &KNOWN_APPS[idx];
                    let id = format!("app_{}", app_info.label.to_lowercase().replace(' ', "_"));
                    if !seen_ids.contains(&id) {
                        seen_ids.insert(id.clone());
                        let event = AppEvent {
                            id,
                            label: app_info.label.to_string(),
                            color: app_info.color.to_string(),
                        };
                        current_apps.push(event);
                    }
                }
            }
            
            // Log detected apps for debugging
            if !current_apps.is_empty() {
                let names: Vec<&str> = current_apps.iter().map(|a| a.label.as_str()).collect();
                log::line(format!("process_scanner: detected {} apps: {:?}", current_apps.len(), names));
            }
            
            let _ = app.emit("apps-sync", current_apps);
            std::thread::sleep(POLL_INTERVAL);
        }
    });
}
