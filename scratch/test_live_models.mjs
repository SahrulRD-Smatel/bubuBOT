import WebSocket from 'ws';
import { execSync } from 'child_process';

// Get key using powershell
const key = execSync(`powershell -Command "[CredHelper3]::Get('google-api-key.com.bubu.desktop')"`).toString().trim();
console.log("Key found length:", key.length);

const testModels = [
  "models/gemini-2.5-flash",
  "models/gemini-2.0-flash",
  "models/gemini-2.0-flash-exp",
  "models/gemini-flash-latest",
  "models/gemini-3.8-flash",
  "models/gemini-2.5-flash-preview-tts"
];

for (const model of testModels) {
  console.log(`\nTesting model: ${model}`);
  await new Promise((resolve) => {
    const ws = new WebSocket(`wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1alpha.GenerativeService.BidiGenerateContent?key=${key}`);
    const timer = setTimeout(() => {
      console.log(`[TIMEOUT] for ${model}`);
      ws.close();
      resolve();
    }, 4000);

    ws.on('open', () => {
      console.log(`[CONNECTED] Sending setup for ${model}`);
      ws.send(JSON.stringify({
        setup: {
          model,
          generationConfig: {
            responseModalities: ["AUDIO"]
          }
        }
      }));
    });

    ws.on('message', (data) => {
      console.log(`[RECEIVED] for ${model}:`, data.toString().slice(0, 150));
      clearTimeout(timer);
      ws.close();
      resolve();
    });

    ws.on('error', (err) => {
      console.log(`[ERROR] for ${model}:`, err.message);
    });

    ws.on('close', (code, reason) => {
      console.log(`[CLOSED] for ${model}: code=${code}, reason=${reason.toString()}`);
      clearTimeout(timer);
      resolve();
    });
  });
}
