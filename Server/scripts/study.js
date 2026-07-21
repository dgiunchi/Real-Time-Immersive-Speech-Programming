"use strict";

/**
 * One-command study launcher.
 *
 *   cd Server && npm run study
 *
 * Automatically on every run:
 *   1. Kills any stale processes on ports 8005 / 8010 / 8181.
 *   2. Detects the Mac's current LAN IP and patches Unity/Assets/Demos/Server.asset
 *      so the Quest headset always finds the right server on any network.
 *   3. Installs node dependencies if missing.
 *   4. Copies TLS certs into the wizard_of_oz app if missing.
 *   5. Starts the Wizard-of-Oz server and opens the researcher panel.
 */

const { execSync, spawn } = require("child_process");
const fs   = require("fs");
const path = require("path");

const serverRoot  = path.resolve(__dirname, "..");
const wozDir      = path.join(serverRoot, "samples", "apps", "wizard_of_oz");
const certSrcDir  = path.join(serverRoot, "samples", "apps", "code_runtime_generator");
const serverAsset = path.resolve(serverRoot, "..", "Unity", "Assets", "Demos", "Server.asset");
const controlUrl  = "http://localhost:8181";
const STUDY_PORTS = [8005, 8010, 8181];

function log(msg) { console.log(`\x1b[1m\x1b[36m[study]\x1b[0m ${msg}`); }

// ── 1. Kill stale processes ───────────────────────────────────────────────────
log("Clearing ports 8005 / 8010 / 8181…");
for (const port of STUDY_PORTS) {
    try {
        execSync(`lsof -ti :${port} | xargs kill -9 2>/dev/null; true`, { shell: true, stdio: "pipe" });
    } catch (_) {}
}

// ── 2. Auto-detect LAN IP and patch Server.asset ──────────────────────────────
function getLanIp() {
    for (const iface of ["en0", "en1", "en2", "utun0"]) {
        try {
            const ip = execSync(`ipconfig getifaddr ${iface} 2>/dev/null`, { shell: true }).toString().trim();
            if (ip && !ip.startsWith("169.")) return ip;  // skip link-local
        } catch (_) {}
    }
    return "127.0.0.1";
}

function patchServerAsset(ip) {
    if (!fs.existsSync(serverAsset)) {
        log("Server.asset not found — skipping IP patch (is Unity folder at ../Unity?)");
        return;
    }
    let txt = fs.readFileSync(serverAsset, "utf8");
    // Only patch the "Platform Connection" block (platform 17 = Android/Quest standalone).
    // The main "Server" block stays as localhost (used by the editor + Quest Link).
    const patched = txt.replace(
        /(m_Name: Platform Connection[\s\S]{0,600}?sendToIp:)\s+\S+/,
        `$1 ${ip}`
    );
    if (patched === txt) {
        log(`Server.asset Quest IP already correct (${ip}).`);
        return;
    }
    fs.writeFileSync(serverAsset, patched);
    log(`Server.asset → Quest standalone IP set to ${ip}`);
}

const lanIp = getLanIp();
log(`LAN IP: ${lanIp}`);
patchServerAsset(lanIp);

// ── 3. Dependencies ───────────────────────────────────────────────────────────
if (!fs.existsSync(path.join(serverRoot, "node_modules", "ubiq"))) {
    log("Installing dependencies (first run, this may take a minute)…");
    execSync("npm install", { cwd: serverRoot, stdio: "inherit" });
} else {
    log("Dependencies present.");
}

// ── 4. Certs ──────────────────────────────────────────────────────────────────
for (const f of ["cert.pem", "key.pem"]) {
    const dst = path.join(wozDir, f);
    const src = path.join(certSrcDir, f);
    if (!fs.existsSync(dst) && fs.existsSync(src)) {
        fs.copyFileSync(src, dst);
        log(`Copied ${f} into wizard_of_oz.`);
    }
}

// ── 5. Speech-server reachability check (non-blocking) ───────────────────────
// Any HTTP response (even 404) counts as reachable; only a connect failure
// means the transcription server can't be reached from this network.
const sttUrl = process.env.STT_HTTP_URL || "http://130.136.2.161:50101/stt/transcribe";
(() => {
    const http = require("http");
    const u = new URL(sttUrl);
    const req = http.request(
        { host: u.hostname, port: u.port || 80, path: u.pathname, method: "GET", timeout: 4000 },
        () => log(`\x1b[32mSpeech server reachable\x1b[0m (${u.hostname}) — live transcripts will work.`)
    );
    const warn = () => {
        req.destroy();
        log(`\x1b[33mWARNING: speech server UNREACHABLE\x1b[0m (${u.hostname}).`);
        log(`\x1b[33mParticipants will NOT see live transcripts on this network.\x1b[0m`);
        log(`Injections still work. On the uni network it should connect automatically;`);
        log(`or set STT_HTTP_URL to a different endpoint before launching.`);
    };
    req.on("timeout", warn);
    req.on("error", warn);
    req.end();
})();

// ── 6. Open browser and start server ─────────────────────────────────────────
function openBrowser(url) {
    const cmd = process.platform === "darwin" ? "open"
              : process.platform === "win32"  ? "start"
              : "xdg-open";
    try { spawn(cmd, [url], { shell: true, stdio: "ignore", detached: true }).unref(); }
    catch (_) {}
}
setTimeout(() => { log(`Opening control panel: ${controlUrl}`); openBrowser(controlUrl); }, 4000);

log("Starting Wizard-of-Oz study server…");
const child = spawn("node", [path.join(__dirname, "start-wizard-of-oz.js")], {
    cwd: serverRoot, stdio: "inherit"
});
child.on("exit", (code) => process.exit(code || 0));
