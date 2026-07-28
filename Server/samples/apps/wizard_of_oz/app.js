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

// ── Study content: 3 tasks × 4 AI-pipeline error categories ──────────────────
//
// DESIGN (per implementation note agreed with Daniele, July 2026)
// Within-subjects: every participant does all three conditions, and a DIFFERENT
// task in each. Because no task is ever repeated within a participant, tasks no
// longer need equivalent "variants" — the task rotation itself is what stops a
// correction learned in condition 1 from carrying into conditions 2 and 3.
//
// Each task instead carries the FOUR AI-pipeline error categories, so the same
// task can fail in four distinguishable ways and the Wizard chooses which one to
// inject. The categories follow the stages of the speech→action pipeline the
// participant believes is running:
//
//   speech    — speech recognition mis-heard the words
//   intent    — the words were right, the interpreted action was wrong
//   parameter — the action was right, but a needed detail was missing/ambiguous
//   execution — the action was right and understood, but performed incorrectly
//
// TRIAL PROTOCOL
//   attempt 1 → Wizard injects the selected error category
//   feedback  → per condition (A: none, B: text panel only, C: agent only)
//   attempt 2 → if the participant repairs the problem, inject SUCCESS;
//               if they repeat the same mistake, inject the SAME error again.
//
// Everything here is data, not code: the headset runs these specs directly, so
// tasks/wording/dialogue can be edited WITHOUT rebuilding and redeploying the
// Quest APK. Edit freely, then restart the server.

const ERROR_CATEGORIES = [
    { key: "speech",    name: "Speech recognition",   short: "Mis-heard the words" },
    { key: "intent",    name: "Intent interpretation", short: "Wrong action inferred" },
    { key: "parameter", name: "Missing parameter",     short: "Needed detail absent" },
    { key: "execution", name: "Execution",             short: "Performed incorrectly" }
];

