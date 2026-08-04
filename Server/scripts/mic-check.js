#!/usr/bin/env node
/**
 * Live microphone monitor.
 *
 * Answers one question in real time: is the headset actually capturing sound?
 *
 * It exists because a dead microphone looks identical to a working one from the
 * control panel — the hold-to-record button reports success, Unity logs
 * "recording start", and the transcript simply stays empty. The reason is almost
 * never the code. Quest mutes the microphone at the OS level and drops audio
 * focus whenever the headset is off the head, so the app opens the device, gets
 * a few milliseconds of audio, and is then handed silence with no error
 * anywhere. Diagnosing that from logs takes twenty minutes; this takes five
 * seconds.
 *
 *   node Server/scripts/mic-check.js
 *
 * Put the headset on, speak, and watch LEVEL. If it moves, capture works and any
 * empty transcript is a problem further down the pipeline. If it stays 0.000
 * while WORN reads no, the headset is off your head — that is the whole answer.
 */

const http = require("http");

const PORT = Number(process.env.WOZ_CONTROL_PORT) || 8181;
const INTERVAL_MS = 500;

function getStatus() {
    return new Promise((resolve) => {
        const req = http.get(
            { host: "127.0.0.1", port: PORT, path: "/status", timeout: 2000 },
            (res) => {
                let body = "";
                res.on("data", (c) => (body += c));
                res.on("end", () => {
                    try { resolve(JSON.parse(body)); } catch { resolve(null); }
                });
            }
        );
        req.on("error", () => resolve(null));
        req.on("timeout", () => { req.destroy(); resolve(null); });
    });
}

// A bar rather than a number, because the thing being judged is "is it moving",
// which the eye reads far faster from a length than from digits.
function bar(level, width = 28) {
    const filled = Math.min(width, Math.round(level * width * 8));
    return "[" + "#".repeat(filled) + ".".repeat(width - filled) + "]";
}

let lastTranscriptCount = 0;
let sawAnyLevel = false;

async function tick() {
    const status = await getStatus();
    if (!status) {
        process.stdout.write("\r  server not answering on :" + PORT + " — is it running?   ");
        return;
    }

    const mic = status.mic || {};
    const ageS = mic.at ? ((Date.now() - mic.at) / 1000).toFixed(1) : "?";
    const level = Number(mic.level || 0);
    if (level > 0) sawAnyLevel = true;

    // A stale report means the headset stopped talking to us altogether, which
    // is a different failure from a live headset capturing silence.
    const reporting = mic.stale ? "NO (stale " + ageS + "s)" : "yes";

    process.stdout.write(
        "\r  " + bar(level) +
        "  level " + level.toFixed(3) +
        " | reporting " + reporting +
        " | devices " + (mic.devices ?? "?") +
        " | rec " + (mic.recording ? "ON " : "off") +
        "   "
    );

    const history = status.transcriptHistory || [];
    if (history.length > lastTranscriptCount) {
        for (const t of history.slice(lastTranscriptCount)) {
            const text = typeof t === "string" ? t : (t && (t.text || t.transcript)) || "";
            process.stdout.write("\n  HEARD: \"" + text + "\"\n");
        }
        lastTranscriptCount = history.length;
    }
}

console.log("");
console.log("  Live microphone monitor — Ctrl+C to stop");
console.log("  Put the headset ON, hold the trigger, and speak.");
console.log("");
console.log("  If LEVEL stays 0.000 with the headset off your head, that is");
console.log("  expected: Quest mutes the microphone when it is not worn.");
console.log("");

setInterval(tick, INTERVAL_MS);
tick();

process.on("SIGINT", () => {
    console.log("\n");
    console.log(sawAnyLevel
        ? "  Capture confirmed — the microphone produced signal."
        : "  No signal seen. If the headset was worn the whole time, check\n" +
          "  Microphone permission in the headset (Settings > Privacy).");
    console.log("");
    process.exit(0);
});
