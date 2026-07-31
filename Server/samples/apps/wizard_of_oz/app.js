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

// ── Study content: 3 tasks × 3 variants × 4 error types ──────────────────────
//
// ROOT-CAUSE ATTRIBUTION (agreed with supervisor)
// Every error must be a plausible consequence of something ambiguous, missing or
// underspecified in the participant's OWN instruction — never an arbitrary system
// glitch. The scene stays coherent at first; only through further interaction does
// the participant realise the ambiguity was on their end. All wording below is
// written to that principle: the feedback names the missing detail, it does not
// announce a malfunction.
//
// The scene is the DreamCodeVR campfire scene: a sphere, a cube and a campfire.
// StudyOutcomes rebuilds the sphere and cube on every trial start so each trial
// begins from an identical arrangement.
//
// Errors are pre-scripted and injected by the Wizard REGARDLESS of what the
// participant actually says — the participant is never told what to say.
//
// Everything here is data, not code: the headset runs these specs directly, so
// wording and dialogue can be edited WITHOUT rebuilding the APK. Restart the
// server to pick up changes.

/**
 * Does a repair utterance supply the detail the error was about?
 *
 * Accepts a set of equivalent terms rather than one keyword, and matches on
 * word boundaries. Plain substring matching fails both ways here: "one" hits
 * "stone" and "someone", while a perfectly good repair phrased as "in my palm"
 * or "beside the campfire" misses "hand" and "next to" entirely. This measure
 * backs the training-signal claim, so its false-positive and false-negative
 * rates are the study's, not an implementation detail.
 */
function slotMatched(text, terms) {
    if (!terms || !terms.length) return null;   // null = not scoreable
    const haystack = String(text || "").toLowerCase();
    return terms.some(term => {
        const s = String(term).toLowerCase().trim();
        if (!s) return false;
        const escaped = s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
        return new RegExp(`(^|\\W)${escaped}(\\W|$)`).test(haystack);
    });
}

// ── Study content: four scenario types ───────────────────────────────
//
// Each task IS an error scenario. The set is deliberately split so that only
// half the failures are the participant's doing:
//
//   Task 1  user error            they omitted something required
//   Task 2  user error            their phrasing allowed another reading
//   Task 3  system limitation     the request was valid; the system cannot do it
//   Task 4  system behaviour      it executed, but the result is not where
//                                 they would look for it
//
// That split is the study. Tasks 3 and 4 are the interesting half: a memory
// ceiling is nobody's fault and CANNOT be fixed by rephrasing, so a participant
// who blames themselves there will burn attempts rewording a request that can
// never succeed. Feedback wording must therefore never imply user fault on
// tasks 3 and 4, or it manufactures exactly the mis-attribution we measure.
//
// Errors are pre-scripted and injected by the Wizard REGARDLESS of what the
// participant says. Participants are never told what to say.
//
// This is data, not code: the headset executes these specs, so wording can be
// edited without rebuilding the APK. Restart the server to pick changes up.

/** Task 1 — user error: a required detail was never given. */
function buildTask1(v) {
    const o = v.object;
    return {
        label: `Create a ${o} in your hand`,
        scenario: "user_error",
        prompt: `In this scene you can see a sphere, a cube and a campfire. Ask the ` +
                `system to create a ${o} that appears in your hand when you raise it. ` +
                `Use your own words, as if talking to someone.`,
        error: {
            action: "noop",
            errorText: `The ${o} was not created. No hand height was given, so there ` +
                       `was nothing to trigger on.`,
            agentPost: `I wasn't told how high your hand needed to be, so I didn't ` +
                       `know when to create the ${o}.`,
            missingSlot: "hand height",
            slotTerms: ["height", "high", "above", "shoulder", "chest", "eye level",
                        "level", "raise", "raised", "up", "upward", "lift"]
        },
        success: {
            action: "spawn", shape: v.shape, pos: "hand",
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color
        }
    };
}

/** Task 2 — user error: the phrasing allowed more than one reading. */
function buildTask2(v) {
    const m = v.mover, t = v.target;
    return {
        label: `Move the ${m} to the ${t}`,
        scenario: "user_error",
        prompt: `Ask the system to move the ${m} so that it ends up next to the ${t}. ` +
                `Use your own words.`,
        error: {
            action: "move", target: m, moveTo: t, away: true,
            errorText: `The ${m} moved, but no direction or distance was specified, ` +
                       `so it went the other way.`,
            agentPost: `The ${m} moved, but I wasn't sure which side of the ${t} ` +
                       `you meant.`,
            missingSlot: `next to the ${t}`,
            slotTerms: [t, "next to", "beside", "near", "close", "closer", "toward",
                        "towards", "adjacent", "by the", "against", "touching",
                        "alongside", "right of", "left of", "in front"]
        },
        success: { action: "move", target: m, moveTo: t }
    };
}