const TASKS = {
    task1: {
        name: "Create an object",
        prompt: "Ask the system to create a ball in front of you.",
        errors: {
            speech: {
                action: "spawn", shape: "cube", pos: "hand", physics: false,
                scaleX: 1.2, scaleY: 0.9, scaleZ: 0.08,
                label: "Created a wall",
                errorText: "A wall was created. The speech was heard as \"wall\" rather than \"ball\".",
                agentPre: "Okay, creating that for you.",
                agentPost: "I've made a wall — I heard \"wall\" rather than \"ball\". Could you say that again?"
            },
            intent: {
                action: "spawn", shape: "sphere", pos: "hand", physics: true, count: 5,
                scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
                label: "Created balls",
                errorText: "Five balls were created instead of one — the request was taken as a request for several.",
                agentPre: "Sure, I'll create that now.",
                agentPost: "I've made five balls — I took that as a request for several. Did you want just one?"
            },
            parameter: {
                action: "spawn", shape: "sphere", pos: "origin", physics: true,
                scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
                label: "Created a ball",
                errorText: "A ball was created, but at the centre of the room rather than near you — no position was specified.",
                agentPre: "Okay, I'll create a ball for you.",
                agentPost: "I made the ball, but I wasn't told where to put it, so it went to the centre of the room. Where would you like it?"
            },
            execution: {
                action: "spawn", shape: "sphere", pos: "high", physics: true, useCollider: false,
                scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
                label: "Created a ball",
                errorText: "The ball was created in the right place but fell straight through the floor — it was made without a solid surface.",
                agentPre: "Alright, making a ball in front of you.",
                agentPost: "The ball was created, but it dropped through the floor — it came out without a solid surface. Shall I try again?"
            }
        },
        success: {
            action: "spawn", shape: "sphere", pos: "hand", physics: true,
            scaleX: 0.15, scaleY: 0.15, scaleZ: 0.15,
            label: "Created a ball in front of you",
            agentPre: "Alright, creating a ball right in front of you.",
            agentPost: "There you go — a ball just in front of you."
        }
    },

    task2: {
        name: "Change an object's appearance",
        prompt: "Ask the system to make the object green.",
        errors: {
            speech: {
                action: "recolor", color: "#efe3c0",
                label: "Applied cream",
                errorText: "The object was coloured cream. The speech was heard as \"cream\" rather than \"green\".",
                agentPre: "Okay, applying that colour.",
                agentPost: "I've made it cream — I heard \"cream\" rather than \"green\". Could you repeat that?"
            },
            intent: {
                action: "recolor", color: "#2ecc40", applyToAll: true,
                label: "Applied green",
                errorText: "Everything in the scene turned green — the colour was applied to the whole scene rather than to one object.",
                agentPre: "You'd like something green — let me apply that.",
                agentPost: "I turned everything green because I wasn't sure which object you meant. Which one should it be?"
            },
            parameter: {
                action: "recolor", color: "#00b3b3",
                label: "Applied a green",
                errorText: "The object was coloured, but it came out teal — no particular shade of green was specified.",
                agentPre: "Sure, making it green.",
                agentPost: "It's coloured, but the shade came out teal. No particular green was specified — which shade did you want?"
            },
            execution: {
                action: "recolor", color: "#2ecc40", revert: true,
                label: "Applied green",
                errorText: "The colour was applied but did not persist — it reverted to the original after a moment.",
                agentPre: "Applying green now.",
                agentPost: "It turned green briefly and then reverted — the change didn't stick. Shall I try again?"
            }
        },
        success: {
            action: "recolor", color: "#2ecc40",
            label: "Coloured the object green",
            agentPre: "Okay, colouring that object green.",
            agentPost: "Done — that object is now green."
        }
    },

    task3: {
        name: "Make an object move",
        prompt: "Ask the system to make the object circle slowly around the cube.",
        errors: {
            speech: {
                action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 60, drift: true,
                label: "Sent the object away",
                errorText: "The object drifted off instead of circling. The speech was heard as \"cross\" rather than \"circle\".",
                agentPre: "Okay, setting that motion up.",
                agentPost: "It's moved away rather than circling — I heard \"cross\" rather than \"circle\". Could you say that again?"
            },
            intent: {
                action: "orbit", orbitTarget: "origin", orbitAxis: "up", orbitSpeed: 60,
                label: "Started an orbit",
                errorText: "The object is circling the centre of the room rather than the cube — the wrong thing was taken as the centre.",
                agentPre: "Okay, I'll set it circling.",
                agentPost: "It's circling, but around the centre of the room rather than the cube. What should it circle around?"
            },
            parameter: {
                action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 900,
                label: "Started an orbit",
                errorText: "The object is circling far too fast to follow — no speed was specified.",
                agentPre: "Okay, making it circle the cube.",
                agentPost: "It's circling, but very fast — no speed was given. How fast should it go?"
            },
            execution: {
                action: "orbit", orbitTarget: "cube", orbitAxis: "forward", orbitSpeed: 60,
                label: "Started an orbit",
                errorText: "The object is circling the cube, but on a vertical plane instead of flat around it.",
                agentPre: "Alright, setting up the circling motion.",
                agentPost: "It's circling the cube, but vertically rather than flat around it. Shall I correct the plane?"
            }
        },
        success: {
            action: "orbit", orbitTarget: "cube", orbitAxis: "up", orbitSpeed: 25,
            label: "Object orbits the cube slowly",
            agentPre: "Setting a slow, steady circle around the cube.",
            agentPost: "There — a slow, steady orbit around the cube."
        }
    }
};

// ── Counterbalancing ─────────────────────────────────────────────────────────
// Within-subjects with task rotation: each participant is assigned one of the
// six condition orders AND one of three task rotations. Rotating the task order
// independently of the condition order matters — if task order were fixed, then
// "Task 1" would always be the participant's first (least practised) trial and
// task would be confounded with practice.
//
// Across 6 participants every condition order is used once; across any 3
// consecutive participants each task appears in each ordinal position.

const CONDITION_ORDERS = [
    ["A", "B", "C"], ["A", "C", "B"],
    ["B", "A", "C"], ["B", "C", "A"],
    ["C", "A", "B"], ["C", "B", "A"]
];

