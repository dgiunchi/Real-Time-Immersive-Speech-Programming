"use strict";

/**
 * One-command study launcher.
 *
 *   cd Server && npm run study
 *
 * Automatically on every run:
 *   1. Kills any stale processes on ports 8005 / 8010 / 8181.
 *   2. Detects the Mac's current LAN IP and patches Assets/Demos/Server.asset
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

// Declared with the imports rather than beside its first use: the mode prompt
// reads it, and that runs before anything further down the file would have
// initialised it.
const IS_WINDOWS = process.platform === "win32";

const serverRoot  = path.resolve(__dirname, "..");
const repoRoot    = path.resolve(serverRoot, "..");
const wozDir      = path.join(serverRoot, "samples", "apps", "wizard_of_oz");
const certSrcDir  = path.join(serverRoot, "samples", "apps", "code_runtime_generator");
const serverAsset = path.resolve(repoRoot, "Unity", "Assets", "Demos", "Server.asset");
const controlUrl  = "http://localhost:8181";
const STUDY_PORTS = [8005, 8010, 8181, 8007];
const UBIQ_PORT   = 8005;   // Ubiq RoomServer TCP port (matches config.json)
const BEACON_PORT = 8007;   // UDP port the Quest listens on for auto-discovery
const ANDROID_PACKAGE = "com.DefaultCompany.ubiqgenie";  // must match Player Settings
const HANDOFF_FILE    = "study_server.txt";              // matches ServerAutoDiscovery.cs

function log(msg) { console.log(`\x1b[1m\x1b[36m[study]\x1b[0m ${msg}`); }

// ── 0. Mode ───────────────────────────────────────────────────────────────────
//
// One command, two modes. The server is identical in both; what differs is how
// the HEADSET is configured, which is why this is a launcher concern and not a
// setting inside the app.
//
//   study  the default. Guardian on, screen sleeps normally, capture at stock.
//   demo   filming. Capture at 1080p60, proximity sensor held open so the
//          headset does not sleep each time you take it off to check a take.
//
// Neither is irreversible, and study mode in particular is written to be
// SELF-HEALING: it does not merely decline to apply the demo settings, it
// actively puts every one of them back, every time it starts. That matters
// because the failure people actually have is forgetting to undo something.
// Nothing you can do in demo mode can survive into a participant session,
// because starting a participant session is what undoes it.
//
// The mode lives here, on the researcher's machine, and is deliberately not
// exposed over HTTP or to the headset. A participant has no route to it.

const MODE_FILE = path.join(os.tmpdir(), "dreamcodevr_mode");

/** Blocking sleep, no shell and no platform assumptions. */
function sleepSync(ms) {
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}

/** The console device to read a keypress from, per platform. */
const TTY_DEVICES = IS_WINDOWS ? ["CONIN$", 0] : ["/dev/tty", 0];

/** Open the terminal for reading, or null when there isn't one. */
function openTty() {
    for (const dev of TTY_DEVICES) {
        try { return fs.openSync(dev, "rs"); } catch (_) {}
    }
    return null;
}

/**
 * Blocking read of one line from the terminal.
 *
 * Reads /dev/tty rather than fd 0. On macOS, fd 0 is frequently left in
 * non-blocking mode, and fs.readSync then throws EAGAIN instead of waiting -
 * so the question appears, nothing waits for an answer, and the default is
 * taken silently. A menu that prints and does not wait is worse than no menu,
 * because it looks like it asked.
 *
 * Opening /dev/tty gives a fresh blocking descriptor regardless of what fd 0
 * is doing. EAGAIN is still retried in case the terminal hands one back.
 */
function promptSync(question) {
    process.stdout.write(question);
    const fd = openTty();
    if (fd === null) return "";            // no terminal: caller falls back

    const buf = Buffer.alloc(256);
    const deadline = Date.now() + 120000; // don't hang a session forever
    try {
        while (Date.now() < deadline) {
            try {
                const n = fs.readSync(fd, buf, 0, 256);
                if (n === 0) return "";    // EOF
                return buf.toString("utf8", 0, n).trim().toLowerCase();
            } catch (e) {
                // Blocking sleep with no shell involved. This used to run
                // `sleep 0.05`, which does not exist on Windows, so the catch
                // swallowed the failure and the loop span the CPU flat out
                // until the deadline.
                if (e.code === "EAGAIN") { sleepSync(50); continue; }
                if (e.code === "EOF") return "";
                return "";
            }
        }
        return "";
    } finally {
        try { fs.closeSync(fd); } catch (_) {}
    }
}

