#!/usr/bin/env node
/**
 * Capture helper for the demo films.
 *
 *   npm run demo:setup   — raise the headset's capture quality, keep it awake
 *   npm run demo:pull    — copy the clips off the headset into Recordings/
 *   npm run demo:reset   — put the capture settings back to stock
 *
 * The Quest records at a low default bitrate that turns the campfire scene into
 * a smear of blocking artefacts — fine for a bug report, not for something a
 * reviewer watches. `demo:setup` raises it before you record; it has no effect
 * on a clip already captured, so run it first.
 *
 * Recording itself stays manual (headset button or the in-VR Camera panel).
 * Driving it over adb is possible but fragile across Quest system updates, and
 * a failed take you don't notice is worse than pressing a button.
 */

const { execSync } = require("child_process");
const fs   = require("fs");
const path = require("path");

const OUT_DIR = path.resolve(__dirname, "..", "..", "Recordings");

// Where the Quest writes captures. The first that exists wins — Meta has moved
// this between system versions.
const REMOTE_DIRS = [
    "/sdcard/Oculus/VideoShots",
    "/sdcard/Oculus/Screenshots",
    "/sdcard/DCIM/Oculus",
    "/sdcard/Movies"
];

const C = { dim: "\x1b[2m", red: "\x1b[31m", green: "\x1b[32m", cyan: "\x1b[36m", off: "\x1b[0m" };
const log  = m => console.log(`${C.cyan}[demo]${C.off} ${m}`);
const warn = m => console.log(`${C.red}[demo]${C.off} ${m}`);
const ok   = m => console.log(`${C.green}[demo]${C.off} ${m}`);

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

const adb = findAdb();
if (!adb) {
    warn("adb not found. Install Android platform-tools, or open Unity's Android module.");
    process.exit(1);
}

function sh(cmd, quiet = true) {
    return execSync(`"${adb}" ${cmd}`, { stdio: quiet ? "pipe" : "inherit", shell: true })
        .toString();
}

function requireDevice() {
    let out = "";
    try { out = sh("devices"); } catch (_) {}
    const connected = out.split("\n").slice(1)
        .filter(l => l.trim() && l.includes("\tdevice")).length;
    if (!connected) {
        warn("No headset detected. Plug in the USB-C cable and accept the debugging prompt.");
        process.exit(1);
    }
}

/**
 * Capture quality. These are the stock Oculus debug properties; they persist
 * until reboot or until `demo:reset` clears them.
 *
 * 1920x1080@60 is deliberate: it is what a conference video wants, and dropping
 * to 30fps makes head turns judder badly enough to be distracting — which
 * matters most in task 4, where the whole point is that the participant turns.
 */
const CAPTURE_PROPS = {
    "debug.oculus.capture.width":   "1920",
    "debug.oculus.capture.height":  "1080",
    "debug.oculus.capture.fps":     "60",
    "debug.oculus.capture.bitrate": "15000000"
};

function setup() {
    requireDevice();

    for (const [k, v] of Object.entries(CAPTURE_PROPS)) {
        try { sh(`shell setprop ${k} ${v}`); }
        catch (_) { warn(`Could not set ${k} — capture will use the stock value.`); }
    }
    ok("Capture set to 1920x1080 @ 60fps, 15 Mbps.");

    // Off-head filming: the proximity sensor otherwise sleeps the headset the
    // moment you take it off to check a take.
    try {
        sh("shell am broadcast -a com.oculus.vrpowermanager.prox_close");
        ok("Proximity sensor held open — the headset will not sleep off-head.");
    } catch (_) {
        warn("Could not hold the proximity sensor open.");
    }

    console.log(`
${C.dim}Record with the headset button or the in-VR Camera panel.
Turn ON microphone audio in the capture settings — the participant's speech IS
the input, and a silent clip shows nothing.

These settings are for filming only. Run 'npm run demo:reset' before you run a
real participant: the proximity override keeps the headset awake on the desk,
which is not what you want mid-session.${C.off}`);
}

function pull() {
    requireDevice();
    fs.mkdirSync(OUT_DIR, { recursive: true });

    let found = 0;
    for (const dir of REMOTE_DIRS) {
        let listing = "";
        try { listing = sh(`shell ls ${dir} 2>/dev/null`); } catch (_) { continue; }

        const files = listing.split("\n").map(s => s.trim())
            .filter(f => /\.(mp4|jpg|png)$/i.test(f));
        if (!files.length) continue;

        for (const f of files) {
            const dest = path.join(OUT_DIR, f);
            if (fs.existsSync(dest)) { log(`skip ${f} (already here)`); continue; }
            try {
                sh(`pull "${dir}/${f}" "${dest}"`);
                ok(`pulled ${f}`);
                found++;
            } catch (_) { warn(`could not pull ${f}`); }
        }
    }

    if (!found) log("Nothing new to pull.");
    else ok(`${found} file(s) in ${OUT_DIR}`);
    log("Recordings/ is git-ignored — clips stay off the repo.");
}

function reset() {
    requireDevice();
    for (const k of Object.keys(CAPTURE_PROPS)) {
        try { sh(`shell setprop ${k} ""`); } catch (_) {}
    }
    try { sh("shell am broadcast -a com.oculus.vrpowermanager.automation_disable"); } catch (_) {}
    ok("Capture settings and proximity sensor back to stock.");
    warn("Also confirm the guardian is on before a participant wears it:");
    console.log(`${C.dim}  adb shell setprop debug.oculus.guardian_pause 0${C.off}`);
}

const cmd = process.argv[2];
if (cmd === "setup")      setup();
else if (cmd === "pull")  pull();
else if (cmd === "reset") reset();
else {
    console.log(`
Usage: npm run demo:setup | demo:pull | demo:reset

  setup   raise capture quality, stop the headset sleeping off-head
  pull    copy clips into Recordings/
  reset   undo setup (run before a real participant)
`);
    process.exit(1);
}
