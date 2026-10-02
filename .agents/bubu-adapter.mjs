import fs from 'fs';
import { spawnSync } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Read stdin (Antigravity's payload)
const input = fs.readFileSync(0, 'utf-8');
let payload = {};
try {
    payload = JSON.parse(input);
} catch (e) {
    // Ignore parse errors if empty
}

const event = process.argv[2]; // PreToolUse, PostToolUse, Stop, PreInvocation

let bubuEvent = event;
let bubuPayload = {
    hook_event_name: event,
    bubu_agent: "antigravity",
    cwd: payload.workspacePaths?.[0] || process.cwd(),
    session_id: payload.conversationId,
};

if (event === "PreToolUse") {
    bubuEvent = "PreToolUse";
    bubuPayload.tool_name = payload.toolCall?.name;
    bubuPayload.tool_input = payload.toolCall?.args;
} else if (event === "PostToolUse") {
    if (payload.error) {
        bubuEvent = "PostToolUseFailure";
    } else {
        bubuEvent = "PostToolUse";
    }
} else if (event === "Stop") {
    bubuEvent = "Stop";
    if (payload.error) {
        bubuEvent = "StopFailure";
    }
    bubuPayload.message = payload.terminationReason || "Finished";
} else if (event === "PreInvocation") {
    bubuEvent = "SessionStart"; // Let's trigger a SessionStart when we start thinking
    if (payload.invocationNum > 1) {
        bubuEvent = "UserPromptSubmit";
        bubuPayload.prompt = "Thinking...";
    }
}

// Ensure bubu-hook.exe exists in the target release folder
const hookExe = path.resolve(__dirname, "../windows/target/release/bubu-hook.exe");

try {
    spawnSync(hookExe, [bubuEvent], {
        input: JSON.stringify(bubuPayload),
        encoding: 'utf-8',
        timeout: 2000
    });
} catch (e) {
    // Ignore hook execution errors so we don't break Antigravity
}

// Return empty JSON to satisfy Antigravity hook contract
console.log(JSON.stringify({}));