const TASK_KEYS = ["task1", "task2", "task3"];

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
 * single task is run in that block. One task per condition, never repeated.
 *
 * The task rotation advances only once the six condition orders are exhausted.
 * Deriving both from n directly (n%6 and n%3) would NOT work: n%3 is fully
 * determined by n%6, which locks each condition to the same tasks forever — in
 * testing that left condition C never paired with task 1. Stepping the task
 * offset every 6 participants crosses the two factors instead, so all nine
 * (condition, task) cells are used.
 */
function planForParticipant(pid) {
    const n = participantIndex(pid);
    const order = CONDITION_ORDERS[n % CONDITION_ORDERS.length];
    const taskOffset = Math.floor(n / CONDITION_ORDERS.length) % TASK_KEYS.length;
    return order.map((condition, block) => {
        const task = TASK_KEYS[(block + taskOffset) % TASK_KEYS.length];
        return {
            block: block + 1,
            condition,
            task,
            taskName: TASKS[task].name
        };
    });
}

// ── Application ───────────────────────────────────────────────────────────────

class WizardOfOzApp extends ApplicationController {
    constructor(configFile = "config.json") {
        super(configFile);
        this.lastTranscript  = "";
        this.transcriptHistory = [];
        this.activeTask      = "task1";
        this.activeError     = "parameter";   // error category armed for the next inject
        this.controlPort     = nconf.get("wizardControlPort") || 8181;
        this.micStatus       = null;

        // Session state (set by the researcher on the web panel before each run)
        this.session = {
            participantId: "",
            condition:     "",   // "A" | "B" | "C" — the CURRENT block's condition
            block:         1,    // which of the 3 condition blocks we're in
            startedAt:     null,
            plan:          []    // counterbalanced condition/task plan
        };

        // The active trial. A trial = one (condition, task) pair run start-to-finish.
        // Kept as a single object rather than keyed maps because the new protocol
        // runs exactly one task per condition, so only one trial is ever live.
        this.trial = null;
        this.trialCounter = 0;   // 1..N within the current participant

        fs.mkdirSync(LOG_DIR, { recursive: true });
    }

    // ── Trial lifecycle ──────────────────────────────────────────────────────

    /** The block entry the counterbalance plan assigns to the current block. */
    plannedBlock(block = this.session.block) {
        return (this.session.plan || [])[block - 1] || null;
    }

    /** Task assigned to the current block (the Wizard may override it). */
    plannedTask(block = this.session.block) {
        const b = this.plannedBlock(block);
        return b ? b.task : "task1";
    }

    attemptCount() {
        return this.trial ? this.trial.attempts : 0;
    }

    /**
     * Begins a trial: clears the scene, arms an error category, and starts the
     * clock. Every trial starts from an identical scene so a participant never
     * inherits objects or colours from the previous condition.
     */
    startTrial(taskKey, errorCategory) {
        // An abandoned trial still gets a row — silently dropping it would make
        // the trial count disagree with the number of conditions run.
        if (this.trial && !this.trial.endedAt) this.completeTrial("abandoned");

        const task = taskKey || this.plannedTask();
        if (!TASKS[task]) return { ok: false, error: "Unknown task: " + task };
        const category = errorCategory || this.activeError;

        this.activeTask  = task;
        this.activeError = category;
        this.trialCounter += 1;

        this.resetScene();

        this.trial = {
            number:        this.trialCounter,
            block:         this.session.block,
            condition:     this.session.condition,
            task,
            errorCategory: category,
            startedAt:     Date.now(),
            startedAtIso:  new Date().toISOString(),
            endedAt:       null,
            status:        "in-progress",
            attempts:      0,   // participant utterances
            injects:       0    // wizard injections
        };

        this.logEvent("trial-start", `${task}/${category}`, { task, errorCategory: category, attempt: 0 });
        console.log(`\x1b[35m[Trial ${this.trialCounter}]\x1b[0m start ` +
            `condition=${this.session.condition} task=${task} error=${category}`);
        return { ok: true, trial: this.trial };
    }

