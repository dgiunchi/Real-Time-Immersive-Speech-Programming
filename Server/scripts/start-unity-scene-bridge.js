"use strict";

const path = require("path");

const appDir = path.resolve(__dirname, "..", "mcp", "unity_scene_bridge");
process.chdir(appDir);

// server.js is a functional MCP entrypoint (not an ApplicationController
// subclass like the sample apps) - requiring it runs main() immediately.
require(path.join(appDir, "server.js"));
