"use strict";

/**
 * Wizard-of-Oz Server App
 * ========================
 * Replaces the live LLM pipeline with a researcher-controlled HTTP endpoint.
 *
 * Pipeline:
 *   1. Audio from participant → STT (unchanged – participant must see transcript).
 *   2. Transcription forwarded to researcher via console + stored in memory.
 *   3. Researcher POSTs to http://localhost:<controlPort>/inject with:
 *        { "task": 1, "response": "success" }   // or "error1" … "error4"
 *   4. Server looks up the pre-scripted code for that task/response and sends it
 *      to the Unity client on network ID 94 (same as the live LLM pipeline).
 *
 * This keeps the study reproducible: every participant in a given condition
 * gets the exact same code/error for each task.
 *
 * Run:
 *   node app.js            (uses config.json in this directory)
 *
 * Researcher API (HTTP):
 *   GET  /status           → last transcript + current task
 *   POST /inject           → { task, response } to inject a pre-scripted reply
 *   POST /task             → { task } to change the active task number (1-based)
 */

const http = require("http");
const fs   = require("fs");
const path = require("path");
const { NetworkId } = require("ubiq/ubiq/messaging");
const { MessageReader, ApplicationController } = require("ubiq-genie-components");
const { SpeechToTextService } = require("ubiq-genie-services");
const nconf = require("nconf");

const STT_CONTROL_PREFIX = "__STT_CONTROL__:";
const MIC_STATUS_PREFIX  = "__MIC_STATUS__:";
const CODE_NETWORK_ID    = 94;  // CodeGenerationManager (runtime Roslyn — Editor only)
const OUTCOME_NETWORK_ID = 99;  // StudyOutcomes (pre-compiled — works on the Quest)
const STT_NETWORK_ID     = 98;

const PUBLIC_DIR = path.join(__dirname, "public");
// Study results live in <project root>/Logs (git-ignored, human-findable).
const LOG_DIR    = path.resolve(__dirname, "..", "..", "..", "..", "Logs");

// ── Study content: 3 tasks × 3 equivalent variants ───────────────────────────
//
// DESIGN (per supervisor meeting, July 2026)
// Every participant experiences ALL THREE conditions (within-subjects), so each
// task needs equivalent-but-different variants — otherwise a participant learns
// the correction in condition 1 and simply repeats it in conditions 2 and 3.
// Variants therefore differ in WHICH detail the system "fails" on, not just in
// surface wording.
//
// TRIAL PROTOCOL
//   attempt 1 → researcher injects the variant's ERROR
//   feedback  → per condition (A: none, B: text panel, C: embodied agent)
//   attempt 2 → if the participant supplies the missing detail, inject SUCCESS;
//               if they repeat the same omission, inject the ERROR again.
//
// Everything here is data, not code: the headset runs these specs directly, so
// tasks/wording/dialogue can be edited WITHOUT rebuilding and redeploying the
// Quest APK. Edit freely.

