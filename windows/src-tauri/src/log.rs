// Small append-only log at %LOCALAPPDATA%\Bubu\bubu.log — the Windows
// equivalent of nbLog() in HookServer.swift. Nothing leaves the machine.

use std::io::Write;

use crate::platform;

pub fn line(message: impl AsRef<str>) {
    let t = platform::local_time();
    let stamp = format!(
        "{:04}-{:02}-{:02} {:02}:{:02}:{:02}",
        t.year, t.month, t.day, t.hour, t.minute, t.second
    );
    let dir = platform::local_dir();
    if std::fs::create_dir_all(&dir).is_err() {
        return;
    }
    let path = dir.join("bubu.log");
    // Keep it from growing forever: start fresh past ~1 MB.
    if std::fs::metadata(&path).map(|m| m.len() > 1_000_000).unwrap_or(false) {
        let _ = std::fs::remove_file(&path);
    }
    if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{stamp} {}", message.as_ref());
    }
}
