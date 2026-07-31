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

const ERROR_CATEGORIES = [
    { key: "missing_detail",       name: "Missing Detail",       short: "Nothing happened" },
    { key: "misrecognition",       name: "Misrecognition",       short: "Wrong thing acted on" },
    { key: "happened_differently", name: "Happened Differently", short: "Right action, wrong result" },
    { key: "happened_plus_extra",  name: "Happened Plus Extra",  short: "Correct, plus something extra" }
];

const ERROR_KEYS = ERROR_CATEGORIES.map(c => c.key);

// ── Ground-truth attribution per error type ──────────────────────────────────
// Fixed at the TYPE level, not per task, because error type is a factor in the
// design: if "happened_plus_extra" meant the user's fault in task 1 and the
// system's in task 2, attributionCorrect would not be comparable across the
// task/error combinations that counterbalancing hands to different people.
//
// The split is principled rather than arbitrary:
//   self   — the instruction was incomplete or ambiguous (omission, vagueness)
//   system — the instruction was fine; the pipeline mis-heard it or overreached
//
// Every error's wording MUST agree with its attribution here. Feedback that
// says "speak more clearly" while the ground truth is "system" would penalise
// participants for believing what they were just told — a confound running in
// the direction of the hypothesis.
const ERROR_ATTRIBUTION = {
    missing_detail:       "self",     // they left a required detail out
    misrecognition:       "system",   // they said it; ASR heard something else
    happened_differently: "self",     // their phrasing allowed another reading
    happened_plus_extra:  "system"    // they never asked for the extra behaviour
};

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

// ── Task builders ────────────────────────────────────────────────────────────
// Variants share one structure and differ only in the object/colour/target named.
// Generating them from a template rather than writing 36 blocks by hand keeps the
// four error types genuinely parallel across variants — the property the design
// depends on for variants to be interchangeable.

/** Task 1 — create an object that appears in the participant's hand. */
function buildTask1(v) {
    const o = v.object;                       // display name, e.g. "ball"
    return {
        label: `Create a ${o} in your hand`,
        prompt: `In this scene you can see a sphere, a cube, and a campfire. We would ` +
                `like you to ask the system to create a ${o} that appears in your hand ` +
                `when you raise it. Use your own words, as if you were talking to someone.`,
        errors: {
            // Root cause: hand height not specified → nothing appears.
            missing_detail: {
                action: "noop",
                errorText: `The ${o} did not appear — no hand height was given.`,
                agentPost: `I wasn't told how high your hand needed to be, so I didn't ` +
                           `know when to create the ${o}.`,
                missingSlot: "hand height",
                slotTerms: ["height", "high", "above", "shoulder", "chest", "eye level",
                            "level", "raise", "raised", "up", "upward", "lift"]
            },
            // Root cause: ASR mis-heard a clearly spoken word → a wall appears.
            // Wording must NOT ask them to speak more clearly: the ground truth
            // is that the pipeline erred, and telling them otherwise would make
            // the honest answer ("I wasn't clear") score as wrong.
            misrecognition: {
                action: "spawn", shape: "cube", pos: "hand",
                scaleX: 1.2, scaleY: 0.9, scaleZ: 0.08,
                errorText: `I heard "wall" and created that instead of the ${o}.`,
                agentPost: `I think I misheard you — I picked up "wall" rather than ` +
                           `"${o}", so I made the wrong thing.`,
                missingSlot: o,
                slotTerms: [o]
            },
            // Root cause: exact hand placement ambiguous → it lands on the floor.
            happened_differently: {
                action: "spawn", shape: v.shape, pos: "floor", physics: true,
                scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color,
                errorText: `The ${o} appeared, but no location was specified.`,
                agentPost: `The ${o} was created, but I wasn't sure where you wanted it.`,
                missingSlot: "in my hand",
                slotTerms: ["hand", "hands", "palm", "holding", "hold", "grip",
                            "grasp", "fingers"]
            },
            // Root cause: system created more than asked — an overreach, not an
            // omission, so this is "system" like every happened_plus_extra.
            happened_plus_extra: {
                action: "spawn", shape: v.shape, pos: "hand", physics: true, count: 5,
                scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color,
                errorText: `I created several ${o}s when only one was asked for.`,
                agentPost: `That went further than you asked — I made a whole group of ` +
                           `${o}s instead of the one.`,
                missingSlot: "only one",
                slotTerms: ["one", "single", "just one", "only one", "a single",
                            "one only", "1"]
            }
        },
        // Correct outcome: no agent intervention in C, no panel in B, nothing in A.
        success: {
            action: "spawn", shape: v.shape, pos: "hand",
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color
        }
    };
}