/** Task 3 — system limitation: valid request, cannot be executed. */
function buildTask3(v) {
    const n = v.count, o = v.object;
    return {
        label: `Create ${n} ${o}s`,
        scenario: "system_limit",
        prompt: `Ask the system to fill the area around the campfire with about ` +
                `${n} ${o}s. Use your own words.`,
        // Nothing is wrong with what they said. The feedback must own the limit
        // and offer a workable number, never suggest they rephrase.
        error: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true, count: 8,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color,
            errorText: `${n} ${o}s is beyond what this system can render. I created ` +
                       `8 instead. A few dozen is the practical limit here.`,
            agentPost: `Your request was clear, but ${n} ${o}s is more than I can ` +
                       `handle at once. I have made 8. Would a smaller number work?`,
            missingSlot: "a smaller number",
            slotTerms: ["fewer", "less", "smaller", "reduce", "ten", "twenty", "fifty",
                        "hundred", "10", "20", "30", "50", "100", "few", "couple",
                        "some", "instead", "ok", "okay", "fine", "that works"]
        },
        success: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true, count: 8,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color
        }
    };
}

/** Task 4 — system behaviour: executed correctly, but out of view. */
function buildTask4(v) {
    const o = v.object;
    return {
        label: `Create a ${o} above the campfire`,
        scenario: "system_behaviour",
        prompt: `Ask the system to create a large ${o} somewhere above the campfire. ` +
                `Use your own words.`,
        // The command succeeded. The only problem is where it landed, which the
        // participant could not have predicted. Feedback locates it; it does not
        // ask them to rephrase.
        error: {
            action: "spawn", shape: v.shape, pos: "behind", physics: false,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color,
            errorText: `The ${o} was created successfully, but it is behind you, ` +
                       `outside your view. Turn around to see it.`,
            agentPost: `I made the ${o}, but I placed it behind you rather than in ` +
                       `front. Turn around and you should see it.`,
            missingSlot: "turn around / in front",
            slotTerms: ["turn", "turned", "behind", "around", "front", "in front",
                        "see", "look", "where", "move it", "bring", "closer", "found"]
        },
        success: {
            action: "spawn", shape: v.shape, pos: "front", physics: false,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color
        }
    };
}

const TASKS = {
    task1: {
        name: "Create an object in your hand",
        scenario: "user_error",
        variants: {
            v1: buildTask1({ object: "ball",    shape: "sphere",  scale: 0.15, color: "" }),
            v2: buildTask1({ object: "cube",    shape: "cube",    scale: 0.15, color: "" }),
            v3: buildTask1({ object: "lantern", shape: "capsule", scale: 0.16, color: "#ffd66b" })
        }
    },
    task2: {
        name: "Move an object to a target",
        scenario: "user_error",
        variants: {
            v1: buildTask2({ mover: "sphere", target: "campfire" }),
            v2: buildTask2({ mover: "cube",   target: "campfire" }),
            v3: buildTask2({ mover: "sphere", target: "cube"     })
        }
    },
    task3: {
        name: "Create many objects (system limit)",
        scenario: "system_limit",
        variants: {
            v1: buildTask3({ count: 1000,  object: "stone", shape: "cube",   scale: 0.12, color: "#8d8d8d" }),
            v2: buildTask3({ count: 10000, object: "ball",  shape: "sphere", scale: 0.12, color: "" }),
            v3: buildTask3({ count: 5000,  object: "log",   shape: "capsule", scale: 0.14, color: "#7a5230" })
        }
    },
    task4: {
        name: "Create an object (lands out of view)",
        scenario: "system_behaviour",
        variants: {
            v1: buildTask4({ object: "moon",    shape: "sphere",  scale: 0.9, color: "#dfe6f2" }),
            v2: buildTask4({ object: "banner",  shape: "cube",    scale: 0.8, color: "#c0392b" }),
            v3: buildTask4({ object: "balloon", shape: "capsule", scale: 0.8, color: "#2ecc40" })
        }
    }
};