const TASKS = {
    task1: {
        name: "Create an object",
        variants: {
            v1: {
                label: "Create a ball",
                prompt: "Ask the system to create a ball.",
                missing: "position",
                error: {
                    action: "spawn", shape: "sphere", pos: "origin", physics: true,
                    scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
                    label: "Created a ball",
                    errorText: "A ball was created, but at the centre of the room rather than near you — no position was specified.",
                    agentPre: "Okay, I'll create a ball for you.",
                    agentPost: "I made the ball, but I wasn't told where to put it, so it went to the centre of the room. Where would you like it?"
                },
                success: {
                    action: "spawn", shape: "sphere", pos: "hand", physics: true,
                    scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
                    label: "Created a ball in front of you",
                    agentPre: "Alright, creating a ball right in front of you.",
                    agentPost: "There you go — a ball just in front of you."
                }
            },
            v2: {
                label: "Create a cube",
                prompt: "Ask the system to create a cube.",
                missing: "size",
                error: {
                    action: "spawn", shape: "cube", pos: "hand", physics: true,
                    scaleX: 0.9, scaleY: 0.9, scaleZ: 0.9,
                    label: "Created a cube",
                    errorText: "A cube was created, but no size was given, so it was made far larger than expected.",
                    agentPre: "Sure, I'll make a cube.",
                    agentPost: "The cube is there, but no size was mentioned so I guessed — it came out very large. How big should it be?"
                },
                success: {
                    action: "spawn", shape: "cube", pos: "hand", physics: true,
                    scaleX: 0.2, scaleY: 0.2, scaleZ: 0.2,
                    label: "Created a small cube in front of you",
                    agentPre: "Okay, making a small cube in front of you.",
                    agentPost: "Done — a small cube, just in front of you."
                }
            },
            v3: {
                label: "Create a pillar",
                prompt: "Ask the system to create a tall pillar.",
                missing: "shape detail",
                error: {
                    action: "spawn", shape: "cylinder", pos: "hand", physics: false,
                    scaleX: 0.4, scaleY: 0.08, scaleZ: 0.4,
                    label: "Created a pillar",
                    errorText: "A pillar was created, but its proportions weren't specified — it came out flat and wide instead of tall.",
                    agentPre: "I'll create a pillar for you.",
                    agentPost: "I made the pillar, but without proportions it came out flat rather than tall. How tall should it be?"
                },
                success: {
                    action: "spawn", shape: "cylinder", pos: "hand", physics: false,
                    scaleX: 0.12, scaleY: 0.5, scaleZ: 0.12,
                    label: "Created a tall pillar in front of you",
                    agentPre: "Okay, a tall pillar coming up.",
                    agentPost: "There it is — a tall, narrow pillar."
                }
            }
        }
    },

    task2: {
        name: "Change an object's appearance",
        variants: {
            v1: {
                label: "Colour it green (ambiguous target)",
                prompt: "Ask the system to make the object green.",
                missing: "which object",
                error: {
                    action: "recolor", color: "#2ecc40", applyToAll: true,
                    label: "Applied green",
                    errorText: "Everything in the scene turned green — it wasn't clear which object you meant.",
                    agentPre: "You'd like something green — let me apply that.",
                    agentPost: "I turned everything green because I wasn't sure which object you meant. Which one should it be?"
                },
                success: {
                    action: "recolor", color: "#2ecc40",
                    label: "Coloured the object green",
                    agentPre: "Okay, colouring that object green.",
                    agentPost: "Done — that object is now green."
                }
            },
            v2: {
                label: "Colour it blue (wrong shade)",
                prompt: "Ask the system to make the object blue.",
                missing: "which shade",
                error: {
                    action: "recolor", color: "#00b3b3",
                    label: "Applied blue",
                    errorText: "The object was coloured, but it came out teal rather than the blue you may have expected — no specific shade was given.",
                    agentPre: "Sure, making it blue.",
                    agentPost: "It's coloured, but the shade came out teal. No particular blue was specified — which shade did you want?"
                },
                success: {
                    action: "recolor", color: "#1560ff",
                    label: "Coloured the object blue",
                    agentPre: "Okay, a proper blue this time.",
                    agentPost: "There — a clear blue."
                }
            },
            v3: {
                label: "Colour it red (does not persist)",
                prompt: "Ask the system to make the object red.",
                missing: "make it permanent",
                error: {
                    action: "recolor", color: "#e02020", revert: true,
                    label: "Applied red",
                    errorText: "The colour was applied but did not persist — it reverted after a moment because the change wasn't made permanent.",
                    agentPre: "Applying red now.",
                    agentPost: "It turned red briefly and then reverted — the change wasn't kept. Shall I make it permanent?"
                },
                success: {
                    action: "recolor", color: "#e02020",
                    label: "Coloured the object red permanently",
                    agentPre: "Okay, making it red and keeping it that way.",
                    agentPost: "Done — red, and it will stay."
                }
            }
        }
    },

    task3: {
        name: "Make an object move",
        variants: {
            v1: {
                label: "Orbit (wrong centre)",
                prompt: "Ask the system to make the object circle around the cube.",
                missing: "what to circle around",
                error: {
                    action: "orbit", orbitTarget: "origin", orbitAxis: "up", orbitSpeed: 60,
                    label: "Started an orbit",
                    errorText: "The object is circling the centre of the room rather than the cube — no target was identified.",
                    agentPre: "Okay, I'll set it circling.",
                    agentPost: "It's circling, but around the centre of the room rather than the cube. What should it circle around?"
                },
                success: {
                    action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 60,
                    label: "Object now orbits the cube",
                    agentPre: "Setting it to circle the cube.",
                    agentPost: "There — it's now circling the cube."
                }
            },
            v2: {
                label: "Orbit (wrong plane)",
                prompt: "Ask the system to make the object circle the cube horizontally.",
                missing: "which plane",
                error: {
                    action: "orbit", orbitTarget: "cube", orbitAxis: "forward", orbitSpeed: 60,
                    label: "Started an orbit",
                    errorText: "The object is circling on a vertical plane — the orientation of the circle wasn't specified.",
                    agentPre: "Alright, setting up the circling motion.",
                    agentPost: "It's circling, but vertically rather than flat. Which plane did you want?"
                },
                success: {
                    action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 60,
                    label: "Object orbits the cube horizontally",
                    agentPre: "Okay, a flat, horizontal circle.",
                    agentPost: "Done — it's circling flat around the cube now."
                }
            },
            v3: {
                label: "Orbit (wrong speed)",
                prompt: "Ask the system to make the object circle the cube slowly.",
                missing: "how fast",
                error: {
                    action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 900,
                    label: "Started an orbit",
                    errorText: "The object is circling far too fast to follow — no speed was specified.",
                    agentPre: "Okay, making it circle the cube.",
                    agentPost: "It's circling, but very fast — no speed was given. How fast should it go?"
                },
                success: {
                    action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 25,
                    label: "Object orbits the cube slowly",
                    agentPre: "Setting a slow, steady circle.",
                    agentPost: "There — a slow, steady orbit."
                }
            }
        }
    }
};

