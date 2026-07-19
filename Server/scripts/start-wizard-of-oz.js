"use strict";

const path = require("path");

const appDir = path.resolve(__dirname, "..", "samples", "apps", "wizard_of_oz");
process.chdir(appDir);

const { WizardOfOzApp } = require(path.join(appDir, "app.js"));

const app = new WizardOfOzApp();
app.start();
