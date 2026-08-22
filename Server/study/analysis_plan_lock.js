"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");

const ANALYSIS_PLAN_PATH = path.join(__dirname, "analysis_plan.v1.json");
const EXPECTED_ANALYSIS_PLAN_SHA256 = "062f3db1099f9d64ba1ea264889d5d1833420a70e66d457f75e3f727edd62ac4";

function fileSha256(filePath) {
    const canonicalBytes = fs.readFileSync(filePath, "utf8").replace(/\r\n/g, "\n");
    return crypto.createHash("sha256").update(canonicalBytes).digest("hex");
}

function verifyAnalysisPlanLock(filePath = ANALYSIS_PLAN_PATH) {
    const actualSha256 = fileSha256(filePath);
    return {
        ok: actualSha256 === EXPECTED_ANALYSIS_PLAN_SHA256,
        filePath,
        expectedSha256: EXPECTED_ANALYSIS_PLAN_SHA256,
        actualSha256,
    };
}

module.exports = { ANALYSIS_PLAN_PATH, EXPECTED_ANALYSIS_PLAN_SHA256, fileSha256, verifyAnalysisPlanLock };
