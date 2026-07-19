"use strict";

/**
 * One-command study launcher.
 *
 *   cd Server && npm run study
 *
 * - Installs node dependencies if node_modules is missing.
 * - Copies the local TLS certs into the wizard_of_oz app if they're not there.
 * - Starts the Wizard-of-Oz server.
 * - Opens the researcher control panel in the default browser.
 */

const { execSync, spawn } = require("child_process");
const fs   = require("fs");
const path = require("path");

const serverRoot = path.resolve(__dirname, "..");
const wozDir     = path.join(serverRoot, "samples", "apps", "wizard_of_oz");
const certSrcDir = path.join(serverRoot, "samples", "apps", "code_runtime_generator");
const controlUrl = "http://localhost:8181";

function log(msg) { console.log(`\x1b[1m\x1b[36m[study]\x1b[0m ${msg}`); }

// 1. Dependencies
if (!fs.existsSync(path.join(serverRoot, "node_modules", "ubiq"))) {
    log("Installing dependencies (first run, this may take a minute)…");
    execSync("npm install", { cwd: serverRoot, stdio: "inherit" });
} else {
    log("Dependencies present.");
}

// 2. Certs
for (const f of ["cert.pem", "key.pem"]) {
    const dst = path.join(wozDir, f);
    const src = path.join(certSrcDir, f);
    if (!fs.existsSync(dst) && fs.existsSync(src)) {
        fs.copyFileSync(src, dst);
        log(`Copied ${f} into wizard_of_oz.`);
    }
}

// 3. Open browser once the port is likely up
function openBrowser(url) {
    const cmd = process.platform === "darwin" ? "open"
              : process.platform === "win32"  ? "start"
              : "xdg-open";
    try { spawn(cmd, [url], { shell: true, stdio: "ignore", detached: true }).unref(); }
    catch (_) { /* ignore – researcher can open it manually */ }
}
setTimeout(() => { log(`Opening control panel: ${controlUrl}`); openBrowser(controlUrl); }, 4000);

// 4. Start the server (inherits stdio so logs stream to this terminal)
log("Starting Wizard-of-Oz study server…");
const child = spawn("node", [path.join(__dirname, "start-wizard-of-oz.js")], {
    cwd: serverRoot, stdio: "inherit"
});
child.on("exit", (code) => process.exit(code || 0));