// ── Counterbalancing ─────────────────────────────────────────────────────────
// Within-subjects: every participant sees all three conditions. Condition order
// is rotated across participants (Latin square) so order effects don't load onto
// one condition, and the variant offset is rotated too so a given variant isn't
// always paired with the same condition.

const CONDITION_ORDERS = [
    ["A", "B", "C"], ["B", "C", "A"], ["C", "A", "B"],
    ["A", "C", "B"], ["B", "A", "C"], ["C", "B", "A"]
];
const VARIANT_KEYS = ["v1", "v2", "v3"];

/** Stable small integer from a participant id (P01, 7, anything). */
function participantIndex(pid) {
    const digits = String(pid || "").match(/\d+/);
    if (digits) return parseInt(digits[0], 10);
    let h = 0;
    for (const ch of String(pid || "")) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return h;
}

/**
 * The full plan for a participant: which condition each block uses, and which
 * variant of each task that block uses.
 */
function planForParticipant(pid) {
    const n = participantIndex(pid);
    const order = CONDITION_ORDERS[n % CONDITION_ORDERS.length];
    const offset = n % VARIANT_KEYS.length;
    return order.map((condition, block) => ({
        block: block + 1,
        condition,
        variants: Object.keys(TASKS).reduce((acc, taskKey) => {
            acc[taskKey] = VARIANT_KEYS[(block + offset) % VARIANT_KEYS.length];
            return acc;
        }, {})
    }));
}

// ── Application ───────────────────────────────────────────────────────────────

class WizardOfOzApp extends ApplicationController {
    constructor(configFile = "config.json") {
        super(configFile);
        this.lastTranscript  = "";
        this.transcriptHistory = [];
        this.activeTask      = "task1";
        this.controlPort     = nconf.get("wizardControlPort") || 8181;
        this.micStatus       = null;

        // Session state (set by the researcher on the web panel before each run)
        this.session = {
            participantId: "",
            condition:     "",   // "A" | "B" | "C" — the CURRENT block's condition
            block:         1,    // which of the 3 condition blocks we're in
            startedAt:     null,
            plan:          []    // counterbalanced condition/variant plan
        };

        // Per-trial state. A trial = one (condition, task) pair; attempt 1 is the
        // injected error, attempt 2+ are the participant's recovery attempts.
        this.attempts = {};        // "B|task1" -> attempt number
        this.trialStartedAt = {};  // "B|task1" -> ms timestamp of first transcript

        fs.mkdirSync(LOG_DIR, { recursive: true });
    }