// Ground-truth attribution, fixed per task because task IS scenario type here.
// Tasks 1-2 are the participant's doing; tasks 3-4 are not.
const TASK_ATTRIBUTION = {
    task1: "self",
    task2: "self",
    task3: "system",
    task4: "system"
};

// ── Counterbalancing ─────────────────────────────────────────────────────────
//
// BETWEEN-SUBJECTS, 30 participants, 10 per feedback condition.
// Each participant experiences ONE condition and does all four tasks.
//
//   condition(p)   = CONDITIONS[(p-1) mod 3]   -> exactly 10 each over P1..P30
//   task order(p)  = balanced Latin square row (p-1) mod 4
//   variant(p,t)   = ((p-1) + t) mod 3
//
// Conditions are interleaved rather than blocked (A,B,C,A,B,C...) so that any
// drift over the recruitment period - the wizard getting smoother, seasonal
// participant differences - spreads across all three groups instead of loading
// onto whichever was run first.
//
// Task order uses a balanced Latin square, not a plain rotation: with four
// tasks it ensures each task appears in each position equally often AND each
// task precedes every other equally often, which a naive rotation does not do.
// That matters because tasks 3 and 4 teach participants the system has limits,
// which could otherwise colour how they read tasks 1 and 2.

const STUDY_DESIGN = "between";   // confirmed with supervisor, July 2026

const CONDITIONS = ["A", "B", "C"];
const TASK_KEYS    = ["task1", "task2", "task3", "task4"];
const VARIANT_KEYS = ["v1", "v2", "v3"];

const TARGET_N            = 30;
const PARTICIPANTS_PER_CONDITION = 10;

// Balanced (Williams) Latin square for 4 items.
const TASK_ORDERS = [
    [0, 1, 3, 2],
    [1, 2, 0, 3],
    [2, 3, 1, 0],
    [3, 0, 2, 1]
];

/** Participant number from an id (P01 -> 1, 7 -> 7). 1-based. */
function participantIndex(pid) {
    const digits = String(pid || "").match(/\d+/);
    if (digits) return parseInt(digits[0], 10);
    let h = 0;
    for (const ch of String(pid || "")) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return (h % TARGET_N) + 1;
}

/**
 * The plan for a participant: their single condition, plus the four tasks in
 * their assigned order with a content variant for each.
 */