/** Task 2 — move an object so it comes to rest next to a target. */
function buildTask2(v) {
    const m = v.mover, t = v.target, w = v.wrongMover;
    return {
        label: `Move the ${m} to the ${t}`,
        prompt: `In this scene you can see a sphere, a cube, and a campfire. We would ` +
                `like you to ask the system to move the ${m} so that it ends up next to ` +
                `the ${t}. Use your own words.`,
        errors: {
            // Root cause: how close was never stated → it does not move.
            missing_detail: {
                action: "noop",
                errorText: `The ${m} did not move — no destination distance was given.`,
                agentPost: `I wasn't told how close to the ${t} you wanted the ${m}, ` +
                           `so I left it where it was.`,
                missingSlot: `next to the ${t}`,
                slotTerms: ["next to", "beside", "near", "close", "closer", "adjacent",
                            "by the", "against", "touching", "up to", "alongside", t]
            },
            // Root cause: ASR mis-heard the object word → the other one moves.
            misrecognition: {
                action: "move", target: w, moveTo: t,
                errorText: `I heard "${w}" and moved that instead of the ${m}.`,
                agentPost: `I think I misheard you — I picked up "${w}" rather than ` +
                           `"${m}", so I moved the wrong one.`,
                missingSlot: m,
                slotTerms: [m]
            },
            // Root cause: direction ambiguous → it moves the wrong way.
            happened_differently: {
                action: "move", target: m, moveTo: t, away: true,
                errorText: `The ${m} moved, but no direction was specified.`,
                agentPost: `The ${m} moved, but I wasn't sure which way you meant.`,
                missingSlot: `toward the ${t}`,
                slotTerms: [t, "toward", "towards", "to the", "into", "onto",
                            "next to", "beside", "near", "closer"]
            },
            // Root cause: system resized it unasked — overreach, so "system".
            happened_plus_extra: {
                action: "move", target: m, moveTo: t, scaleMultiplier: 2.2,
                errorText: `The ${m} reached the ${t}, but I also resized it unasked.`,
                agentPost: `That went further than you asked — I moved the ${m} but ` +
                           `changed its size as well.`,
                missingSlot: "keep the same size",
                slotTerms: ["size", "scale", "same size", "keep", "dont", "don't",
                            "do not", "without", "bigger", "smaller", "resize", "grow"]
            }
        },
        success: { action: "move", target: m, moveTo: t }
    };
}

/** Task 3 — recolour an object and move it next to a target. */
function buildTask3(v) {
    const o = v.object, c = v.colourName, t = v.target;
    return {
        label: `Make the ${o} ${c} and move it next to the ${t}`,
        prompt: `In this scene you can see a sphere, a cube, and a campfire. We would ` +
                `like you to ask the system to change the colour of the ${o} and move it ` +
                `next to the ${t}. Use your own words.`,
        errors: {
            // Root cause: destination never stated → colour lands, move does not.
            missing_detail: {
                action: "recolor", target: o, color: v.colour,
                errorText: `The colour changed, but no destination was given.`,
                agentPost: `I changed the colour, but I wasn't told where to put the ${o}.`,
                missingSlot: `next to the ${t}`,
                slotTerms: [t, "next to", "beside", "near", "close", "adjacent",
                            "by the", "move", "put", "place"]
            },
            // Root cause: ASR mis-heard the colour word.
            misrecognition: {
                action: "recolor", target: o, color: v.wrongColour, moveTo: t,
                errorText: `I heard a different colour and applied that instead of ${c}.`,
                agentPost: `I think I misheard the colour — I didn't pick up "${c}", ` +
                           `so I used the wrong one.`,
                missingSlot: c,
                slotTerms: [c]
            },
            // Root cause: "next to" left the exact placement open → ends up far.
            happened_differently: {
                action: "recolor", target: o, color: v.colour, moveTo: t, far: true,
                errorText: `The ${o} moved, but how close to place it was left open.`,
                agentPost: `I moved the ${o}, but I wasn't sure how near the ${t} ` +
                           `you wanted it.`,
                missingSlot: "right next to it",
                slotTerms: ["next to", "beside", "near", "close", "closer", "touching",
                            "against", "adjacent", "right by", "alongside", t]
            },
            // Root cause: system added spin unasked — overreach, so "system".
            happened_plus_extra: {
                action: "recolor", target: o, color: v.colour, moveTo: t, spin: true,
                errorText: `The ${o} is correct, but I also set it spinning unasked.`,
                agentPost: `That went further than you asked — the ${o} is right, but ` +
                           `I made it spin as well.`,
                missingSlot: "keep it still",
                slotTerms: ["still", "stop", "spin", "spinning", "rotate", "rotating",
                            "steady", "static", "dont", "don't", "do not", "without",
                            "motionless", "stationary"]
            }
        },
        success: { action: "recolor", target: o, color: v.colour, moveTo: t }
    };
}

