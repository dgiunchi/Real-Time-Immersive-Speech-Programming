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
const dgram = require("dgram");
const os    = require("os");
const fs    = require("fs");
const path  = require("path");

const serverRoot  = path.resolve(__dirname, "..");
const wozDir      = path.join(serverRoot, "samples", "apps", "wizard_of_oz");
const certSrcDir  = path.join(serverRoot, "samples", "apps", "code_runtime_generator");
const serverAsset = path.resolve(serverRoot, "..", "Unity", "Assets", "Demos", "Server.asset");
const controlUrl  = "http://localhost:8181";
const STUDY_PORTS = [8005, 8010, 8181, 8007];
const UBIQ_PORT   = 8005;   // Ubiq RoomServer TCP port (matches config.json)
const BEACON_PORT = 8007;   // UDP port the Quest listens on for auto-discovery
const ANDROID_PACKAGE = "com.DefaultCompany.ubiqgenie";  // must match Player Settings
const HANDOFF_FILE    = "study_server.txt";              // matches ServerAutoDiscovery.cs

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

// ── 2a. USB fallback: adb reverse ─────────────────────────────────────────────
// Wi-Fi auto-discovery only works when the Quest and Mac share a subnet. In a lab
// that is often false — the headset sits on the lab AP while the Mac is on the
// institutional network, so the UDP beacon never reaches it (broadcasts do not
// route) and the app falls back to localhost, which on the headset is itself:
// "connection lost".
//
// `adb reverse` maps the headset's OWN localhost:PORT back to this Mac over the
// USB cable. Because the app already dials localhost, that fallback becomes the
// working path — no rebuild, no IP config, and immune to subnet layout, client
// isolation and the missing multicast permission. Wi-Fi still works when the two
// do share a network; this simply means a session is never lost to the network.
function findAdb() {
    const candidates = [
        "adb",
        `${process.env.HOME}/Library/Android/sdk/platform-tools/adb`,
        `${process.env.HOME}/Library/Android/Sdk/platform-tools/adb`,
        "/opt/homebrew/bin/adb",
        "/usr/local/bin/adb"
    ];
    for (const c of candidates) {
        try {
            execSync(`"${c}" version`, { stdio: "pipe", shell: true });
            return c;
        } catch (_) {}
    }
    return null;
}

function setupUsbTunnel() {
    const adb = findAdb();
    if (!adb) { log("adb not found — USB fallback unavailable (Wi-Fi only)."); return; }
    let devices = "";
    try {
        devices = execSync(`"${adb}" devices`, { stdio: "pipe", shell: true }).toString();
    } catch (_) { return; }
    const connected = devices.split("\n").slice(1)
        .filter(l => l.trim() && l.includes("\tdevice")).length;
    if (!connected) { log("No headset on USB — relying on Wi-Fi discovery."); return; }

    // (a) IP handoff. Write this Mac's current address into the app's own storage
    // so the headset connects over Wi-Fi immediately, wherever we are today. This
    // is what lets the cable come off: the tunnel below dies with the cable, but
    // an address learned this way keeps working over Wi-Fi.
    try {
        const dest = `/sdcard/Android/data/${ANDROID_PACKAGE}/files/${HANDOFF_FILE}`;
        const tmp = path.join(os.tmpdir(), HANDOFF_FILE);
        fs.writeFileSync(tmp, `${lanIp}:${UBIQ_PORT}`);
        execSync(`"${adb}" shell mkdir -p /sdcard/Android/data/${ANDROID_PACKAGE}/files`,
                 { stdio: "pipe", shell: true });
        execSync(`"${adb}" push "${tmp}" "${dest}"`, { stdio: "pipe", shell: true });
        fs.unlinkSync(tmp);
        log(`Headset told to use ${lanIp}:${UBIQ_PORT} — it can run unplugged on Wi-Fi.`);
    } catch (e) {
        log(`Could not write IP to headset (${e.message.split("\n")[0]}).`);
    }

    // (b) USB tunnel. Maps the headset's own localhost back here, so it connects
    // even on a network where nothing else would work (client isolation, blocked
    // ports). Requires the cable to stay in.
    try {
        for (const port of [UBIQ_PORT, 8010, 8181]) {
            execSync(`"${adb}" reverse tcp:${port} tcp:${port}`, { stdio: "pipe", shell: true });
        }
        log(`USB tunnel active (ports ${UBIQ_PORT}/8010/8181) — backup if Wi-Fi is locked down.`);
    } catch (e) {
        log(`USB tunnel failed (${e.message.split("\n")[0]}) — relying on Wi-Fi discovery.`);
    }
}
setupUsbTunnel();

// ── 2b. Discovery beacon ──────────────────────────────────────────────────────
// The Quest headset listens on UDP BEACON_PORT and connects to whatever server
// address it hears here. This means the headset finds the Mac automatically on
// ANY Wi-Fi they share — no IP configuration and no rebuild when the network
// changes. Requires only that the Quest and Mac are on the same network.
function computeBroadcastAddrs() {
    const addrs = new Set(["255.255.255.255"]);
    const ifaces = os.networkInterfaces();
    for (const name of Object.keys(ifaces)) {
        for (const net of ifaces[name] || []) {
            if (net.family !== "IPv4" || net.internal) continue;
            const ip = net.address.split(".").map(Number);
            const mask = net.netmask.split(".").map(Number);
            const bcast = ip.map((o, i) => (o & mask[i]) | (~mask[i] & 0xff));
            addrs.add(bcast.join("."));
        }
    }
    return [...addrs];
}

function startBeacon() {
    const sock = dgram.createSocket({ type: "udp4", reuseAddr: true });
    const message = Buffer.from(`UBIQ_DISCOVERY:${lanIp}:${UBIQ_PORT}`);
    sock.on("error", (e) => log(`Beacon socket error: ${e.message}`));

    // Reply directly (unicast) whenever the headset asks. This path needs no
    // special Android permission on the Quest, so it works even if Wi-Fi
    // power-saving filters our broadcasts.
    sock.on("message", (buf, rinfo) => {
        if (buf.toString().startsWith("UBIQ_QUERY")) {
            sock.send(message, 0, message.length, rinfo.port, rinfo.address, () => {});
        }
    });

    sock.bind(BEACON_PORT, () => {
        sock.setBroadcast(true);
        const targets = computeBroadcastAddrs();
        log(`Discovery beacon broadcasting "${lanIp}:${UBIQ_PORT}" on UDP ${BEACON_PORT} → ${targets.join(", ")}`);
        setInterval(() => {
            for (const t of targets) {
                sock.send(message, 0, message.length, BEACON_PORT, t, () => {});
            }
        }, 1000);
    });
}
startBeacon();

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
