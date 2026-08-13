"use strict";

// Loads Server/.env (gitignored - see root .gitignore's `.env` rule) into
// process.env so local secrets like ANTHROPIC_API_KEY do not need to be
// exported in every terminal. Deliberately dependency-free, and values already
// present in the environment always win, so a PowerShell `$env:...` override
// still behaves exactly as documented in docs/LIVE_SYSTEM_REQUIREMENTS.md.
// Secret VALUES are never printed - only the variable names that were loaded.

const fs = require("fs");
const path = require("path");

const envPath = path.join(__dirname, "..", ".env");
if (fs.existsSync(envPath)) {
    const loaded = [];
    for (const line of fs.readFileSync(envPath, "utf8").split(/\r?\n/)) {
        const trimmed = line.trim();
        if (!trimmed || trimmed.startsWith("#")) continue;
        const separator = trimmed.indexOf("=");
        if (separator <= 0) continue;
        const name = trimmed.slice(0, separator).trim();
        const value = trimmed.slice(separator + 1).trim();
        if (!name || process.env[name] !== undefined) continue;
        process.env[name] = value;
        loaded.push(name);
    }
    if (loaded.length) console.error(`[load-local-env] loaded from Server/.env: ${loaded.join(", ")}`);
}