const TASKS = {
    task1: {
        name: "Create an object in your hand",
        variants: {
            v1: buildTask1({ object: "ball",   shape: "sphere",   scale: 0.15, color: "" }),
            v2: buildTask1({ object: "cube",   shape: "cube",     scale: 0.15, color: "" }),
            v3: buildTask1({ object: "lantern", shape: "capsule", scale: 0.16, color: "#ffd66b" })
        }
    },
    task2: {
        name: "Move an object to a target",
        variants: {
            v1: buildTask2({ mover: "sphere", target: "campfire", wrongMover: "cube"   }),
            v2: buildTask2({ mover: "cube",   target: "campfire", wrongMover: "sphere" }),
            v3: buildTask2({ mover: "sphere", target: "cube",     wrongMover: "cube"   })
        }
    },
    task3: {
        name: "Recolour an object and move it",
        variants: {
            v1: buildTask3({ object: "cube",   colourName: "red",   colour: "#e02020",
                             wrongColour: "#1560ff", target: "sphere"   }),
            v2: buildTask3({ object: "sphere", colourName: "blue",  colour: "#1560ff",
                             wrongColour: "#2ecc40", target: "cube"     }),
            v3: buildTask3({ object: "cube",   colourName: "green", colour: "#2ecc40",
                             wrongColour: "#e02020", target: "campfire" })
        }
    }
};

// ── Counterbalancing (master table) ──────────────────────────────────────────
//
// Systematic rotation, not random, reproducing the agreed master table exactly:
//
//   condition      = cycles A, B, C by participant   → 4×A, 3×B, 3×C over 10
//   variant(p,t)   = ((p-1) + (t-1)) mod 3
//   error pair(p,t)= two consecutive types from the 4-type rotation starting at
//                    (p + t - 2) mod 4
//
// Each participant does all three tasks, sees each variant once, and gets two of
// the four error types per task. Every cell of the printed P1–P10 table is
// reproduced by these formulas (verified against the document).
//
// ⚠ DESIGN CONFLICT — READ BEFORE RUNNING PARTICIPANTS
// The master table assigns ONE condition per participant (between-subjects), but
// the previous implementation note specified within-subjects, every participant
// doing all three conditions. These cannot both be true and they imply different
// sample sizes and analyses. The table is marked "Proposed — for Supervisor
// Confirmation", so it is implemented as the default here; flip STUDY_DESIGN to
// "within" if the supervisor confirms otherwise. Nothing else needs to change.

const STUDY_DESIGN = "between";   // "between" = master table | "within" = 3 conditions per participant

const CONDITIONS = ["A", "B", "C"];

// Used only when STUDY_DESIGN === "within".
const CONDITION_ORDERS = [
    ["A", "B", "C"], ["A", "C", "B"],
    ["B", "A", "C"], ["B", "C", "A"],
    ["C", "A", "B"], ["C", "B", "A"]
];

const TASK_KEYS = ["task1", "task2", "task3"];
const VARIANT_KEYS = ["v1", "v2", "v3"];

/** Participant number from an id (P01 → 1, 7 → 7). 1-based, as the table is. */
function participantIndex(pid) {
    const digits = String(pid || "").match(/\d+/);
    if (digits) return parseInt(digits[0], 10);
    let h = 0;
    for (const ch of String(pid || "")) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return (h % 10) + 1;
}

/**
 * The full plan for a participant: one entry per task, giving the condition,
 * the content variant and the two error types to inject.
 */