    /**
     * Ends the active trial and writes its summary row.
     * status: "completed" (participant recovered) | "failed" | "abandoned"
     */
    completeTrial(status = "completed") {
        if (!this.trial) return { ok: false, error: "No active trial" };
        if (this.trial.endedAt) return { ok: true, trial: this.trial };

        this.trial.endedAt = Date.now();
        this.trial.endedAtIso = new Date().toISOString();
        this.trial.status = status;
        this.logTrial(this.trial);
        this.logEvent("trial-end", status, {
            task: this.trial.task,
            errorCategory: this.trial.errorCategory,
            attempt: this.trial.attempts,
            msSinceTrialStart: this.trial.endedAt - this.trial.startedAt
        });
        console.log(`\x1b[35m[Trial ${this.trial.number}]\x1b[0m ${status} ` +
            `attempts=${this.trial.attempts} ` +
            `duration=${Math.round((this.trial.endedAt - this.trial.startedAt) / 1000)}s`);
        return { ok: true, trial: this.trial };
    }

    /**
     * Finishes the current condition and moves to the next assigned one: applies
     * the next block's condition on the headset, loads its assigned task and
     * clears the scene. This is the "after each condition" automation — the
     * Wizard presses one button rather than remembering four steps.
     */
    advanceBlock() {
        if (this.trial && !this.trial.endedAt) this.completeTrial("completed");

        const next = this.session.block + 1;
        if (next > (this.session.plan || []).length) {
            this.resetScene();
            return { ok: true, finished: true, session: this.session };
        }

        this.session.block = next;
        const planned = this.plannedBlock(next);
        this.session.condition = planned.condition;
        this.activeTask = planned.task;

        this.sendControl("condition", this.session.condition);
        this.resetScene();
        this.logSessionStart();
        this.logEvent("block-advance", `block ${next} → ${planned.condition}/${planned.task}`,
            { task: planned.task, attempt: 0 });
        console.log(`\x1b[35m[Block ${next}]\x1b[0m condition=${planned.condition} task=${planned.task}`);

        return { ok: true, finished: false, session: this.session, planned };
    }

    // ── Data logging ─────────────────────────────────────────────────────────

    /**
     * One row per event, with the full trial context the analysis needs:
     * participant, condition, block, task, error category, trial, attempt.
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
            this.trial ? this.trial.number : "",
            taskKey,
            extra.errorCategory !== undefined ? extra.errorCategory
                : (this.trial ? this.trial.errorCategory : ""),
            extra.attempt !== undefined ? extra.attempt : this.attemptCount(),
            type,
            extra.msSinceTrialStart !== undefined ? extra.msSinceTrialStart : "",
            csvEscape(detail)
        ].join(",");
        if (isNew) {
            fs.writeFileSync(file,
                "timestamp,participantId,condition,block,trial,task,errorCategory,attempt," +
                "eventType,msSinceTrialStart,detail\n");
        }
        fs.appendFileSync(file, row + "\n");
    }

    /**
     * One row per completed trial — the primary analysis unit. Carries every
     * field the implementation note asks for, so per-condition comparisons need
     * no joining against the event log.
     */
    logTrial(trial) {
        const file = path.join(LOG_DIR, "trials.csv");
        const isNew = !fs.existsSync(file);
        if (isNew) fs.writeFileSync(file,
            "participantId,conditionOrder,block,condition,trial,task,errorCategory," +
            "startTime,endTime,durationMs,completionStatus,attempts,injects\n");
        const order = (this.session.plan || []).map(p => p.condition).join("-");
        fs.appendFileSync(file, [
            this.session.participantId,
            order,
            trial.block,
            trial.condition,
            trial.number,
            trial.task,
            trial.errorCategory,
            trial.startedAtIso,
            trial.endedAtIso || "",
            trial.endedAt ? trial.endedAt - trial.startedAt : "",
            trial.status,
            trial.attempts,
            trial.injects
        ].join(",") + "\n");
    }

