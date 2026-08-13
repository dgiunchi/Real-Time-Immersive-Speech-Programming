"use strict";

const path = require("path");

require("./load-local-env");

const appDir = path.resolve(__dirname, "..", "samples", "apps", "code_runtime_generator");
process.chdir(appDir);

const { CodeGeneration } = require(path.join(appDir, "app.js"));

const app = new CodeGeneration();
app.start();