function planForParticipant(pid) {
    const p = participantIndex(pid);
    return TASK_KEYS.map((taskKey, ti) => {
        const t = ti + 1;
        const variant = VARIANT_KEYS[((p - 1) + ti) % VARIANT_KEYS.length];
        const start = (p + t - 2) % ERROR_KEYS.length;
        const errors = [ERROR_KEYS[start], ERROR_KEYS[(start + 1) % ERROR_KEYS.length]];
        const condition = STUDY_DESIGN === "within"
            ? CONDITION_ORDERS[p % CONDITION_ORDERS.length][ti]
            : CONDITIONS[(p - 1) % CONDITIONS.length];
        return {
            block: t,
            condition,
            task: taskKey,
            taskName: TASKS[taskKey].name,
            variant,
            variantLabel: TASKS[taskKey].variants[variant].label,
            errors
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
        this.activeError     = ERROR_KEYS[0];  // error type armed for the next inject
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
    plannedErrors(taskKey = this.activeTask) {
        const b = (this.session.plan || []).find(x => x.task === taskKey);
        return b ? b.errors : ERROR_KEYS.slice(0, 2);
    }

    attemptCount() {
        return this.trial ? this.trial.attempts : 0;
    }

    /**
     * Begins a trial: clears the scene, arms an error type, and starts the
     * clock. Every trial starts from an identical scene so a participant never
     * inherits objects or colours from the previous trial.
     */
    startTrial(taskKey, errorCategory, variantKey) {
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
        const category = errorCategory || this.plannedErrors(task)[0];

        this.activeTask    = task;
        this.activeError   = category;
        this.activeVariant = variant;
        this.trialCounter += 1;

        this.resetScene();

        // Look up the missingSlot and correct attribution for this error type
        // so the trial record can later flag whether the repair supplied the slot.
        const taskObj = TASKS[task];
        const variantObj = taskObj && taskObj.variants[variant];
        const errorSpec = variantObj && variantObj.errors[category];

        this.trial = {
            number:            this.trialCounter,
            block:             this.session.block,
            condition:         this.session.condition,
            task,
            variant,
            errorCategory:     category,
            plannedErrors:     this.plannedErrors(task),
            missingSlot:       errorSpec ? (errorSpec.missingSlot || "") : "",
            slotTerms:         errorSpec ? (errorSpec.slotTerms || []) : [],
            // Attribution is a property of the error TYPE, so it stays constant
            // across tasks and variants and remains comparable between people.
            correctAttribution: ERROR_ATTRIBUTION[category] || "",
            attribution:       null,   // filled by POST /attribution
            repairContainsSlot: null,  // filled automatically on next transcript
            startedAt:         Date.now(),
            startedAtIso:      new Date().toISOString(),
            endedAt:           null,
            status:            "in-progress",
            attempts:          0,   // participant utterances
            injects:           0    // wizard injections
        };

        this.logEvent("trial-start", `${task}/${variant}/${category}`,
            { task, variant, errorCategory: category, attempt: 0 });
        console.log(`\x1b[35m[Trial ${this.trialCounter}]\x1b[0m start ` +
            `condition=${this.session.condition} task=${task} variant=${variant} error=${category}`);
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
        this.activeTask    = planned.task;
        this.activeVariant = planned.variant;
        this.activeError   = planned.errors[0];

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
        const taskKey = extra.task || this.activeTask;
        const row = [
            new Date().toISOString(),
            pid,
            this.session.condition || "",
            this.session.block || "",
            this.trial ? this.trial.number : "",
            taskKey,
            extra.variant || (this.trial ? this.trial.variant : ""),
            extra.errorCategory !== undefined ? extra.errorCategory
                : (this.trial ? this.trial.errorCategory : ""),
            extra.attempt !== undefined ? extra.attempt : this.attemptCount(),
            type,
            extra.msSinceTrialStart !== undefined ? extra.msSinceTrialStart : "",
            csvEscape(detail)
        ].join(",");
        appendCsv(file,
            "timestamp,participantId,condition,block,trial,task,variant,errorType,attempt," +
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
            "participantId,conditionOrder,block,condition,trial,task,variant,errorType," +
            "startTime,endTime,durationMs,completionStatus,attempts,injects," +
            "attribution,correctAttribution,attributionCorrect,repairContainsSlot,missingSlot", [
            this.session.participantId,
            order,
            trial.block,
            trial.condition,
            trial.number,
            trial.task,
            trial.variant,
            trial.errorCategory,
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
     * outcome = "error"   → the selected error category's failure. Re-injectable:
     *                       if the participant repeats the same mistake, send it
     *                       again rather than handing them the answer.
     * outcome = "success" → the corrected result, once they repair the problem.
     *
     * The headset receives a JSON spec and performs it with compiled code, so
     * content can be edited here without rebuilding the APK.
     */
    injectResponse(taskKey, outcome, errorCategory, variantKey) {
        const task = TASKS[taskKey];
        if (!task) return { ok: false, error: `Unknown task: ${taskKey}` };

        const vKey = variantKey ||
            (this.trial ? this.trial.variant : this.activeVariant);
        const variant = task.variants[vKey];
        if (!variant) return { ok: false, error: `Unknown variant: ${vKey} for ${taskKey}` };

        const category = errorCategory ||
            (this.trial ? this.trial.errorCategory : this.activeError);

        let spec;
        if (outcome === "success") {
            spec = variant.success;
        } else {
            spec = variant.errors[category];
            if (!spec) return { ok: false, error: `Unknown error type: ${category}` };
        }
        if (!spec) return { ok: false, error: `Unknown outcome: ${outcome}` };

        if (this.trial && !this.trial.endedAt) this.trial.injects += 1;
        const msSinceTrialStart = this.trial ? Date.now() - this.trial.startedAt : "";

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m ${taskKey}/${vKey} ${outcome}` +
            (outcome === "success" ? "" : `/${category}`));
        this.logEvent("inject", outcome === "success" ? "success" : `error/${category}`, {
            task: taskKey, variant: vKey,
            errorCategory: outcome === "success" ? "" : category,
            msSinceTrialStart
        });
        // A correct outcome is silent by design, so recording "nothing was shown"
        // matters as much as recording the error text — without this row the log
        // cannot distinguish a silent success from a missing feedback event.
        this.logEvent("feedback-shown",
            outcome === "success" ? "(silent — correct outcome)" : (spec.errorText || ""), {
            task: taskKey, variant: vKey,
            errorCategory: outcome === "success" ? "" : category
        });

        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome",
            peer: "WizardOfOz",
            data: JSON.stringify(spec)
        });

        return {
            ok: true, task: taskKey, variant: vKey, outcome,
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
                    activeVariant: this.activeVariant,
                    plannedTask: this.plannedTask(),
                    plannedVariant: this.plannedVariant(),
                    plannedErrors: this.plannedErrors(),
                    trial: this.trial,
                    attempt: this.attemptCount(),
                    errorCategories: ERROR_CATEGORIES,
                    availableTasks: Object.keys(TASKS).map(k => ({ key: k, name: TASKS[k].name }))
                });
            }

            if (req.method === "GET" && url === "/tasks") {
                return send(200, Object.entries(TASKS).map(([k, v]) => ({
                    key: k, name: v.name,
                    variants: Object.entries(v.variants).map(([vk, vv]) => ({
                        key: vk, label: vv.label, prompt: vv.prompt,
                        errors: Object.entries(vv.errors).map(([ek, ev]) => ({
                            key: ek, errorText: ev.errorText, agentPost: ev.agentPost,
                            missingSlot: ev.missingSlot || "",
                            attribution: ERROR_ATTRIBUTION[ek] || ""
                        }))
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
                            this.activeError   = planned.errors[0];
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
                    // Wizard sets task and error category first, then starts.
                    if (req.method === "POST" && url === "/task") {
                        const key = String(payload.task).startsWith("task")
                            ? String(payload.task) : `task${payload.task}`;
                        if (!TASKS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        this.logEvent("task-change", key, { task: key, attempt: 0 });
                        return send(200, { activeTask: this.activeTask, plannedTask: this.plannedTask() });
                    }

                    // Arms which of the four error types the next inject will use.
                    if (req.method === "POST" && url === "/error-category") {
                        const cat = String(payload.category || "");
                        if (!ERROR_KEYS.includes(cat)) {
                            return send(400, { error: "Unknown error type: " + cat });
                        }
                        this.activeError = cat;
                        if (this.trial && !this.trial.endedAt) this.trial.errorCategory = cat;
                        return send(200, { ok: true, activeError: cat });
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
                        return send(200, this.startTrial(key, payload.category, payload.variant));
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
                        return send(200, this.injectResponse(
                            taskKey, outcome, payload.category, payload.variant));
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

module.exports = { WizardOfOzApp, TASKS, ERROR_CATEGORIES, CONDITION_ORDERS, planForParticipant };

if (require.main === module) {
    const app = new WizardOfOzApp();
    app.start();
}