    // ── Trial helpers ────────────────────────────────────────────────────────

    trialKey(taskKey = this.activeTask) {
        return `${this.session.condition || "?"}|${taskKey}`;
    }

    /** Variant in use for a task in the current block (from the counterbalance plan). */
    currentVariant(taskKey = this.activeTask) {
        const entry = (this.session.plan || []).find(p => p.condition === this.session.condition);
        return (entry && entry.variants && entry.variants[taskKey]) || "v1";
    }

    attemptCount(taskKey = this.activeTask) {
        return this.attempts[this.trialKey(taskKey)] || 0;
    }

    // ── Data logging ─────────────────────────────────────────────────────────

    /**
     * One row per event, with the full trial context the analysis needs:
     * participant, condition, block, task, variant, attempt, event, detail.
     */
    logEvent(type, detail = "", extra = {}) {
        // Before a session is started, events are warm-up/testing — keep them out
        // of participant data files.
        const pid = this.session.participantId || "warmup";
        const file = path.join(LOG_DIR, `${pid}_events.csv`);
        const isNew = !fs.existsSync(file);
        const taskKey = extra.task || this.activeTask;
        const row = [
            new Date().toISOString(),
            pid,
            this.session.condition || "",
            this.session.block || "",
            taskKey,
            extra.variant || this.currentVariant(taskKey),
            extra.attempt !== undefined ? extra.attempt : this.attemptCount(taskKey),
            extra.errorType || "",
            type,
            extra.msSinceTrialStart !== undefined ? extra.msSinceTrialStart : "",
            csvEscape(detail)
        ].join(",");
        if (isNew) {
            fs.writeFileSync(file,
                "timestamp,participantId,condition,block,task,variant,attempt,errorType," +
                "eventType,msSinceTrialStart,detail\n");
        }
        fs.appendFileSync(file, row + "\n");
    }

    /** Records the session/block start row in the master sessions.csv. */
    logSessionStart() {
        const file = path.join(LOG_DIR, "sessions.csv");
        const isNew = !fs.existsSync(file);
        if (isNew) fs.writeFileSync(file,
            "timestamp,participantId,condition,block,conditionOrder,variantPlan\n");
        const order = (this.session.plan || []).map(p => p.condition).join("-");
        const variants = (this.session.plan || [])
            .map(p => `${p.condition}:${Object.values(p.variants || {}).join("/")}`).join(" ");
        fs.appendFileSync(file, [
            new Date().toISOString(), this.session.participantId, this.session.condition,
            this.session.block, order, csvEscape(variants)
        ].join(",") + "\n");
    }

    /** Saves a completed questionnaire to <participant>_questionnaire.csv. */
    saveQuestionnaire(payload) {
        const pid = payload.participantId || this.session.participantId || "unknown";
        const file = path.join(LOG_DIR, `${pid}_questionnaire.csv`);
        const answers = payload.answers || {};
        const keys = Object.keys(answers);
        const isNew = !fs.existsSync(file);
        if (isNew) {
            fs.writeFileSync(file,
                "timestamp,participantId,condition,questionnaire," + keys.join(",") + "\n");
        }
        const row = [
            new Date().toISOString(),
            pid,
            payload.condition || this.session.condition || "",
            payload.questionnaire || "post",
            ...keys.map(k => csvEscape(String(answers[k])))
        ].join(",");
        fs.appendFileSync(file, row + "\n");
        return file;
    }

    registerComponents() {
        this.components.audioReceiver = new MessageReader(this.scene, STT_NETWORK_ID);
        this.components.transcriptionService = new SpeechToTextService(this.scene, nconf.get());
    }

