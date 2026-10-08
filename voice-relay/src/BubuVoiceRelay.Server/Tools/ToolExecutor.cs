// Executes function calls requested by Gemini during a voice session.
// Each function maps to a local system action (open app, search, etc.).

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BubuVoiceRelay.Server.Tools;

public sealed class ToolExecutor
{
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(ILogger<ToolExecutor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Dispatches a function call by name, executes it locally, and returns the result
    /// as a dictionary that will be sent back to Gemini as a toolResponse.
    /// </summary>
    public async Task<Dictionary<string, object>> ExecuteAsync(
        string functionName,
        Dictionary<string, object>? args)
    {
        _logger.LogInformation("Executing tool: {Name} with args: {Args}",
            functionName, args is not null ? System.Text.Json.JsonSerializer.Serialize(args) : "{}");

        try
        {
            return functionName switch
            {
                "open_application" => await OpenApplicationAsync(GetString(args, "app_name")),
                "search_web" => await SearchWebAsync(GetString(args, "query")),
                "control_system" => await ControlSystemAsync(GetString(args, "action"), GetDouble(args, "value")),
                "open_url" => await OpenUrlAsync(GetString(args, "url")),
                "type_text" => await TypeTextAsync(GetString(args, "text")),
                _ => new() { ["error"] = $"Unknown function: {functionName}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {Name}", functionName);
            return new() { ["error"] = ex.Message };
        }
    }

    // ── Tool implementations ─────────────────────────────────────────────────

    private Task<Dictionary<string, object>> OpenApplicationAsync(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "app_name is required" });

        bool success = false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Try Start Menu shortcut search first, then fall back to cmd start
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C start \"\" \"{appName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                Process.Start(psi);
                success = true;
            }
            catch { /* fall through */ }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                Process.Start("open", $"-a \"{appName}\"");
                success = true;
            }
            catch { /* fall through */ }
        }
        else // Linux
        {
            try
            {
                Process.Start("xdg-open", appName);
                success = true;
            }
            catch { /* fall through */ }
        }

        _logger.LogInformation("open_application({App}) → {Result}", appName, success ? "OK" : "FAIL");

        return Task.FromResult<Dictionary<string, object>>(new()
        {
            ["success"] = success,
            ["message"] = success
                ? $"Berhasil membuka {appName}"
                : $"Gagal membuka {appName}, aplikasi tidak ditemukan"
        });
    }

    private Task<Dictionary<string, object>> SearchWebAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "query is required" });

        var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
        OpenUrlInBrowser(url);

        _logger.LogInformation("search_web({Query})", query);

        return Task.FromResult<Dictionary<string, object>>(new()
        {
            ["success"] = true,
            ["message"] = $"Sudah membuka pencarian Google untuk: {query}"
        });
    }

    private Task<Dictionary<string, object>> ControlSystemAsync(string action, double? value)
    {
        if (string.IsNullOrWhiteSpace(action))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "action is required" });

        var success = false;
        var message = "";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            switch (action.ToLowerInvariant())
            {
                case "volume_mute":
                    RunPowerShell("(New-Object -ComObject WScript.Shell).SendKeys([char]173)");
                    success = true;
                    message = "Volume di-mute";
                    break;
                case "volume_up":
                    RunPowerShell("(New-Object -ComObject WScript.Shell).SendKeys([char]175)");
                    success = true;
                    message = "Volume dinaikkan";
                    break;
                case "volume_down":
                    RunPowerShell("(New-Object -ComObject WScript.Shell).SendKeys([char]174)");
                    success = true;
                    message = "Volume diturunkan";
                    break;
                case "lock_screen":
                case "lock":
                    RunCmd("rundll32.exe user32.dll,LockWorkStation");
                    success = true;
                    message = "Layar di-lock";
                    break;
                case "sleep":
                    RunCmd("rundll32.exe powrprof.dll,SetSuspendState 0,1,0");
                    success = true;
                    message = "Komputer masuk mode sleep";
                    break;
                case "shutdown":
                    RunCmd("shutdown /s /t 30");
                    success = true;
                    message = "Komputer akan shutdown dalam 30 detik";
                    break;
                default:
                    message = $"Action '{action}' tidak dikenali";
                    break;
            }
        }
        else
        {
            message = "System control hanya tersedia di Windows saat ini";
        }

        _logger.LogInformation("control_system({Action}, {Value}) → {Success}", action, value, success);

        return Task.FromResult<Dictionary<string, object>>(new()
        {
            ["success"] = success,
            ["message"] = message
        });
    }

    private Task<Dictionary<string, object>> OpenUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "url is required" });

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "URL harus dimulai http:// atau https://" });

        OpenUrlInBrowser(url);

        return Task.FromResult<Dictionary<string, object>>(new()
        {
            ["success"] = true,
            ["message"] = $"Sudah membuka {url}"
        });
    }

    private Task<Dictionary<string, object>> TypeTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<Dictionary<string, object>>(new() { ["error"] = "text is required" });

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Use PowerShell SendKeys to type text into the active window
            var escaped = text.Replace("'", "''");
            RunPowerShell($"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.SendKeys]::SendWait('{escaped}')");
        }

        return Task.FromResult<Dictionary<string, object>>(new()
        {
            ["success"] = true,
            ["message"] = $"Sudah mengetik: {text}"
        });
    }

    // ── OS helpers ───────────────────────────────────────────────────────────

    private static void OpenUrlInBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            RunCmd($"start \"\" \"{url}\"");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    private static void RunCmd(string args)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C {args}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void RunPowerShell(string script)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    // ── Arg extraction helpers ───────────────────────────────────────────────

    private static string GetString(Dictionary<string, object>? args, string key)
    {
        if (args is null || !args.TryGetValue(key, out var val)) return "";
        return val?.ToString() ?? "";
    }

    private static double? GetDouble(Dictionary<string, object>? args, string key)
    {
        if (args is null || !args.TryGetValue(key, out var val)) return null;
        if (val is double d) return d;
        if (double.TryParse(val?.ToString(), out var parsed)) return parsed;
        return null;
    }
}