function resolveMode() {
    const argv = process.argv.slice(2).map(a => a.toLowerCase());
    if (argv.includes("--demo"))  return "demo";
    if (argv.includes("--study")) return "study";

    // Non-interactive (CI, piped, nohup): never silently pick the mode that
    // leaves a headset unsafe for a participant. Tested by whether a controlling
    // terminal can actually be opened, not by stdin.isTTY, which says nothing
    // about whether a read on it will block.
    let hasTty = false;
    const probe = openTty();
    if (probe !== null) { try { fs.closeSync(probe); } catch (_) {} hasTty = true; }
    if (!hasTty) {
        log("Not a terminal — defaulting to STUDY mode.");
        return "study";
    }

    let last = "";
    try { last = fs.readFileSync(MODE_FILE, "utf8").trim(); } catch (_) {}

    console.log("");
    console.log("  \x1b[1mWhich mode?\x1b[0m" + (last ? `   \x1b[2m(last time: ${last})\x1b[0m` : ""));
    console.log("    \x1b[1m1\x1b[0m  study   run a participant   \x1b[2m(guardian on, normal sleep)\x1b[0m");
    console.log("    \x1b[1m2\x1b[0m  demo    film the clips     \x1b[2m(1080p60, stays awake off-head)\x1b[0m");
    console.log("");
    const answer = promptSync("  1 or 2 [1]: ");
    return (answer === "2" || answer === "demo") ? "demo" : "study";
}

const MODE = resolveMode();
try { fs.writeFileSync(MODE_FILE, MODE); } catch (_) {}

// Capture properties, applied in demo and explicitly cleared in study.
const CAPTURE_PROPS = {
    "debug.oculus.capture.width":   "1920",
    "debug.oculus.capture.height":  "1080",
    "debug.oculus.capture.fps":     "60",
    "debug.oculus.capture.bitrate": "15000000"
};

// Returns true only if the settings were actually written to a headset.
// The caller must not claim the headset is safe on the strength of having tried.
function applyHeadsetMode(mode) {
    const adb = findAdb();
    if (!adb) { log("adb not found — headset settings unchanged."); return false; }
    let devices = "";
    try { devices = execSync(`"${adb}" devices`, { stdio: "pipe", shell: true }).toString(); }
    catch (_) { return false; }
    const connected = devices.split("\n").slice(1)
        .filter(l => l.trim() && l.includes("\tdevice")).length;
    if (!connected) {
        log(`No headset on USB — ${mode} settings not applied.`);
        if (mode === "study") {
            log("\x1b[33mPlug in and rerun if the headset was last used for filming.\x1b[0m");
        }
        return false;
    }

    const sh = c => { try { execSync(`"${adb}" ${c}`, { stdio: "pipe", shell: true }); } catch (_) {} };

    if (mode === "demo") {
        for (const [k, v] of Object.entries(CAPTURE_PROPS)) sh(`shell setprop ${k} ${v}`);
        sh("shell am broadcast -a com.oculus.vrpowermanager.prox_close");
        log("Demo mode: capture 1920x1080@60, headset will not sleep off-head.");
        return true;
    } else {
        // Undo everything demo mode does, unconditionally, whether or not this
        // machine is the one that set it.
        for (const k of Object.keys(CAPTURE_PROPS)) sh(`shell setprop ${k} ""`);
        sh("shell am broadcast -a com.oculus.vrpowermanager.automation_disable");
        sh("shell setprop debug.oculus.guardian_pause 0");
        log("Study mode: guardian on, normal sleep, capture back to stock.");
        return true;
    }
}

// ── 1. Kill stale processes ───────────────────────────────────────────────────
log("Clearing ports 8005 / 8010 / 8181…");
for (const port of STUDY_PORTS) {
    try {
        if (IS_WINDOWS) {
            // netstat lists the owning PID in the last column; taskkill /F ends it.
            // Wrapped in a for-loop rather than piped, because Windows has no xargs.
            execSync(
                `for /f "tokens=5" %a in ('netstat -ano ^| findstr :${port} ^| findstr LISTENING') ` +
                `do @taskkill /F /PID %a`,
                { shell: "cmd.exe", stdio: "pipe" });
        } else {
            execSync(`lsof -ti :${port} | xargs kill -9 2>/dev/null; true`, { shell: true, stdio: "pipe" });
        }
    } catch (_) {}
}