    definePipeline() {
        // Step 1: forward audio chunks to STT so the participant's transcript
        // is still generated (the researcher sees it in the console and on
        // the control endpoint).
        this.components.audioReceiver.on("data", (data) => {
            const peerUUID = data.message.subarray(0, 36).toString();
            const chunk    = Buffer.from(data.message.subarray(36));

            if (chunk.length <= 64) {
                const ctrl = chunk.toString("utf8");
                if (ctrl.startsWith(STT_CONTROL_PREFIX)) {
                    const action = ctrl.slice(STT_CONTROL_PREFIX.length);
                    if (action === "start") this.components.transcriptionService.recordingStart(peerUUID);
                    else if (action === "stop") this.components.transcriptionService.recordingStop(peerUUID);
                    return;
                }
                // Mic health from the headset — surfaced on the control panel so a
                // dead microphone is obvious before a participant is mid-session.
                // Must return, or this text would be fed to STT as audio.
                if (ctrl.startsWith(MIC_STATUS_PREFIX)) {
                    const f = ctrl.slice(MIC_STATUS_PREFIX.length).split("|");
                    this.micStatus = {
                        devices:   parseInt((f[0] || "d0").slice(1), 10) || 0,
                        live:      (f[1] || "l0").slice(1) === "1",
                        recording: (f[2] || "r0").slice(1) === "1",
                        level:     parseFloat((f[3] || "v0").slice(1)) || 0,
                        at:        Date.now()
                    };
                    return;
                }
            }
            this.components.transcriptionService.addAudioChunk(peerUUID, chunk);
        });

        // Step 2: log transcript for researcher (do NOT auto-send to LLM) and
        // send it straight to the Unity client so the participant sees what the
        // system heard immediately (not at the end of the pipeline).
        this.components.transcriptionService.on("response", (data) => {
            const text = data.toString().replace(/(\r\n|\n|\r)/gm, "").replace(/^>/, "").trim();
            if (text.length < 5) return;
            this.lastTranscript = text;
            this.transcriptHistory.push({ at: new Date().toISOString(), text });
            if (this.transcriptHistory.length > 50) this.transcriptHistory.shift();

            // First utterance of a trial starts its clock, so later timings are
            // measured from when the participant actually began.
            const key = this.trialKey();
            if (!this.trialStartedAt[key]) this.trialStartedAt[key] = Date.now();

            // Attempt N here means "input given after N injects", which is what
            // distinguishes the initial request from a post-feedback recovery.
            this.logEvent("transcript", text, {
                msSinceTrialStart: Date.now() - this.trialStartedAt[key]
            });
            console.log(`\x1b[36m[Transcript]\x1b[0m "${text}"  →  waiting for researcher to inject response`);

            // Show in VR via TranscriptionCollector (network ID 98)
            this.scene.send(new NetworkId(STT_NETWORK_ID), {
                type: "Transcript",
                peer: "server",
                data: text
            });
        });

        // Step 3: start the researcher control HTTP server.
        this.startControlServer();
    }

    /** Destroys everything the study created so the next participant / the real
     *  session starts from a clean scene. Runs as an injected one-shot script. */
    resetScene() {
        console.log(`\x1b[33m[WoZ Reset]\x1b[0m clearing created objects`);
        this.logEvent("reset", "clear-scene");
        this.sendControl("clear");
        return { ok: true, reset: true };
    }