    /** Records the session/block start row in the master sessions.csv. */
    logSessionStart() {
        const file = path.join(LOG_DIR, "sessions.csv");
        const isNew = !fs.existsSync(file);
        if (isNew) fs.writeFileSync(file,
            "timestamp,participantId,condition,block,conditionOrder,taskPlan\n");
        const order = (this.session.plan || []).map(p => p.condition).join("-");
        const tasks = (this.session.plan || [])
            .map(p => `${p.condition}:${p.task}`).join(" ");
        fs.appendFileSync(file, [
            new Date().toISOString(), this.session.participantId, this.session.condition,
            this.session.block, order, csvEscape(tasks)
        ].join(",") + "\n");
    }

    /**
     * Saves a questionnaire. Background and per-condition questionnaires have
     * different item sets, so they go to separate files — sharing one file would
     * leave the header matching only whichever was submitted first.
     *
     * Per-condition questionnaires are submitted three times per participant
     * (once per condition); the condition and block columns distinguish them.
     */
    saveQuestionnaire(payload) {
        const pid  = payload.participantId || this.session.participantId || "unknown";
        const type = payload.questionnaire === "background" ? "background" : "condition";
        const file = path.join(LOG_DIR, `${pid}_${type}.csv`);
        const answers = payload.answers || {};
        const keys = Object.keys(answers);
        const isNew = !fs.existsSync(file);
        if (isNew) {
            fs.writeFileSync(file,
                "timestamp,participantId,condition,block,questionnaire," + keys.join(",") + "\n");
        }
        const row = [
            new Date().toISOString(),
            pid,
            payload.condition || (type === "background" ? "" : this.session.condition || ""),
            payload.block || (type === "background" ? "" : this.session.block || ""),
            payload.questionnaire || type,
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

            // Each utterance inside a trial is one attempt — this is the
            // "number of attempts" the trial summary reports, and counting
            // participant speech (rather than wizard injections) is what makes
            // it a measure of the participant's recovery effort.
            if (this.trial && !this.trial.endedAt) this.trial.attempts += 1;

            this.logEvent("transcript", text, {
                msSinceTrialStart: this.trial ? Date.now() - this.trial.startedAt : ""
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
     * outcome = "error"   → the selected error category's failure. Re-injectable:
     *                       if the participant repeats the same mistake, send it
     *                       again rather than handing them the answer.
     * outcome = "success" → the corrected result, once they repair the problem.
     *
     * The headset receives a JSON spec and performs it with compiled code, so
     * content can be edited here without rebuilding the APK.
     */
    injectResponse(taskKey, outcome, errorCategory) {
        const task = TASKS[taskKey];
        if (!task) return { ok: false, error: `Unknown task: ${taskKey}` };

        const category = errorCategory ||
            (this.trial ? this.trial.errorCategory : this.activeError);

        let spec;
        if (outcome === "success") {
            spec = task.success;
        } else {
            spec = task.errors[category];
            if (!spec) return { ok: false, error: `Unknown error category: ${category}` };
        }
        if (!spec) return { ok: false, error: `Unknown outcome: ${outcome}` };

        if (this.trial && !this.trial.endedAt) this.trial.injects += 1;
        const msSinceTrialStart = this.trial ? Date.now() - this.trial.startedAt : "";

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m ${taskKey} ${outcome}` +
            (outcome === "success" ? "" : `/${category}`));
        this.logEvent("inject", outcome === "success" ? "success" : `error/${category}`, {
            task: taskKey,
            errorCategory: outcome === "success" ? "" : category,
            msSinceTrialStart
        });
        // What the participant was actually shown — needed to interpret their next input.
        this.logEvent("feedback-shown", spec.errorText || spec.label || "", {
            task: taskKey,
            errorCategory: outcome === "success" ? "" : category
        });

        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome",
            peer: "WizardOfOz",
            data: JSON.stringify(spec)
        });

        return {
            ok: true, task: taskKey, outcome,
            errorCategory: outcome === "success" ? "" : category,
            attempts: this.attemptCount(),
            injects: this.trial ? this.trial.injects : 0
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
            // Pre-session background questionnaire (once per participant).
            if (req.method === "GET" && url === "/background") {
                return serveFile(res, path.join(PUBLIC_DIR, "background.html"), "text/html");
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
                    activeError: this.activeError,
                    plannedTask: this.plannedTask(),
                    trial: this.trial,
                    attempt: this.attemptCount(),
                    errorCategories: ERROR_CATEGORIES,
                    availableTasks: Object.keys(TASKS).map(k => ({
                        key: k, name: TASKS[k].name, prompt: TASKS[k].prompt
                    }))
                });
            }

            if (req.method === "GET" && url === "/tasks") {
                return send(200, Object.entries(TASKS).map(([k, v]) => ({
                    key: k, name: v.name, prompt: v.prompt,
                    errors: Object.entries(v.errors).map(([ek, ev]) => ({
                        key: ek, label: ev.label, errorText: ev.errorText
                    }))
                })));
            }

            // The counterbalanced plan for a participant, so the researcher can
            // see which condition/task to run before starting.
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

                        // Trial numbering runs 1..N within a participant, so a
                        // new participant restarts the count.
                        if (newParticipant) { this.trial = null; this.trialCounter = 0; }

                        // Follow the plan's task for this block unless overridden.
                        this.activeTask = planned ? planned.task : this.activeTask;

                        this.logSessionStart();
                        console.log(`\x1b[35m[Session]\x1b[0m participant=${pid} ` +
                            `block=${this.session.block} condition=${this.session.condition} ` +
                            `order=${this.session.plan.map(p => p.condition).join("-")}`);

                        if (["A", "B", "C"].includes(this.session.condition)) {
                            this.sendControl("condition", this.session.condition);
                        }
                        return send(200, { ok: true, session: this.session });
                    }

                    // Selects the task to run. Does NOT start the trial — the
                    // Wizard sets task and error category first, then starts.
                    if (req.method === "POST" && url === "/task") {
                        const key = String(payload.task).startsWith("task")
                            ? String(payload.task) : `task${payload.task}`;
                        if (!TASKS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        this.logEvent("task-change", key, { task: key, attempt: 0 });
                        return send(200, { activeTask: this.activeTask, plannedTask: this.plannedTask() });
                    }

                    // Arms which of the four AI-pipeline error categories the
                    // next inject will use.
                    if (req.method === "POST" && url === "/error-category") {
                        const cat = String(payload.category || "");
                        if (!ERROR_CATEGORIES.some(c => c.key === cat)) {
                            return send(400, { error: "Unknown error category: " + cat });
                        }
                        this.activeError = cat;
                        if (this.trial && !this.trial.endedAt) this.trial.errorCategory = cat;
                        return send(200, { ok: true, activeError: cat });
                    }

                    // Start trial: resets the scene, starts the clock, fixes the
                    // (task, error category) pair for this trial.
                    if (req.method === "POST" && url === "/trial/start") {
                        const key = payload.task
                            ? (String(payload.task).startsWith("task") ? String(payload.task) : `task${payload.task}`)
                            : null;
                        return send(200, this.startTrial(key, payload.category));
                    }

                    // End trial and write its summary row.
                    if (req.method === "POST" && url === "/trial/complete") {
                        return send(200, this.completeTrial(payload.status || "completed"));
                    }

                    // Finish this condition and load the next assigned one.
                    if (req.method === "POST" && url === "/next-block") {
                        return send(200, this.advanceBlock());
                    }

                    if (req.method === "POST" && url === "/inject") {
                        const taskKey = payload.task
                            ? (String(payload.task).startsWith("task") ? String(payload.task) : `task${payload.task}`)
                            : this.activeTask;
                        // "error" replays the selected category (use again when the
                        // participant repeats the same mistake); "success" resolves it.
                        const outcome = payload.outcome || payload.response || "error";
                        return send(200, this.injectResponse(taskKey, outcome, payload.category));
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

module.exports = { WizardOfOzApp, TASKS, ERROR_CATEGORIES, CONDITION_ORDERS, planForParticipant };

if (require.main === module) {
    const app = new WizardOfOzApp();
    app.start();
}