// ── 2. Auto-detect LAN IP and patch Server.asset ──────────────────────────────
//
// Reads the interface table directly instead of shelling out. The old version
// ran `ipconfig getifaddr en0`, which is macOS-only twice over: Windows has an
// `ipconfig` that takes no such argument and prints an unrelated table, and the
// en0/en1 names do not exist there at all. On Windows it fell through to
// 127.0.0.1, which is then what gets written onto the headset as the address to
// connect back to — so the headset would look for the server on itself and the
// session would never start.
function getLanIp() {
    const ifaces = os.networkInterfaces();
    // Wi-Fi first: the headset is on Wi-Fi, so an address on the same adapter is
    // the one most likely to be reachable from it. Virtual adapters (VirtualBox,
    // WSL, Docker, VPNs) hand out addresses that look perfectly valid and route
    // nowhere the headset can follow, so they are skipped explicitly.
    const skip = /^(vEthernet|VirtualBox|VMware|Docker|Loopback|utun|awdl|llw|bridge|Hyper-V)/i;
    const preferred = /^(en0|en1|Wi-?Fi|Wireless|Ethernet)/i;

    const found = [];
    for (const [name, addrs] of Object.entries(ifaces)) {
        if (skip.test(name)) continue;
        for (const a of addrs || []) {
            const family = typeof a.family === "number" ? a.family === 4 : a.family === "IPv4";
            if (!family || a.internal) continue;
            if (a.address.startsWith("169.")) continue;      // link-local, not routable
            found.push({ name, ip: a.address });
        }
    }
    if (!found.length) return "127.0.0.1";
    const best = found.find(f => preferred.test(f.name)) || found[0];
    if (found.length > 1) {
        log(`Multiple addresses found (${found.map(f => `${f.name}=${f.ip}`).join(", ")}); ` +
            `using ${best.ip}. Override with STUDY_LAN_IP if the headset cannot reach it.`);
    }
    return process.env.STUDY_LAN_IP || best.ip;
}