    /**
     * Injects an outcome for a task.
     *
     * outcome = "error"   → the variant's planned failure (attempt 1, and again
     *                       whenever the participant repeats the same omission)
     * outcome = "success" → the corrected result, once they supply what was missing
     *
     * The headset receives a JSON spec and performs it with compiled code, so
     * content can be edited here without rebuilding the APK.
     */
    injectResponse(taskKey, outcome, variantKey) {
        const task = TASKS[taskKey];
        if (!task) return { ok: false, error: `Unknown task: ${taskKey}` };

        const vKey = variantKey || this.currentVariant(taskKey);
        const variant = task.variants[vKey];
        if (!variant) return { ok: false, error: `Unknown variant: ${vKey} for ${taskKey}` };

        const spec = outcome === "success" ? variant.success : variant.error;
        if (!spec) return { ok: false, error: `Unknown outcome: ${outcome}` };

        // Attempt 1 is the injected error; each further inject is a recovery attempt.
        const key = this.trialKey(taskKey);
        this.attempts[key] = (this.attempts[key] || 0) + 1;
        const attempt = this.attempts[key];

        const started = this.trialStartedAt[key];
        const msSinceTrialStart = started ? Date.now() - started : "";

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m ${taskKey}/${vKey} ${outcome} (attempt ${attempt})`);
        this.logEvent("inject", `${vKey}/${outcome}`, {
            task: taskKey, variant: vKey, attempt,
            errorType: outcome === "success" ? "" : (variant.missing || "error"),
            msSinceTrialStart
        });
        // What the participant was actually shown — needed to interpret their next input.
        this.logEvent("feedback-shown", spec.errorText || spec.label || "", {
            task: taskKey, variant: vKey, attempt,
            errorType: outcome === "success" ? "" : (variant.missing || "error")
        });

        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome",
            peer: "WizardOfOz",
            data: JSON.stringify(spec)
        });

        return {
            ok: true, task: taskKey, variant: vKey, outcome, attempt,
            missing: variant.missing || ""
        };
    }

    /** Sends a control action (condition switch, mic override, clear) to the headset. */
    sendControl(action, value = "") {
        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome", peer: "WizardOfOz",
            data: JSON.stringify({ action, value })
        });
    }

    startControlServer() {
        const server = http.createServer((req, res) => {
            const send = (status, body) => {
                res.writeHead(status, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
                res.end(JSON.stringify(body));
            };

            const url = req.url.split("?")[0];

            // ── Static pages ──────────────────────────────────────────────────
            if (req.method === "GET" && (url === "/" || url === "/control")) {
                return serveFile(res, path.join(PUBLIC_DIR, "control.html"), "text/html");
            }
            if (req.method === "GET" && url === "/questionnaire") {
                return serveFile(res, path.join(PUBLIC_DIR, "questionnaire.html"), "text/html");
            }

            // ── Read endpoints ────────────────────────────────────────────────
            if (req.method === "GET" && url === "/status") {
                // Mic is considered stale if the headset hasn't reported in 5s
                // (app closed, headset asleep, or network dropped).
                const mic = this.micStatus
                    ? { ...this.micStatus, stale: Date.now() - this.micStatus.at > 5000 }
                    : null;
                return send(200, {
                    session: this.session,
                    mic,
                    lastTranscript: this.lastTranscript,
                    transcriptHistory: this.transcriptHistory.slice(-8),
                    activeTask: this.activeTask,
                    activeVariant: this.currentVariant(),
                    attempt: this.attemptCount(),
                    availableTasks: Object.keys(TASKS).map(k => ({ key: k, name: TASKS[k].name }))
                });
            }

            if (req.method === "GET" && url === "/tasks") {
                return send(200, Object.entries(TASKS).map(([k, v]) => ({
                    key: k, name: v.name,
                    variants: Object.entries(v.variants).map(([vk, vv]) => ({
                        key: vk, label: vv.label, prompt: vv.prompt, missing: vv.missing
                    }))
                })));
            }

            // The counterbalanced plan for a participant, so the researcher can
            // see which condition/variants to run before starting.
            if (req.method === "GET" && url === "/plan") {
                const pid = (req.url.split("?")[1] || "").replace(/^pid=/, "") ||
                            this.session.participantId;
                return send(200, { participantId: pid, plan: planForParticipant(pid) });
            }

            // ── Write endpoints ───────────────────────────────────────────────
            let body = "";
            req.on("data", d => (body += d));
            req.on("end", () => {
                try {
                    const payload = body ? JSON.parse(body) : {};

                    if (req.method === "POST" && url === "/session") {
                        const pid = String(payload.participantId || "").trim();
                        const newParticipant = pid !== this.session.participantId;
                        this.session.participantId = pid;
                        this.session.plan = planForParticipant(pid);

                        // Condition defaults to whatever the counterbalance plan
                        // says for this block, but the researcher can override.
                        this.session.block = Number(payload.block) || 1;
                        const planned = this.session.plan[this.session.block - 1];
                        this.session.condition = String(
                            payload.condition || (planned && planned.condition) || "A"
                        ).trim().toUpperCase();
                        this.session.startedAt = new Date().toISOString();

                        // Attempts are per (condition, task); starting a new
                        // participant clears them entirely.
                        if (newParticipant) { this.attempts = {}; this.trialStartedAt = {}; }

                        this.logSessionStart();
                        console.log(`\x1b[35m[Session]\x1b[0m participant=${pid} ` +
                            `block=${this.session.block} condition=${this.session.condition} ` +
                            `order=${this.session.plan.map(p => p.condition).join("-")}`);

                        if (["A", "B", "C"].includes(this.session.condition)) {
                            this.sendControl("condition", this.session.condition);
                        }
                        return send(200, { ok: true, session: this.session });
                    }

                    if (req.method === "POST" && url === "/task") {
                        const key = `task${payload.task}`;
                        if (!TASKS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        // A trial's clock starts when the researcher selects the task.
                        this.trialStartedAt[this.trialKey(key)] = Date.now();
                        this.logEvent("task-change", key, { task: key, attempt: 0 });
                        return send(200, {
                            activeTask: this.activeTask,
                            variant: this.currentVariant(),
                            attempt: this.attemptCount()
                        });
                    }

                    if (req.method === "POST" && url === "/inject") {
                        const taskKey = payload.task ? `task${payload.task}` : this.activeTask;
                        // "error" replays the planned failure (use again when the
                        // participant repeats the same omission); "success" resolves it.
                        const outcome = payload.outcome || payload.response || "error";
                        return send(200, this.injectResponse(taskKey, outcome, payload.variant));
                    }

                    if (req.method === "POST" && url === "/reset") {
                        return send(200, this.resetScene());
                    }

                    // Researcher fallback for push-to-talk: hold recording open
                    // from the panel when the controller trigger isn't usable.
                    if (req.method === "POST" && url === "/record") {
                        const on = !!payload.recording;
                        this.sendControl("mic", on ? "start" : "stop");
                        this.logEvent("remote-record", on ? "start" : "stop");
                        return send(200, { ok: true, recording: on });
                    }

                    if (req.method === "POST" && url === "/event") {
                        this.logEvent(payload.type || "note", payload.detail || "");
                        return send(200, { ok: true });
                    }

                    if (req.method === "POST" && url === "/questionnaire") {
                        const file = this.saveQuestionnaire(payload);
                        console.log(`\x1b[35m[Questionnaire]\x1b[0m saved → ${path.basename(file)}`);
                        return send(200, { ok: true, saved: path.basename(file) });
                    }

                    send(404, { error: "Not found" });
                } catch (e) {
                    send(400, { error: e.message });
                }
            });
        });

        server.listen(this.controlPort, () => {
            console.log("");
            console.log(`\x1b[1m\x1b[32m╔══════════════════════════════════════════════════════════════╗\x1b[0m`);
            console.log(`\x1b[1m\x1b[32m║  WIZARD-OF-OZ STUDY SERVER READY                               ║\x1b[0m`);
            console.log(`\x1b[1m\x1b[32m╚══════════════════════════════════════════════════════════════╝\x1b[0m`);
            console.log(`\x1b[1m  Researcher panel:  \x1b[4mhttp://localhost:${this.controlPort}\x1b[0m`);
            console.log(`\x1b[1m  Questionnaire:     \x1b[4mhttp://localhost:${this.controlPort}/questionnaire\x1b[0m`);
            console.log(`  Study results saved to: ${LOG_DIR}`);
            console.log("");
        });
    }
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function csvEscape(value) {
    const s = String(value == null ? "" : value);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function serveFile(res, filePath, contentType) {
    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(404, { "Content-Type": "text/plain" });
            res.end("Not found: " + path.basename(filePath));
            return;
        }
        res.writeHead(200, { "Content-Type": contentType });
        res.end(data);
    });
}

module.exports = { WizardOfOzApp };

if (require.main === module) {
    const app = new WizardOfOzApp();
    app.start();
}