function planForParticipant(pid) {
    const p = participantIndex(pid);
    const condition = CONDITIONS[(p - 1) % CONDITIONS.length];
    const order = TASK_ORDERS[(p - 1) % TASK_ORDERS.length];

    return order.map((taskIdx, position) => {
        const taskKey = TASK_KEYS[taskIdx];
        const variant = VARIANT_KEYS[((p - 1) + position) % VARIANT_KEYS.length];
        const task = TASKS[taskKey];
        return {
            block: position + 1,
            condition,
            task: taskKey,
            taskName: task.name,
            scenario: task.scenario,
            attribution: TASK_ATTRIBUTION[taskKey],
            variant,
            variantLabel: task.variants[variant].label
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
        this.activeVariant   = "v1";           // content variant, normally from the plan
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

    /** Content variant the master table assigns to a task for this participant. */
    plannedVariant(taskKey = this.activeTask) {
        const b = (this.session.plan || []).find(x => x.task === taskKey);
        return b ? b.variant : "v1";
    }

    /** The two error types the master table assigns to a task. */

    attemptCount() {
        return this.trial ? this.trial.attempts : 0;
    }

    /**
     * Begins a trial: clears the scene, arms an error type, and starts the
     * clock. Every trial starts from an identical scene so a participant never
     * inherits objects or colours from the previous trial.
     */
    startTrial(taskKey, variantKey) {
        // An abandoned trial still gets a row — silently dropping it would make
        // the trial count disagree with the number of trials run.
        if (this.trial && !this.trial.endedAt) this.completeTrial("abandoned");

        const task = taskKey || this.plannedTask();
        if (!TASKS[task]) return { ok: false, error: "Unknown task: " + task };

        // Default to what the master table assigns, so the Wizard only has to
        // override when deliberately going off-plan.
        const variant = variantKey || this.plannedVariant(task);
        if (!TASKS[task].variants[variant]) {
            return { ok: false, error: `Unknown variant: ${variant} for ${task}` };
        }
        const scenario = TASKS[task].scenario;

        this.activeTask    = task;
        this.activeVariant = variant;
        this.trialCounter += 1;

        this.resetScene();

        // Look up the missingSlot and correct attribution for this error type
        // so the trial record can later flag whether the repair supplied the slot.
        const taskObj = TASKS[task];
        const variantObj = taskObj && taskObj.variants[variant];
        const errorSpec = variantObj && variantObj.error;

        this.trial = {
            number:            this.trialCounter,
            block:             this.session.block,
            condition:         this.session.condition,
            task,
            variant,
            scenario,
            missingSlot:       errorSpec ? (errorSpec.missingSlot || "") : "",
            slotTerms:         errorSpec ? (errorSpec.slotTerms || []) : [],
            // Attribution is a property of the error TYPE, so it stays constant
            // across tasks and variants and remains comparable between people.
            correctAttribution: TASK_ATTRIBUTION[task] || "",
            attribution:       null,   // filled by POST /attribution
            repairContainsSlot: null,  // filled automatically on next transcript
            startedAt:         Date.now(),
            startedAtIso:      new Date().toISOString(),
            endedAt:           null,
            status:            "in-progress",
            attempts:          0,   // participant utterances
            injects:           0    // wizard injections
        };

        this.logEvent("trial-start", `${task}/${variant}/${scenario}`,
            { task, variant, scenario, attempt: 0 });
        console.log(`\x1b[35m[Trial ${this.trialCounter}]\x1b[0m start ` +
            `condition=${this.session.condition} task=${task} variant=${variant} scenario=${scenario}`);
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
            scenario: this.trial.scenario,
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
        this.activeTask    = planned.task;
        this.activeVariant = planned.variant;

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
     * participant, condition, block, task, scenario, trial, attempt.
     */
    logEvent(type, detail = "", extra = {}) {
        // Before a session is started, events are warm-up/testing — keep them out
        // of participant data files.
        const pid = this.session.participantId || "warmup";
        const file = path.join(LOG_DIR, `${pid}_events.csv`);
        const taskKey = extra.task || this.activeTask;
        const row = [
            new Date().toISOString(),
            pid,
            this.session.condition || "",
            this.session.block || "",
            this.trial ? this.trial.number : "",
            taskKey,
            extra.variant || (this.trial ? this.trial.variant : ""),
            extra.scenario !== undefined ? extra.scenario
                : (this.trial ? this.trial.scenario : ""),
            extra.attempt !== undefined ? extra.attempt : this.attemptCount(),
            type,
            extra.msSinceTrialStart !== undefined ? extra.msSinceTrialStart : "",
            csvEscape(detail)
        ].join(",");
        appendCsv(file,
            "timestamp,participantId,condition,block,trial,task,variant,scenario,attempt," +
            "eventType,msSinceTrialStart,detail", row);
    }

    /**
     * One row per completed trial — the primary analysis unit. Carries every
     * field the implementation note asks for, so per-condition comparisons need
     * no joining against the event log.
     */
    logTrial(trial) {
        const file = path.join(LOG_DIR, "trials.csv");
        const order = (this.session.plan || []).map(p => p.condition).join("-");
        appendCsv(file,
            "participantId,taskOrder,block,condition,trial,task,variant,scenario," +
            "startTime,endTime,durationMs,completionStatus,attempts,injects," +
            "attribution,correctAttribution,attributionCorrect,repairContainsSlot,missingSlot", [
            this.session.participantId,
            order,
            trial.block,
            trial.condition,
            trial.number,
            trial.task,
            trial.variant,
            trial.scenario,
            trial.startedAtIso,
            trial.endedAtIso || "",
            trial.endedAt ? trial.endedAt - trial.startedAt : "",
            trial.status,
            trial.attempts,
            trial.injects,
            trial.attribution   || "",
            trial.correctAttribution || "",
            trial.attribution !== null
                ? (trial.attribution === trial.correctAttribution ? "yes" : "no")
                : "",
            trial.repairContainsSlot !== null ? trial.repairContainsSlot : "",
            csvEscape(trial.missingSlot || "")
        ].join(","));
    }

    /** Records the session/block start row in the master sessions.csv. */
    logSessionStart() {
        const file = path.join(LOG_DIR, "sessions.csv");
        const order = (this.session.plan || []).map(p => p.condition).join("-");
        // Records the assigned variant and error pair too, so the plan actually
        // run is recoverable from the data even if the table is later revised.
        const plan = (this.session.plan || [])
            .map(p => `${p.condition}:${p.task}:${p.variant}:${(p.errors||[]).join("+")}`).join(" ");
        appendCsv(file,
            "timestamp,participantId,condition,block,conditionOrder,plan",
            [
                new Date().toISOString(), this.session.participantId, this.session.condition,
                this.session.block, order, csvEscape(plan)
            ].join(","));
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
        const row = [
            new Date().toISOString(),
            pid,
            payload.condition || (type === "background" ? "" : this.session.condition || ""),
            payload.block || (type === "background" ? "" : this.session.block || ""),
            payload.questionnaire || type,
            ...keys.map(k => csvEscape(String(answers[k])))
        ].join(",");
        appendCsv(file,
            "timestamp,participantId,condition,block,questionnaire," + keys.join(","), row);
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

            // Auto-flag whether the repair contains the missing slot keyword.
            // Only set once (first repair attempt after an error inject) so we
            // capture whether the feedback caused an immediate targeted fix —
            // if they say it first time without the slot the flag stays false.
            if (this.trial && !this.trial.endedAt &&
                this.trial.repairContainsSlot === null && this.trial.injects > 0) {
                const matched = slotMatched(text, this.trial.slotTerms);
                if (matched !== null) {
                    this.trial.repairContainsSlot = matched;
                    this.logEvent("repair-slot-check",
                        `slot="${this.trial.missingSlot}" found=${matched}`, {
                        msSinceTrialStart: Date.now() - this.trial.startedAt
                    });
                }
            }

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
        // "clear" removes trial debris and rebuilds the sphere and cube, so the
        // participant always opens on the arrangement the briefing describes.
        this.sendControl("clear");
        return { ok: true, reset: true };
    }

    /**
     * Injects an outcome for a task.
     *
     * outcome = "error"   → this task's scripted failure. Re-injectable:
     *                       if the participant repeats the same mistake, send it
     *                       again rather than handing them the answer.
     * outcome = "success" → the corrected result, once they repair the problem.
     *
     * The headset receives a JSON spec and performs it with compiled code, so
     * content can be edited here without rebuilding the APK.
     */
    injectResponse(taskKey, outcome, variantKey) {
        const task = TASKS[taskKey];
        if (!task) return { ok: false, error: `Unknown task: ${taskKey}` };

        const vKey = variantKey ||
            (this.trial ? this.trial.variant : this.activeVariant);
        const variant = task.variants[vKey];
        if (!variant) return { ok: false, error: `Unknown variant: ${vKey} for ${taskKey}` };

        const scenario = TASKS[taskKey] ? TASKS[taskKey].scenario : "";

        let spec;
        if (outcome === "success") {
            spec = variant.success;
        } else {
            spec = variant.error;
            if (!spec) return { ok: false, error: `No error spec for ${taskKey}` };
        }
        if (!spec) return { ok: false, error: `Unknown outcome: ${outcome}` };

        if (this.trial && !this.trial.endedAt) this.trial.injects += 1;
        const msSinceTrialStart = this.trial ? Date.now() - this.trial.startedAt : "";

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m ${taskKey}/${vKey} ${outcome}` +
            (outcome === "success" ? "" : `/${scenario}`));
        this.logEvent("inject", outcome === "success" ? "success" : `error/${scenario}`, {
            task: taskKey, variant: vKey,
            scenario: outcome === "success" ? "" : scenario,
            msSinceTrialStart
        });
        // A correct outcome is silent by design, so recording "nothing was shown"
        // matters as much as recording the error text — without this row the log
        // cannot distinguish a silent success from a missing feedback event.
        this.logEvent("feedback-shown",
            outcome === "success" ? "(silent — correct outcome)" : (spec.errorText || ""), {
            task: taskKey, variant: vKey,
            scenario: outcome === "success" ? "" : scenario
        });

        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome",
            peer: "WizardOfOz",
            data: JSON.stringify(spec)
        });

        return {
            ok: true, task: taskKey, variant: vKey, outcome,
            scenario: outcome === "success" ? "" : scenario,
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
                    activeVariant: this.activeVariant,
                    plannedTask: this.plannedTask(),
                    plannedVariant: this.plannedVariant(),
                    trial: this.trial,
                    attempt: this.attemptCount(),
                    taskAttribution: TASK_ATTRIBUTION,
                    availableTasks: Object.keys(TASKS).map(k => ({ key: k, name: TASKS[k].name }))
                });
            }

            if (req.method === "GET" && url === "/tasks") {
                return send(200, Object.entries(TASKS).map(([k, v]) => ({
                    key: k, name: v.name,
                    scenario: v.scenario,
                    attribution: TASK_ATTRIBUTION[k] || "",
                    variants: Object.entries(v.variants).map(([vk, vv]) => ({
                        key: vk, label: vv.label, prompt: vv.prompt,
                        error: {
                            errorText:   vv.error.errorText,
                            agentPost:   vv.error.agentPost,
                            missingSlot: vv.error.missingSlot || ""
                        }
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

                        // Follow the plan for this block unless overridden, so the
                        // Wizard never has to read the master table by hand.
                        if (planned) {
                            this.activeTask    = planned.task;
                            this.activeVariant = planned.variant;
                                            }

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
                    // Wizard confirms task and variant first, then starts.
                    if (req.method === "POST" && url === "/task") {
                        const key = String(payload.task).startsWith("task")
                            ? String(payload.task) : `task${payload.task}`;
                        if (!TASKS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        this.logEvent("task-change", key, { task: key, attempt: 0 });
                        return send(200, { activeTask: this.activeTask, plannedTask: this.plannedTask() });
                    }


                    // Selects the content variant (normally set by the plan).
                    if (req.method === "POST" && url === "/variant") {
                        const v = String(payload.variant || "");
                        if (!TASKS[this.activeTask].variants[v]) {
                            return send(400, { error: "Unknown variant: " + v });
                        }
                        this.activeVariant = v;
                        if (this.trial && !this.trial.endedAt) this.trial.variant = v;
                        return send(200, { ok: true, activeVariant: v });
                    }

                    // Start trial: resets the scene, starts the clock, fixes the
                    // (task, variant, error type) triple for this trial.
                    if (req.method === "POST" && url === "/trial/start") {
                        const key = payload.task
                            ? (String(payload.task).startsWith("task") ? String(payload.task) : `task${payload.task}`)
                            : null;
                        return send(200, this.startTrial(key, payload.variant));
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
                        // "error" replays this task's scripted failure (use again if
                        // they repeat the mistake); "success" resolves it.
                        const outcome = payload.outcome || payload.response || "error";
                        return send(200, this.injectResponse(taskKey, outcome, payload.variant));
                    }

                    // Attribution probe: wizard records what the participant said
                    // when asked why they think the error happened.
                    // Values: "self" | "system" | "unsure"
                    if (req.method === "POST" && url === "/attribution") {
                        const val = String(payload.attribution || "").toLowerCase();
                        if (!["self", "system", "unsure"].includes(val)) {
                            return send(400, { error: "attribution must be self|system|unsure" });
                        }
                        if (this.trial) {
                            this.trial.attribution = val;
                            const correct = this.trial.correctAttribution;
                            const isCorrect = val === correct;
                            this.logEvent("attribution",
                                `participant=${val} correct=${correct} match=${isCorrect}`, {
                                msSinceTrialStart: Date.now() - this.trial.startedAt
                            });
                        }
                        return send(200, { ok: true, attribution: val,
                            correct: this.trial ? this.trial.correctAttribution : "" });
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

/**
 * Appends a row to a CSV, guaranteeing the file's header matches the row.
 *
 * Writing the header only when the file is new is not enough: if the columns
 * change between runs — a task edit, a new field — the old file keeps its old
 * header and every new row lands under the wrong column names. That corrupts
 * the data silently, and mid-study it would not be noticed until analysis.
 * Here a stale file is set aside with a timestamped name and a fresh one is
 * started, so no row is ever written under a header it does not match.
 */
function appendCsv(file, header, row) {
    if (fs.existsSync(file)) {
        const first = fs.readFileSync(file, "utf8").split("\n", 1)[0];
        if (first.trim() !== header.trim()) {
            const stamp = new Date().toISOString().replace(/[:.]/g, "-");
            const retired = file.replace(/\.csv$/, `_pre-${stamp}.csv`);
            fs.renameSync(file, retired);
            console.log(`\x1b[33m[Logs]\x1b[0m column layout changed — ` +
                `previous ${path.basename(file)} kept as ${path.basename(retired)}`);
        }
    }
    if (!fs.existsSync(file)) fs.writeFileSync(file, header + "\n");
    fs.appendFileSync(file, row + "\n");
}

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

module.exports = { WizardOfOzApp, TASKS, TASK_ATTRIBUTION, planForParticipant };

if (require.main === module) {
    const app = new WizardOfOzApp();
    app.start();
}
