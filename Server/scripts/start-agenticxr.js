"use strict";

const path = require("path");

if (!process.env.ANTHROPIC_API_KEY) {
    console.error("[AgenticXR] ANTHROPIC_API_KEY is missing. Set it in this terminal before starting; do not put it in config.json.");
    process.exit(1);
}

process.env.AGENTICXR_MODE = "claude";
const appDir = path.resolve(__dirname, "..", "samples", "apps", "code_runtime_generator");
process.chdir(appDir);
const { CodeGeneration } = require(path.join(appDir, "app.js"));
new CodeGeneration().start();