// Server.asset is tracked, and the line patched just below holds whichever LAN
// address this machine happens to have right now. So every single launch leaves
// the repository dirty, and two researchers on two networks conflict on that
// one line forever — a conflict neither of them has any reason to resolve,
// because the next launch on either machine overwrites the result anyway.
//
// `--skip-worktree` tells this clone to stop reporting the file. It is the flag
// that survives the file being rewritten in place, which `assume-unchanged` is
// not documented to do. Set on every run rather than written down as a setup
// step: a setup step that has to happen before the first launch is a setup step
// somebody will miss, and the symptom of missing it is a merge conflict during
// a session.
//
// To deliberately commit a change to this file:
//     git update-index --no-skip-worktree Assets/Demos/Server.asset
function keepServerAssetLocal() {
    const tracked = path.relative(repoRoot, serverAsset).split(path.sep).join("/");
    try {
        execSync(`git update-index --skip-worktree "${tracked}"`,
                 { cwd: repoRoot, stdio: "pipe" });
    } catch (_) {
        // Not a git clone (someone downloaded the ZIP), git not on PATH, or the
        // file is not tracked in this checkout. All three are fine — the flag is
        // a courtesy to whoever runs `git status`, not something the study needs.
    }
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
keepServerAssetLocal();
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
/**
 * Every adb that ships inside an installed Unity editor, newest first.
 *
 * Worth searching because it is the one adb a Unity user is guaranteed to
 * already have: installing the Android build support module brings it along,
 * so a colleague who has never heard of the platform-tools download still has
 * a working adb sitting on disk.
 */
function unityBundledAdbPaths(exe) {
    const roots = IS_WINDOWS
        ? ["C:\\Program Files\\Unity\\Hub\\Editor"]
        : ["/Applications/Unity/Hub/Editor"];
    // The two installs put PlaybackEngines in different places: on Windows it is
    // under Editor/Data, on macOS it sits beside Unity.app rather than inside it.
    const tail = IS_WINDOWS
        ? ["Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", exe]
        : ["PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", exe];
    const out = [];
    for (const root of roots) {
        let versions = [];
        try { versions = fs.readdirSync(root).sort().reverse(); } catch (_) { continue; }
        for (const v of versions) out.push(path.join(root, v, ...tail));
    }
    return out;
}

function findAdb() {
    // HOME is not set on Windows (it is USERPROFILE), so the macOS entries below
    // used to expand to the literal string "undefined/Library/..." there. Both
    // are read here so a missing one cannot silently produce a bogus path, and
    // the platform's own SDK locations are searched as well.
    const home = process.env.HOME || process.env.USERPROFILE || "";
    const localAppData = process.env.LOCALAPPDATA || `${home}\\AppData\\Local`;
    const exe = IS_WINDOWS ? "adb.exe" : "adb";
    const candidates = IS_WINDOWS ? [
        exe,                                                    // on PATH
        `${localAppData}\\Android\\Sdk\\platform-tools\\${exe}`, // Android Studio default
        `${home}\\Android\\Sdk\\platform-tools\\${exe}`,
        `C:\\Android\\platform-tools\\${exe}`,
        // Unity ships its own adb. Resolved by listing the Hub's editor folder
        // rather than globbing, because execSync does not expand wildcards.
        ...unityBundledAdbPaths(exe)
    ] : [
        exe,
        `${home}/Library/Android/sdk/platform-tools/adb`,
        `${home}/Library/Android/Sdk/platform-tools/adb`,
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
    // Parse the STATE, not just "is there a line".
    //
    // This used to count only lines containing "\tdevice", so a headset that was
    // plugged in but not yet trusted fell into the same branch as no headset at
    // all and printed "No headset on USB". That is the least useful thing it
    // could say: the cable IS in, the researcher can see it is in, and the one
    // action that would fix it — accepting a dialog inside the headset — is
    // never mentioned. A new headset is exactly when this happens, because the
    // trust prompt is per-computer and appears once.
    const rows = devices.split("\n").slice(1)
        .map(l => l.trim()).filter(Boolean)
        .map(l => { const [serial, state] = l.split(/\s+/); return { serial, state }; });

    const ready = rows.filter(r => r.state === "device");
    const untrusted = rows.filter(r => r.state === "unauthorized");
    const offline = rows.filter(r => r.state === "offline");

    if (untrusted.length) {
        log(`Headset ${untrusted[0].serial} is plugged in but NOT AUTHORISED.`);
        log("  Put it on — there is an 'Allow USB debugging?' prompt waiting.");
        log("  Tick 'Always allow from this computer', then Allow, then rerun this.");
        log("  (No prompt? Developer Mode is off for this headset — enable it in the Meta Quest phone app.)");
    }
    if (offline.length) {
        log(`Headset ${offline[0].serial} is offline — unplug and replug the cable.`);
    }
    if (!ready.length) {
        // Only say "nothing is plugged in" when nothing IS plugged in. Saying it
        // after having just reported an unauthorised headset reads as a
        // contradiction, and the reader believes the second line — so the
        // instructions above it get ignored and the cable gets blamed.
        if (!rows.length) {
            log("No headset on USB — relying on Wi-Fi discovery.");
        } else {
            log("Headset is connected but not usable yet — see above. Wi-Fi discovery still active.");
        }
        return;
    }

    // A headset with no app installed will sit on a loading screen forever and
    // give no clue why, so say it here rather than letting it be discovered in
    // front of a participant.
    try {
        const pkgs = execSync(`"${adb}" shell pm list packages ${ANDROID_PACKAGE}`,
                              { stdio: "pipe", shell: true }).toString();
        if (!pkgs.includes(ANDROID_PACKAGE)) {
            log(`The study app is NOT INSTALLED on ${ready[0].serial}.`);
            log("  Build it (Unity: Study > Build Quest APK), then:");
            log("  adb install -r Unity/Builds/DreamCodeVR-study.apk");
        }
    } catch (_) { /* a failed query is not worth stopping a session over */ }

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
applyHeadsetMode(MODE);

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
            // Some Node builds report family as the number 4 rather than the
            // string "IPv4". A strict string test silently matches nothing on
            // those, and the beacon then broadcasts to an empty target list
            // while still logging that it started.
            const isV4 = typeof net.family === "number" ? net.family === 4 : net.family === "IPv4";
            if (!isV4 || net.internal) continue;
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
// Printed LAST, once the server has finished its own startup noise, so the
// thing still on screen when you look up is what to do next rather than the
// tail of a boot log. The mode colour is repeated here because the dangerous
// mistake is filming settings surviving into a participant session.
function printNextSteps(mode) {
    const W = 68;
    const bg = mode === "demo" ? "\x1b[43m\x1b[30m" : "\x1b[42m\x1b[30m";
    const R  = "\x1b[0m", B = "\x1b[1m", D = "\x1b[2m";
    const bar  = (t = "") => console.log(bg + " " + t.padEnd(W - 2) + " " + R);
    const line = (t = "") => console.log("  " + t);

    console.log("");
    bar();
    bar(mode === "demo"
        ? "DEMO MODE - FILMING.  Do NOT run a participant in this mode."
        : "STUDY MODE - guardian on.  Safe to run a participant.");
    bar();
    console.log("");

    if (mode === "demo") {
        line(`${B}WHAT TO DO NOW${R}`);
        line("");
        line(`${B}1.${R} In the headset: turn microphone audio ${B}ON${R} in capture settings`);
        line(`${B}2.${R} On the Mac: start a QuickTime screen recording of the panel`);
        line(`${B}3.${R} Panel is open at ${B}${controlUrl}${R}`);
        line(`   Participant ID picks the condition:`);
        line(`   ${D}DEMO01 = A (no feedback)   DEMO02 = B (panel)   DEMO03 = C (agent)${R}`);
        line(`${B}4.${R} Clap once on camera, then shoot ${B}clip 0${R} first:`);
        line(`   ${D}DEMO01, task 3 - ask for a thousand stones, get 8, no explanation${R}`);
        line(`${B}5.${R} Full shot list with the exact lines: ${B}DEMO_FILMING.md${R}`);
        line("");
        line(`${B}WHEN YOU FINISH FILMING${R}`);
        line(`   Press ${B}Ctrl+C${R}. The clips are copied off the headset and the`);
        line(`   filming settings are undone automatically.`);
    } else {
        line(`${B}WHAT TO DO NOW${R}`);
        line("");
        line(`${B}1.${R} Consent form signed, and audio recorder running`);
        line(`   ${D}attribution answers are spoken to you, not into the headset${R}`);
        line(`${B}2.${R} Headset on the participant  ${D}(guardian is on - this mode set it)${R}`);
        line(`${B}3.${R} Panel at ${B}${controlUrl}${R} - enter the participant ID (P01, P02, ...)`);
        line(`   ${D}the ID sets the condition and task order automatically${R}`);
        line(`${B}4.${R} Run the ${B}practice${R} task first  ${D}(not analysed)${R}`);
        line(`${B}5.${R} Then the ${B}6 tasks${R}, following the star on each screen`);
        line(`   ${D}per task: brief - they speak - INJECT ERROR - probe - repair - end${R}`);
        line(`${B}6.${R} Questionnaire: ${B}${controlUrl}/questionnaire${R}`);
        line(`${B}7.${R} ${B}Debrief${R} - tell them the failures were scripted, not their fault`);
        line(`   ${D}script is in STUDY_GUIDE.md${R}`);
    }
    line("");
    line(`${D}Data saves automatically to Logs/.  Ctrl+C to stop.${R}`);
    console.log("");
}

setTimeout(() => { log(`Opening control panel: ${controlUrl}`); openBrowser(controlUrl); }, 4000);
// After the server's own ready banner, so this is what remains on screen.
setTimeout(() => printNextSteps(MODE), 7000);

log("Starting Wizard-of-Oz study server…");
const child = spawn("node", [path.join(__dirname, "start-wizard-of-oz.js")], {
    cwd: serverRoot, stdio: "inherit"
});
child.on("exit", (code) => process.exit(code || 0));

// ── Shutdown ──────────────────────────────────────────────────────────────────
//
// Demo mode tidies up after itself on Ctrl+C: clips come off the headset and
// the filming settings are undone. Both used to be commands to remember, which
// is the same as saying both were things to forget - and forgetting the second
// leaves a headset that will not sleep, with a guardian someone may have
// paused, waiting for the next person to put it on.
//
// Study mode has nothing to undo, so it just stops.

let shuttingDown = false;

process.on("SIGINT", () => {
    if (shuttingDown) process.exit(0);   // second Ctrl+C: give up and go
    shuttingDown = true;
    console.log("");

    if (MODE === "demo") {
        log("Finishing up…");
        try {
            execSync(`node "${path.join(__dirname, "demo.js")}" pull`,
                     { stdio: "inherit", shell: true });
        } catch (_) {
            log("\x1b[33mCould not copy clips - is the headset still plugged in?\x1b[0m");
            log(`\x1b[33mThey are still on the headset; rerun to try again.\x1b[0m`);
        }
        if (applyHeadsetMode("study")) {
            log("\x1b[32mHeadset back to participant-safe settings.\x1b[0m");
        } else {
            log("\x1b[33mHeadset NOT reset - it was not connected.\x1b[0m");
            log("\x1b[33mPlug it in and run this again before any participant.\x1b[0m");
        }
    }

    try { child.kill("SIGTERM"); } catch (_) {}
    setTimeout(() => process.exit(0), 400);
});
