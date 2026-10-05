import fs from 'fs';
import { spawn } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

// 1. Always ensure we output valid JSON and exit 0, no matter what happens.
function safeExit() {
    console.log(JSON.stringify({}));
    process.exit(0);
}

// 2. Set an absolute fallback timeout to prevent Antigravity from ever hanging (Antigravity timeout is 5s)
setTimeout(safeExit, 1000);

try {
    const __dirname = path.dirname(fileURLToPath(import.meta.url));

    // Read stdin (Antigravity's payload)
    let input = "";
    try {
        // Read synchronously, but if it fails we don't crash
        input = fs.readFileSync(0, 'utf-8');
    } catch (e) {
        // Ignore stdin read errors
    }

    let payload = {};
    try {
        if (input) payload = JSON.parse(input);
    } catch (e) {
        // Ignore parse errors
    }

    const event = process.argv[2]; // PreToolUse, PostToolUse, Stop, PreInvocation

    let bubuEvent = event;
    let bubuPayload = {
        hook_event_name: event,
        bubu_agent: "antigravity",
        cwd: payload?.workspacePaths?.[0] || process.cwd(),
        session_id: payload?.conversationId || "unknown",
    };

    if (event === "PreToolUse") {
        bubuEvent = "PreToolUse";
        bubuPayload.tool_name = payload?.toolCall?.name;
        bubuPayload.tool_input = payload?.toolCall?.args;
    } else if (event === "PostToolUse") {
        if (payload?.error) {
            bubuEvent = "PostToolUseFailure";
        } else {
            bubuEvent = "PostToolUse";
        }
    } else if (event === "Stop") {
        bubuEvent = "Stop";
        if (payload?.error) {
            bubuEvent = "StopFailure";
        }
        bubuPayload.message = payload?.terminationReason || "Finished";
    } else if (event === "PreInvocation") {
        bubuEvent = "SessionStart";
        if (payload?.invocationNum > 1) {
            bubuEvent = "UserPromptSubmit";
            bubuPayload.prompt = "Thinking...";
        }
    }

    // Prioritize the installed hook in %LOCALAPPDATA% over the local compile
    const localAppData = process.env.LOCALAPPDATA || process.env.USERPROFILE + "\\AppData\\Local";
    let hookExe = path.join(localAppData, "bubu/bin/bubu-hook.exe");
    
    if (!fs.existsSync(hookExe)) {
        // Fallback to local target build if not installed
        hookExe = path.resolve(__dirname, "../windows/target/release/bubu-hook.exe");
    }

    if (fs.existsSync(hookExe)) {
        // Use detached spawn (Fire and Forget) so we don't wait for Bubu to respond.
        // This prevents the adapter from hanging Antigravity.
        const child = spawn(hookExe, [bubuEvent], {
            detached: true,
            stdio: ['pipe', 'ignore', 'ignore']
        });
        
        child.on('error', () => { /* ignore */ });
        
        if (child.stdin) {
            child.stdin.write(JSON.stringify(bubuPayload));
            child.stdin.end();
        }
        
        child.unref(); // Allow Node to exit immediately without waiting for the child
    }
} catch (e) {
    // Completely ignore any catastrophic logic errors
}

// Exit cleanly
safeExit();
