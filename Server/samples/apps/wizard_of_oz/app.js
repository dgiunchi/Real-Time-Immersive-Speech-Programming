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
const TELEMETRY_NETWORK_ID = 97;  // StudyTelemetry (head pose from the headset)

const PUBLIC_DIR = path.join(__dirname, "public");
// Study results live in <project root>/Logs (git-ignored, human-findable).
const LOG_DIR    = path.resolve(__dirname, "..", "..", "..", "..", "Logs");
// Superseded files are moved here rather than left beside the live ones, so
// Logs/ only ever shows one file per participant.
const ARCHIVE_DIR = path.join(LOG_DIR, "archive");

// The participant file's columns, in order. APPEND ONLY — never reorder or
// remove, or previously written files stop lining up with new ones.
//
// Rows are written by name, so a row type simply leaves blank whatever does not
// apply to it. `recordType` says which columns to expect:
//
//   event            any state change — transcripts, injects, notes, head pose
//   trial-summary    one per completed trial; the analysis unit
//   session-start    the assigned plan, written when a session or block begins
//   questionnaire    the whole form in one row, answers held as JSON
//   questionnaire-item   one per answered item — the tidy form, ready to analyse
//   questionnaire-score  one per scored scale/subscale
const LOG_COLUMNS = [
    // Identity and clocks. Three clocks because each answers a different
    // question: epochMs merges against video, msSinceSessionStart shows fatigue
    // and drift, msSinceTrialStart positions an event within the analysis unit.
    "seq", "timestampIso", "epochMs", "msSinceSessionStart", "msSinceTrialStart",
    "participantId", "condition", "block", "trial", "task", "variant", "scenario",
    "attempt", "source", "category", "recordType",
    // Event payload. `eventType` stays its own column so the file can still be
    // filtered to transcripts, injects or head pose without parsing prose.
    "eventType", "detail", "value", "target", "posX", "posY", "posZ", "yaw",
    // Trial summary.
    "taskOrder", "startTime", "endTime", "durationMs", "completionStatus",
    "attempts", "injects", "attribution", "correctAttribution",
    "attributionCorrect",
    "perceivedReparability", "reparabilityCorrect",
    "noticedFeedback", "firstRepairStrategy",
    "repairSequence", "wastedRepairs", "repairContainsSlot", "preInjectHadSlot",
    "msToFirstRepair", "maxUtteranceSimilarity", "utteranceSimilarities",
    "missingSlot",
    // Session and questionnaire.
    "conditionOrder", "plan", "questionnaire", "answers",
    // ── Scene objects ────────────────────────────────────────────────────
    // Every object the study creates is followed from spawn to destruction:
    // where it appeared, what colour it was, where it went, and how long it
    // stayed. `lifetimeMs` is only filled on the destroy row — that is the
    // row that knows it.
    "objectId", "objectShape", "color", "prevColor",
    "fromX", "fromY", "fromZ", "lifetimeMs",
    // ── Questionnaire, in analysable form ────────────────────────────────
    // The JSON blob in `answers` is the archive copy; these are what an
    // analysis actually reads. One row per item, one row per scored scale,
    // so no one has to parse JSON out of a CSV cell.
    "itemId", "itemRaw", "itemScore", "itemReversed", "scaleName", "scaleScore",
    // Which published instrument the item or scale came from, carried on the row
    // itself so provenance survives without a methods chapter beside it.
    "instrument"
];

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

/**
 * Lexical overlap between two utterances, 0..1 (Jaccard over normalised tokens).
 *
 * This exists to put an independent check under `wastedRepairs`. That measure is
 * co-primary and is coded live by the wizard, in the moment, while also running
 * the session - a single rater with no second opinion on a headline number. If a
 * reviewer asks how we know "repeated verbatim" means what we say it means, the
 * answer cannot be "the researcher judged it".
 *
 * The raw score is logged rather than a yes/no. Where the cut sits between
 * "repeated himself" and "rephrased" is an analysis decision, and baking a
 * threshold in here would quietly make it an implementation one.
 */
function utteranceSimilarity(a, b) {
    const tok = t => String(t || "").toLowerCase()
        .replace(/[^a-z0-9\s]/g, " ").split(/\s+/).filter(Boolean);
    const A = new Set(tok(a)), B = new Set(tok(b));
    if (!A.size || !B.size) return null;
    let inter = 0;
    for (const w of A) if (B.has(w)) inter++;
    return +(inter / (A.size + B.size - inter)).toFixed(3);
}

// ── Questionnaire scoring ────────────────────────────────────────────────────
//
// Scoring lives here, next to the logging, so the participant file already
// contains the numbers the analysis needs. The alternative — a JSON blob in one
// cell and a scoring script written months later — is where reversal mistakes
// come from, and a reversal applied wrongly is invisible: it just moves a mean.
//
// Every scale below is used as published. Items were reworded for
// comprehensibility (non-native speakers) but not renumbered, so the reversal
// sets are the standard ones.

/** Negatively worded items, scored (max + 1) - raw. */
const QUESTIONNAIRE_REVERSED = new Set([
    // SUS: the even-numbered items.
    "sus2", "sus4", "sus6", "sus8", "sus10",
    // UES-SF: the Perceived Usability subscale is worded as complaints, so all
    // three reverse. This is the most commonly botched part of this scale.
    "ues_pu1", "ues_pu2", "ues_pu3",
    // Trust (Jian et al.): items 01-05 are the distrust half.
    "trust01", "trust02", "trust03", "trust04", "trust05",
    // IPQ: "I did not feel present in the virtual space."
    "pres2"
    // NASA-TLX has no reversed items. Performance runs Perfect -> Failure, so a
    // high score already means high workload, same direction as the other five.
    // Reversing it is the classic TLX mistake and would invert the subscale.
    // ESS has no reversed items either.
    // Godspeed differentials already run negative -> positive, so a high score
    // is a high rating on the named dimension; nothing to reverse.
    // SSQ items are all symptoms — higher is worse throughout.
    // The retrospective attribution items are not a scale and are never summed,
    // so reversing attr2 against attr1 would be wrong: they measure two
    // different things that can both be high.
]);

/**
 * Scale maximum per item, for reversal. Everything not listed is 1-5.
 *
 * Only matters for reversed items, but getting it wrong is silent: reversing a
 * 1-7 item against a maximum of 5 produces negative scores that still average
 * to a plausible-looking number.
 */
const QUESTIONNAIRE_MAX = {
    trust01: 7, trust02: 7, trust03: 7, trust04: 7, trust05: 7,
    pres2: 7
};

/**
 * Which published instrument an item belongs to, stored with every answer.
 *
 * Without this the provenance of a number lives only in a methods chapter, and
 * anyone re-analysing the file a year later has to guess whether `sus2` was
 * reverse-scored and against which norms it should be read.
 */
const ITEM_INSTRUMENT = {};
const registerInstrument = (ids, name) => ids.forEach(id => { ITEM_INSTRUMENT[id] = name; });

const SCALE_ITEMS = {
    sus: Array.from({ length: 10 }, (_, i) => `sus${i + 1}`),
    // Raw (unweighted) TLX: the mean of the six subscales. The weighted version
    // needs 15 pairwise comparisons from the participant, which is more burden
    // than an exploratory moderator justifies; Raw TLX is the accepted
    // alternative and is what gets reported.
    nasa_tlx: ["tlx_mental", "tlx_physical", "tlx_temporal",
               "tlx_performance", "tlx_effort", "tlx_frustration"],
    ess: Array.from({ length: 8 }, (_, i) => `ess${i + 1}`),

    // Items 1-4 only. Item 5 is a 1-7 overall rating and is reported on its own
    // below: averaging it in with four 1-5 items gives a mean that belongs to
    // neither scale, which is the kind of number that survives into a results
    // table because it still looks plausible.
    perceived_support: Array.from({ length: 4 }, (_, i) => `psup${i + 1}`),
    // Eleven items, not twelve. trust12 measures familiarity rather than trust
    // and is reported on its own below — folding it in is the documented usual
    // mistake with this scale.
    trust_automation:  Array.from({ length: 11 }, (_, i) => `trust${String(i + 1).padStart(2, "0")}`),
    presence:          Array.from({ length: 4 }, (_, i) => `pres${i + 1}`),
    self_efficacy_post: Array.from({ length: 3 }, (_, i) => `se_post${i + 1}`),

    // UES-SF is reported as an overall mean and as its four subscales; the
    // subscales are what the scale is actually for. Item ids carry their
    // subscale because the presentation order is randomised per participant,
    // so position no longer identifies the item.
    ues_focused_attention:   ["ues_fa1", "ues_fa2", "ues_fa3"],
    ues_perceived_usability: ["ues_pu1", "ues_pu2", "ues_pu3"],
    ues_aesthetic_appeal:    ["ues_ae1", "ues_ae2", "ues_ae3"],
    ues_reward:              ["ues_rw1", "ues_rw2", "ues_rw3"],
    ues_sf: ["fa", "pu", "ae", "rw"].flatMap(s => [1, 2, 3].map(i => `ues_${s}${i}`)),

    godspeed_anthropomorphism: Array.from({ length: 5 }, (_, i) => `gs_anthro${i + 1}`),
    godspeed_intelligence:     Array.from({ length: 5 }, (_, i) => `gs_intel${i + 1}`)

    // SSQ is deliberately absent here: it is not a mean, it has its own
    // weighted subscale formula and is computed separately below.
    // The retrospective attribution items are likewise not a scale.
};

registerInstrument(SCALE_ITEMS.sus,      "SUS (Brooke 1996)");
registerInstrument(SCALE_ITEMS.nasa_tlx, "NASA-TLX (Hart & Staveland 1988)");
registerInstrument(SCALE_ITEMS.ess,      "ESS (Hoffman et al. 2018)");
// psup5 is listed explicitly: it is part of the instrument but not of the
// scale mean, so registering SCALE_ITEMS.perceived_support alone would leave it
// as the one item in the form with no provenance.
registerInstrument([...SCALE_ITEMS.perceived_support, "psup5"],
                   "Perceived Support (custom, H3)");
registerInstrument(SCALE_ITEMS.self_efficacy_post,
                   "Speech-system self-efficacy (custom, post)");
registerInstrument(["blame_split", "attr_confidence"],
                   "Graded attribution (custom, H1)");
registerInstrument([...SCALE_ITEMS.trust_automation, "trust12"],
                   "Trust in Automation (Jian, Bisantz & Drury 2000)");
registerInstrument(SCALE_ITEMS.presence,
                   "IPQ presence, 4 items (Schubert et al. 2001)");
registerInstrument(SCALE_ITEMS.ues_sf,
                   "UES-SF (O'Brien, Cairns & Hall 2018)");
registerInstrument([...SCALE_ITEMS.godspeed_anthropomorphism,
                    ...SCALE_ITEMS.godspeed_intelligence],
                   "Godspeed I & III (Bartneck et al. 2009)");
registerInstrument(Array.from({ length: 16 }, (_, i) => `ssq${i + 1}`),
                   "SSQ (Kennedy et al. 1993)");
registerInstrument(Array.from({ length: 5 }, (_, i) => `attr${i + 1}`),
                   "Retrospective attribution (custom, H1)");
registerInstrument(Array.from({ length: 3 }, (_, i) => `se_pre${i + 1}`),
                   "Speech-system self-efficacy (custom, pre)");
registerInstrument(["fatigue_mental", "fatigue_energy"],
                   "Fatigue and focus (custom, post-session)");
registerInstrument(["c_read", "c_questions", "c_voluntary", "c_deception",
                    "c_publication", "c_logging", "c_audio", "c_age",
                    "c_takepart", "consent_date", "consent_version", "researcher"],
                   "Informed consent record");
registerInstrument(Array.from({ length: 6 }, (_, i) => `iv${i + 1}`),
                   "Semi-structured interview");
// Covariates, not scales — tagged so nobody later mistakes them for one.
registerInstrument(["age", "gender", "native_language", "english_proficiency",
                    "vr_experience", "gaming_frequency", "tech_background",
                    "assistant_use", "assistant_reliability", "handedness",
                    "hearing", "speech_condition"],
                   "Background (custom covariates)");
registerInstrument(["discomfort_pre", "discomfort_post"],
                   "Single-item discomfort (0-10, paired pre/post)");

function scoredValue(itemId, raw) {
    const n = Number(raw);
    if (!Number.isFinite(n)) return null;
    const max = QUESTIONNAIRE_MAX[itemId] || 5;
    return QUESTIONNAIRE_REVERSED.has(itemId) ? (max + 1) - n : n;
}

/** Mean of the answered items in a scale, or null if none were answered. */
function scaleMean(answers, items) {
    const vals = items.map(id => scoredValue(id, answers[id])).filter(v => v !== null);
    if (!vals.length) return null;
    return +(vals.reduce((a, b) => a + b, 0) / vals.length).toFixed(3);
}

/**
 * Every scale score for one submitted form. Scales the participant did not see
 * (ESS in condition A) are simply absent rather than zero.
 */
function scoreQuestionnaire(answers = {}) {
    const out = {};
    for (const [name, items] of Object.entries(SCALE_ITEMS)) {
        const m = scaleMean(answers, items);
        if (m !== null) out[name] = m;
    }

    // SUS has its own published transform: sum the 0-4 contributions and
    // multiply by 2.5 for the familiar 0-100 figure. Reported as well as the
    // item mean, because 0-100 is the number anyone comparing against published
    // benchmarks will be looking for.
    const susVals = SCALE_ITEMS.sus
        .map(id => scoredValue(id, answers[id]))
        .filter(v => v !== null);
    if (susVals.length === SCALE_ITEMS.sus.length) {
        out.sus_score_0_100 = +(susVals.reduce((a, b) => a + (b - 1), 0) * 2.5).toFixed(2);
    }

    // Kept out of the scale mean above because it is a 1-7 item among 1-5 ones.
    const overall = Number(answers.psup5);
    if (Number.isFinite(overall)) out.perceived_support_overall_1_7 = overall;

    // Familiarity, reported beside the trust total rather than inside it.
    // Everyone meets this system for the first time, so it doubles as a check
    // that nobody arrived already knowing it.
    const fam = Number(answers.trust12);
    if (Number.isFinite(fam)) out.trust_familiarity = fam;

    Object.assign(out, scoreSSQ(answers));
    return out;
}

/**
 * SSQ subscales, Kennedy et al. (1993).
 *
 * Not a mean of anything. Each subscale sums a specific, overlapping subset of
 * the sixteen symptoms and multiplies by a published constant — several
 * symptoms count towards two subscales, which is why this cannot be expressed
 * as a SCALE_ITEMS entry. Item numbers are 1-16 in the standard printed order.
 */
const SSQ_SUBSCALES = {
    ssq_nausea:         { items: [1, 6, 7, 8, 9, 15, 16], weight: 9.54 },
    ssq_oculomotor:     { items: [1, 2, 3, 4, 5, 9, 11],   weight: 7.58 },
    ssq_disorientation: { items: [5, 8, 10, 11, 12, 13, 14], weight: 13.92 }
};

/**
 * Which instrument a computed score belongs to.
 *
 * Most scores map straight back through SCALE_ITEMS, but the derived ones —
 * SSQ's weighted subscales, the SUS 0-100 transform, the single overall support
 * item — have no item list of their own and would otherwise be written with a
 * blank provenance column, which is exactly the gap the column exists to close.
 */
function instrumentForScale(scaleName) {
    const first = (SCALE_ITEMS[scaleName] || [])[0];
    if (first && ITEM_INSTRUMENT[first]) return ITEM_INSTRUMENT[first];

    if (scaleName.startsWith("ssq_")) return ITEM_INSTRUMENT.ssq1 || "";
    if (scaleName.startsWith("perceived_support")) return ITEM_INSTRUMENT.psup1 || "";
    // Familiarity is trust12, deliberately kept out of the trust total, so it
    // has no SCALE_ITEMS entry to look through.
    if (scaleName === "trust_familiarity") return ITEM_INSTRUMENT.trust12 || "";

    const base = scaleName.replace(/_score_0_100$/, "");
    return ITEM_INSTRUMENT[(SCALE_ITEMS[base] || [])[0]] || "";
}

function scoreSSQ(answers = {}) {
    const out = {};
    let rawTotal = 0, complete = true;

    for (const [name, { items, weight }] of Object.entries(SSQ_SUBSCALES)) {
        const vals = items.map(i => Number(answers[`ssq${i}`]));
        if (vals.some(v => !Number.isFinite(v))) { complete = false; continue; }
        const sum = vals.reduce((a, b) => a + b, 0);
        rawTotal += sum;
        out[name] = +(sum * weight).toFixed(2);
    }

    // The total is not the sum of the three weighted subscales — it is the sum
    // of the raw subscale totals, weighted once by 3.74.
    if (complete) out.ssq_total = +(rawTotal * 3.74).toFixed(2);
    return out;
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
            agentPre:  `Let me create that for you.`,
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
            agentPre:  `I'll move it for you.`,
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
        // Nothing is wrong with what they said. The feedback owns the limit and
        // offers a workable number; it must never suggest they rephrase, because
        // there is nothing to rephrase.
        error: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true, count: 8,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color,
            errorText: `${n} ${o}s is beyond what this system can render. I created ` +
                       `8 instead. A few dozen is the practical limit here.`,
            agentPre:  `Let me try to place those.`,
            agentPost: `Your request was clear, but ${n} ${o}s is more than I can ` +
                       `handle at once. I have made 8. Would a smaller number work?`,
            missingSlot: "a smaller number",
            // Deliberately conservative: only an explicit smaller quantity counts.
            // Bare agreement ("ok", "fine") is excluded because those strings
            // appear inside ordinary speech and would inflate the measure. A
            // false negative costs a data point; a false positive corrupts the
            // claim that these repairs are usable training signal.
            slotTerms: ["fewer", "less", "smaller", "reduce", "reduced", "lower",
                        "ten", "twenty", "thirty", "fifty", "hundred", "dozen",
                        "5", "8", "10", "12", "15", "20", "25", "30", "50", "100"]
        },
        // Their revised, smaller request is honoured — visibly more than the 8
        // the limit produced, so the adaptation is seen to have worked.
        success: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true, count: 20,
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
            agentPre:  `Creating it now.`,
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

/**
 * Task 5 - user error: the goal was described, but the setting that achieves it
 * was never given.
 *
 * The omission has to be one people actually make, or the feedback is a lie and
 * anyone who believes it gets scored wrong for doing so. This one qualifies:
 * people state the intention ("so it stands out") rather than the parameter that
 * produces it, and no system can infer a colour from a purpose.
 */
function buildTask5(v) {
    const o = v.object;
    return {
        label: `Create a ${o} that stands out`,
        scenario: "user_error",
        prompt: `The ground around the campfire is dark and hard to read. Ask the ` +
                `system to put a ${o} on the ground that clearly stands out ` +
                `against it. Use your own words.`,
        error: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: "#6b6b6b",
            errorText: `The ${o} was created, but no colour was given, so it used ` +
                       `the default grey and blends into the ground.`,
            agentPre:  `Placing it on the ground.`,
            agentPost: `I made the ${o}, but you didn't say what colour, so it came ` +
                       `out grey and it doesn't stand out.`,
            missingSlot: "a colour",
            // Colour words match unusually cleanly: concrete, not common filler,
            // and no ambiguity about whether one was actually supplied.
            slotTerms: ["red", "orange", "yellow", "green", "blue", "purple",
                        "pink", "white", "bright", "brightly", "colour", "color",
                        "coloured", "colored", "glowing"]
        },
        success: {
            action: "spawn", shape: v.shape, pos: "floor", physics: true,
            scaleX: v.scale, scaleY: v.scale, scaleZ: v.scale, color: v.color
        }
    };
}

/**
 * Task 6 - system limitation: the request is clear, reasonable, and outside what
 * the system can do at all.
 *
 * Distinct from task 3, which is a ceiling on something supported. This is a
 * capability that does not exist, so no rewording anywhere reaches it. It is the
 * purest test of the study's claim: the only useful response is to stop trying
 * and ask for something else, and a participant who blames their own phrasing
 * cannot get there.
 */
function buildTask6(v) {
    const o = v.object;
    return {
        label: `Make the ${o} move on its own`,
        scenario: "system_capability",
        prompt: `Everything in this scene is still. Ask the system to make the ` +
                `${o} keep moving on its own, without you asking each time. ` +
                `Use your own words.`,
        error: {
            action: "noop",
            errorText: `Nothing was changed. This system can create, move and ` +
                       `recolour objects. It cannot animate them, so there is no ` +
                       `wording that would achieve this.`,
            agentPre:  `Let me try.`,
            agentPost: `Your request was clear, but I can only create, move and ` +
                       `recolour things. Continuous movement isn't something I can ` +
                       `do at all. Would one of those work instead?`,
            missingSlot: "a supported operation",
            // Credit only an explicit pivot to something the system can do.
            // Bare agreement is excluded for the same reason as task 3.
            //
            // "move"/"moving" are deliberately NOT here even though moving is
            // supported: the request itself is about movement, so the opening
            // utterance contains them and would score as an adaptation before
            // the participant had adapted to anything. A pivot to moving still
            // registers via "instead".
            slotTerms: ["colour", "color", "recolour", "recolor", "red", "orange",
                        "yellow", "green", "blue", "purple", "white",
                        "create", "spawn", "instead"]
        },
        success: { action: "recolor", target: v.target, color: v.color }
    };
}

const TASKS = {
    // Practice. Not counterbalanced, not analysed, always run first. Without it
    // the first real task doubles as push-to-talk training, and whatever that
    // costs in attempts and time lands on whichever task the square put first.
    // It always succeeds: the participant must not meet a failure until the
    // measured tasks begin.
    practice: {
        name: "Practice (not analysed)",
        scenario: "practice",
        practice: true,
        variants: {
            v1: {
                label: "Practice: change the cube's colour",
                scenario: "practice",
                prompt: `Before we start, a practice round so you can get used to ` +
                        `talking to the system. Hold the trigger, ask it to change ` +
                        `the cube to any colour you like, then let go. Nothing here ` +
                        `is being recorded as part of the study.`,
                error: {
                    action: "noop",
                    errorText: "",
                    agentPost: "",
                    missingSlot: "",
                    slotTerms: []
                },
                success: { action: "recolor", target: "cube", color: "#39a0ed" }
            }
        }
    },
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
    },
    task5: {
        name: "Create an object that stands out",
        scenario: "user_error",
        variants: {
            v1: buildTask5({ object: "marker", shape: "cube",    scale: 0.20, color: "#ff4d4d" }),
            v2: buildTask5({ object: "stone",  shape: "sphere",  scale: 0.20, color: "#ffd166" }),
            v3: buildTask5({ object: "post",   shape: "capsule", scale: 0.22, color: "#4dd2ff" })
        }
    },
    task6: {
        name: "Animate an object (not supported)",
        scenario: "system_capability",
        variants: {
            v1: buildTask6({ object: "campfire", target: "campfire", color: "#ff8c42" }),
            v2: buildTask6({ object: "sphere",   target: "sphere",   color: "#4d9fff" }),
            v3: buildTask6({ object: "cube",     target: "cube",     color: "#5cd65c" })
        }
    }
};

// Ground-truth attribution, fixed per task because task IS scenario type here.
// Tasks 1-2 are the participant's doing; tasks 3-4 are not.
const TASK_ATTRIBUTION = {
    practice: "",          // not scored
    task1: "self",
    task2: "self",
    task5: "self",
    task3: "system",
    task4: "system",
    task6: "system"
};

// ── Counterbalancing ─────────────────────────────────────────────────────────
//
// BETWEEN-SUBJECTS, 30 participants, 10 per feedback condition.
// Each participant experiences ONE condition and does all SIX measured tasks.
//
//   condition(p)   = CONDITIONS[(p-1) mod 3]        -> exactly 10 each over P1..P30
//   task order(p)  = Williams row floor((p-1)/3) mod 6
//   variant(p,t)   = ((p-1) + t) mod 3
//
// SIX tasks, not four. The primary model estimates a per-participant random
// intercept, and with four trials that is two binary observations per cell -
// too thin to identify the random effect, and liable to converge badly. Three
// tasks per fault type roughly halves the per-cell noise for about eight extra
// minutes of session time, which buys more than ten additional participants
// would. Trials per person, not people, was the binding constraint.
//
// Conditions are interleaved rather than blocked (A,B,C,A,B,C...) so that any
// drift over the recruitment period - the wizard getting smoother, seasonal
// participant differences - spreads across all three groups instead of loading
// onto whichever was run first.
//
// Task order uses a balanced (Williams) square: each task appears in each
// position equally often AND precedes every other equally often, which a plain
// rotation does not give. That matters because the system-fault tasks teach
// participants the system has limits, which would otherwise colour how they read
// the user-fault ones.
//
// NOTE the order index is floor((p-1)/3), not (p-1). With six orders and three
// conditions, indexing both by (p-1) would make order a deterministic function
// of condition - condition A would only ever see orders 0 and 3 - which is a
// confound, not a counterbalance. Dividing by 3 first advances the order once
// per full A/B/C cycle, so every condition meets every order.
//
// 30 does not divide evenly into 3 conditions x 6 orders = 18 cells, so order
// coverage within a condition is near-balanced rather than exact (orders 0-3
// appear twice per condition, 4-5 once). Perfect balance would need n=36.

// BETWEEN-SUBJECTS on condition, WITHIN-SUBJECTS on fault type.
//
// One participant sees exactly one feedback condition, and inside it meets six
// failures: three caused by their own underspecification, three caused by the
// system. That split is deliberate and it is what the headline claim rests on —
// people misattribute system faults as their own and spend repair turns that
// cannot work. Because both fault types happen to every participant, that
// comparison is within-person and each participant is their own control.
//
// Condition is the weaker, between-participant factor: ten people per cell can
// only detect a large effect, which is why feedback modality is pre-registered
// as an exploratory moderator rather than a confirmatory test.
//
// Do not turn this into a within-subjects design without redoing the power
// analysis. Running all three conditions per person would triple the session,
// repeat every failure three times, and put learning directly on top of the
// primary measure.
const STUDY_DESIGN = "between";   // confirmed with supervisor, August 2026

const CONDITIONS = ["A", "B", "C"];
// Interleaved by fault type so that neither type is front-loaded if a session
// has to be cut short.
const TASK_KEYS    = ["task1", "task3", "task2", "task4", "task5", "task6"];
const VARIANT_KEYS = ["v1", "v2", "v3"];

const TARGET_N            = 30;
const PARTICIPANTS_PER_CONDITION = 10;

// Balanced (Williams) Latin square for 6 items: first row 0,1,n-1,2,n-2,3 with
// each subsequent row incremented by one, mod 6.
const TASK_ORDERS = [
    [0, 1, 5, 2, 4, 3],
    [1, 2, 0, 3, 5, 4],
    [2, 3, 1, 4, 0, 5],
    [3, 4, 2, 5, 1, 0],
    [4, 5, 3, 0, 2, 1],
    [5, 0, 4, 1, 3, 2]
];

/** Participant number from an id (P01 -> 1, 7 -> 7). 1-based. */
function participantIndex(pid) {
    const digits = String(pid || "").match(/\d+/);
    if (digits) return parseInt(digits[0], 10);
    let h = 0;
    for (const ch of String(pid || "")) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
    return (h % TARGET_N) + 1;
}

// ── Allocation ───────────────────────────────────────────────────────────────
//
// Condition used to be derived from the participant number, which forced
// A,B,C,A,B,C and made "run ten of condition A this week" impossible. Sessions
// get scheduled around room bookings and who turns up, not around a modulo.
//
// So the researcher picks the condition and the system picks everything else:
// it reads what has already been assigned, and hands out the least-used task
// order and variant offset WITHIN that condition. Balance therefore holds no
// matter what sequence the conditions are actually run in.
//
// Persisted, because it has to survive a server restart mid-study. Losing it
// would silently re-issue order 0 to everyone from that point on, and nothing
// downstream would look wrong.
//
// Assignment is idempotent: a participant who already has one keeps it. A
// session restarted halfway through must not be handed a different task order
// than the one already half-written into their file.
const ALLOCATION_FILE = path.join(LOG_DIR, "allocation.json");

// Practice runs write here instead of Logs/, so a rehearsal can never be picked
// up by an analysis that globs the participant files. Kept as a directory
// rather than a filename prefix because a prefix is one typo away from landing
// in the real set, and because "delete the whole folder" is then a safe thing
// to say.
const PRACTICE_DIR = path.join(LOG_DIR, "practice");

// Sign-ups live apart from the study data on purpose: this file is the only one
// holding names and email addresses, so the participant files stay
// non-identifiable and this one can be deleted at the end of the study without
// touching anything that gets analysed.
const SIGNUP_FILE = path.join(LOG_DIR, "signups.csv");
const SLOTS_FILE  = path.join(LOG_DIR, "slots.json");

// Edited by the researcher, read fresh on every request so slots can be added
// mid-study without a restart. Seeded with two, for the pilot.
const DEFAULT_SLOTS = {
    config: {
        infoSheetUrl: "REPLACE-WITH-YOUR-INFORMATION-SHEET-URL",
        contactEmail: "REPLACE-WITH-YOUR-EMAIL"
    },
    slots: [
        { id: "s1", when: "Monday 11 August, 10:00", capacity: 1 },
        { id: "s2", when: "Monday 11 August, 14:00", capacity: 1 }
    ]
};

function readSlots() {
    try { return JSON.parse(fs.readFileSync(SLOTS_FILE, "utf8")); }
    catch (_) {
        fs.mkdirSync(LOG_DIR, { recursive: true });
        fs.writeFileSync(SLOTS_FILE, JSON.stringify(DEFAULT_SLOTS, null, 2));
        return DEFAULT_SLOTS;
    }
}

/** How many people have already booked each slot. */
function signupCounts() {
    const counts = {};
    try {
        const lines = fs.readFileSync(SIGNUP_FILE, "utf8").trim().split("\n").slice(1);
        for (const line of lines) {
            const id = (line.split(",")[1] || "").replace(/^"|"$/g, "");
            if (id) counts[id] = (counts[id] || 0) + 1;
        }
    } catch (_) {}
    return counts;
}

function readAllocations() {
    try { return JSON.parse(fs.readFileSync(ALLOCATION_FILE, "utf8")); }
    catch (_) { return {}; }
}

function writeAllocations(all) {
    fs.mkdirSync(LOG_DIR, { recursive: true });
    fs.writeFileSync(ALLOCATION_FILE, JSON.stringify(all, null, 2));
}

/** Index of the least-used value, so repeats only begin once every option has been used. */
function leastUsed(counts) {
    const min = Math.min(...counts);
    return counts.indexOf(min);
}

/**
 * What this participant should run. Pass commit:true to actually reserve it.
 *
 * Peeking must not consume a slot — the panel previews a plan every time an ID
 * is typed, and reserving on preview would burn allocations on typos.
 */
function allocate(pid, requestedCondition, { commit = false } = {}) {
    const all = readAllocations();
    if (all[pid]) return { ...all[pid], existing: true };

    const asked = String(requestedCondition || "").trim().toUpperCase();
    const condition = CONDITIONS.includes(asked) ? asked : CONDITIONS[0];

    const inCondition = Object.values(all).filter(a => a.condition === condition);
    const orderIndex = leastUsed(
        TASK_ORDERS.map((_, i) => inCondition.filter(a => a.orderIndex === i).length));

    // Variant is chosen least-used-first like the order, but ties are broken by
    // how often that variant has already been paired with THIS task order.
    // Without the tie-break both counters advance in step, variant becomes a
    // function of order, and a variant effect could never be told apart from an
    // order effect — a confound that would only surface at analysis.
    const variantOffset = VARIANT_KEYS
        .map((_, i) => ({
            i,
            overall: inCondition.filter(a => a.variantOffset === i).length,
            withOrder: inCondition.filter(
                a => a.variantOffset === i && a.orderIndex === orderIndex).length
        }))
        .sort((x, y) => x.overall - y.overall
                     || x.withOrder - y.withOrder
                     || x.i - y.i)[0].i;

    const rec = {
        condition, orderIndex, variantOffset,
        assignedAt: new Date().toISOString()
    };
    if (commit) { all[pid] = rec; writeAllocations(all); }
    return { ...rec, existing: false };
}

/** How full each condition is, for the panel. */
function allocationSummary() {
    const all = readAllocations();
    return CONDITIONS.map(c => {
        const inC = Object.values(all).filter(a => a.condition === c);
        return {
            condition: c,
            n: inC.length,
            target: PARTICIPANTS_PER_CONDITION,
            ordersUsed: TASK_ORDERS.map((_, i) => inC.filter(a => a.orderIndex === i).length)
        };
    });
}

/** The six tasks in an assigned order, with a content variant for each. */
function buildPlan(condition, orderIndex, variantOffset) {
    const order = TASK_ORDERS[orderIndex % TASK_ORDERS.length];

    return order.map((taskIdx, position) => {
        const taskKey = TASK_KEYS[taskIdx];
        const variant = VARIANT_KEYS[(variantOffset + position) % VARIANT_KEYS.length];
        const task = TASKS[taskKey];
        return {
            // Position within this participant's six tasks. Named `block` for
            // the CSV's sake; it is trial 1..6, nothing more.
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

/**
 * The plan a participant will run. Peeks at the allocation without reserving it,
 * so previewing an id in the panel is free.
 */
function planForParticipant(pid, requestedCondition) {
    const a = allocate(pid, requestedCondition, { commit: false });
    return buildPlan(a.condition, a.orderIndex, a.variantOffset);
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
            condition:     "",   // "A" | "B" | "C" — this participant's condition
            block:         1,    // which task of the plan we're on
            startedAt:     null,
            plan:          [],   // counterbalanced six-task plan
            // Set once every task is done and cleared when the post-condition
            // questionnaire arrives, so the panel can hold the session open
            // rather than reporting it finished with a form still outstanding.
            awaitingQuestionnaire: false,
            finishedAt:    null,
            // Practice runs are quarantined: no allocation, separate folder.
            practice:      false
        };

        // Every object the study has created and not yet destroyed.
        // id -> { shape, color, pos, spawnedAt, x, y, z, condition, block, trial }
        //
        // The server knows what it asked the headset to build, so the object's
        // life is recorded here rather than waiting on Unity to report it. That
        // matters for two reasons: the coordinates land in the CSV whether or
        // not the APK has been rebuilt, and "how long did it stay" is only
        // answerable by whoever saw both the spawn and the destroy.
        this.sceneObjects = new Map();
        this.objectCounter = 0;

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
            // From POST /attribution-detail. Null rather than "" so an unasked
            // probe cannot be read as an answer.
            //
            // The graded 0-10 blame split and the confidence rating used to sit
            // beside this one, asked after every failure. They now live in the
            // post-session questionnaire instead. This item stays per-trial
            // because it cannot survive the move: asked at the end it is a
            // memory of what they believed; asked here, before they try again,
            // it is the belief the next attempt acts on.
            perceivedReparability: null,
            // Manipulation check. Without it, "feedback made no difference"
            // cannot be told apart from "they never registered the feedback",
            // and in condition A it records whether they noticed the failure
            // at all with nothing to tell them.
            noticedFeedback:   null,   // filled by POST /noticed
            // What they DID about it, which is where misattribution becomes a
            // cost rather than an opinion. Stated blame is cheap; the repair
            // move is the thing that wastes a turn or fixes the problem, and it
            // is the only part of this a real product could act on.
            repairStrategies:  [],     // filled by POST /repair-strategy
            // True when the opening utterance already contained the slot the
            // scripted error claims was missing. Those trials are excludable:
            // the feedback contradicted the participant.
            preInjectHadSlot:  false,
            // Independent, automatic evidence about repair behaviour, so the
            // wizard's live coding is checkable rather than merely trusted.
            lastUtterance:     null,
            utteranceSimilarities: [],   // consecutive-pair overlap, in order
            msToFirstRepair:   null,     // inject -> next utterance = noticing
            repairContainsSlot: null,  // filled automatically on next transcript
            startedAt:         Date.now(),
            startedAtIso:      new Date().toISOString(),
            endedAt:           null,
            status:            "in-progress",
            attempts:          0,   // participant utterances
            injects:           0,   // wizard injections
            successInjected:   false // set once the resolved outcome is sent
        };

        this.logEvent("trial-start", `${task}/${variant}/${scenario}`,
            { task, variant, scenario, attempt: 0, source: "wizard", category: "trial" });
        console.log(`\x1b[35m[Trial ${this.trialCounter}]\x1b[0m start ` +
            `condition=${this.session.condition} task=${task} variant=${variant} scenario=${scenario}`);
        return { ok: true, trial: this.trial };
    }

    /**
     * Ends the active trial and writes its summary row.
     * status: "completed" (participant recovered) | "failed" | "abandoned"
     */
    completeTrial(status = "completed", { advance = false } = {}) {
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
            source: "wizard", category: "trial", value: status,
            msSinceTrialStart: this.trial.endedAt - this.trial.startedAt
        });
        console.log(`\x1b[35m[Trial ${this.trial.number}]\x1b[0m ${status} ` +
            `attempts=${this.trial.attempts} ` +
            `duration=${Math.round((this.trial.endedAt - this.trial.startedAt) / 1000)}s`);

        const trial = this.trial;
        if (!advance) return { ok: true, trial };
        // Ending a trial and loading the next one were two buttons, and the
        // second was easy to forget under load — which left the panel sitting on
        // a finished trial while the participant waited. There is no case where
        // a trial ends and the plan should not move on; going off-plan is what
        // POST /next-block and the task chips are for.
        return { ok: true, trial, ...this.advanceBlock({ endTrial: false }) };
    }

    /**
     * Finishes the current condition and moves to the next assigned one: applies
     * the next block's condition on the headset, loads its assigned task and
     * clears the scene. This is the "after each condition" automation — the
     * Wizard presses one button rather than remembering four steps.
     */
    advanceBlock({ endTrial = true } = {}) {
        if (endTrial && this.trial && !this.trial.endedAt) this.completeTrial("completed");

        const total = (this.session.plan || []).length;
        const next = this.session.block + 1;
        if (next > total) {
            // Last measured task done. The session is not over — the
            // post-condition questionnaire is still outstanding — so say so
            // rather than reporting "finished" and leaving the panel with
            // nothing to point at.
            this.session.awaitingQuestionnaire = true;
            this.resetScene();
            this.logEvent("tasks-complete",
                `${total} of ${total} tasks done — questionnaire due`, {
                source: "wizard", category: "session", value: total
            });
            console.log(`\x1b[35m[Session]\x1b[0m all ${total} tasks done — ` +
                `post-condition questionnaire due`);
            return {
                ok: true, finished: true, questionnaireDue: true,
                session: this.session
            };
        }

        this.session.block = next;
        const planned = this.plannedBlock(next);
        this.session.condition = planned.condition;
        this.activeTask    = planned.task;
        this.activeVariant = planned.variant;

        this.sendControl("condition", this.session.condition);
        this.resetScene();
        this.logSessionStart();
        this.logEvent("task-advance", `task ${next} of ${total} → ${planned.task}/${planned.variant}`,
            { task: planned.task, attempt: 0,
              source: "wizard", category: "session", value: next });
        console.log(`\x1b[35m[Block ${next}]\x1b[0m condition=${planned.condition} task=${planned.task}`);

        return { ok: true, finished: false, session: this.session, planned };
    }

    /**
     * Closes the participant out: the last objects are accounted for, the file
     * is stamped complete, and the panel is released for the next person.
     *
     * Separate from the questionnaire arriving, because the two answer
     * different questions — the questionnaire is the participant's last task,
     * this is the researcher confirming the session is over and the data is
     * good. Debriefing happens between them.
     */
    endStudy() {
        const pid = this.session.participantId;
        if (!pid) return { ok: false, error: "No session running" };

        this.trackDestroyAll("end-of-session");
        this.logEvent("study-complete",
            `condition ${this.session.condition} complete — participant finished`, {
            source: "wizard", category: "session", value: this.session.condition
        });

        this.session.awaitingQuestionnaire = false;
        this.session.finishedAt = new Date().toISOString();

        const file = path.join(this.session.practice ? PRACTICE_DIR : LOG_DIR, `${pid}.csv`);
        const rows = fs.existsSync(file)
            ? fs.readFileSync(file, "utf8").trim().split("\n").length - 1 : 0;
        console.log(`\x1b[1m\x1b[32m[Session]\x1b[0m ${pid} finished — ` +
            `${rows} rows in Logs/${pid}.csv`);
        return { ok: true, session: this.session, file: `${pid}.csv`, rows };
    }

    // ── Data logging ─────────────────────────────────────────────────────────
    //
    // ONE FILE PER PARTICIPANT. Logs/P01.csv holds everything P01 did: every
    // event, every trial summary, every questionnaire answer, in the order it
    // happened.
    //
    // It used to be five: {pid}_events.csv, {pid}_condition.csv,
    // {pid}_background.csv, plus study-wide trials.csv and sessions.csv — and
    // then a retired copy of each whenever a column was added, which during
    // development was often. Finding what one participant did meant opening
    // five files and filtering three of them, and the retired copies made it
    // genuinely unclear which file was current.
    //
    // The cost of one file is one wide schema shared by every row type, so most
    // columns are blank in most rows. That is the right trade here: `recordType`
    // says what a row is, disk is free, and a row that carries its own full
    // context can be read without reconstructing state from the rows above it.
    //
    // Because every row is written through LOG_COLUMNS by name, a row can never
    // drift out of step with the header — which is what forced the retirements.

    /** Column order for the participant file. Append only; never reorder. */
    logRow(recordType, fields = {}) {
        const pid = fields.participantId
            || this.session.participantId
            // Before a session is started this is warm-up or testing, and must
            // not land in a participant's file.
            || "warmup";
        const now = Date.now();
        this.eventSeq = (this.eventSeq || 0) + 1;

        const row = {
            seq: this.eventSeq,
            timestampIso: new Date(now).toISOString(),
            epochMs: now,
            msSinceSessionStart: this.sessionStartedAt ? now - this.sessionStartedAt : "",
            participantId: pid,
            condition: this.session.condition || "",
            block: this.session.block || "",
            recordType,
            ...fields
        };

        const dir = this.session.practice ? PRACTICE_DIR : LOG_DIR;
        if (this.session.practice) fs.mkdirSync(dir, { recursive: true });
        appendCsv(
            path.join(dir, `${pid}.csv`),
            LOG_COLUMNS.join(","),
            LOG_COLUMNS.map(name => csvEscape(
                row[name] === undefined || row[name] === null ? "" : String(row[name])
            )).join(",")
        );
    }


    /**
     * One row per event, and an event is anything that changes state.
     *
     * Deliberately wide and deliberately redundant. Every row carries its own
     * full context (participant, condition, block, trial, task, variant,
     * scenario, attempt) so that any single row is interpretable on its own and
     * no analysis step depends on carrying state forward correctly from earlier
     * rows. Disk is free; a reconstruction error found six months after the last
     * participant has gone home is not.
     *
     * Three clocks, because each answers a different question and none of them
     * substitutes for the others:
     *   epochMs               absolute, for merging against video and audio
     *   msSinceSessionStart   position within the session, for fatigue and drift
     *   msSinceTrialStart     position within the trial, the analysis unit
     *
     * `seq` is a monotonic counter. Two events inside the same millisecond are
     * common (an inject and the feedback it causes), and without a sequence
     * number their order in the file is the only evidence of which came first,
     * which is not something to rely on after a sort.
     */
    logEvent(type, detail = "", extra = {}) {
        const now = Date.now();
        const pos = extra.pos || {};
        const from = extra.from || {};
        this.logRow("event", {
            // Scene-object columns. Blank on every row that is not about an
            // object, which is most of them.
            objectId:    extra.objectId    !== undefined ? extra.objectId    : "",
            objectShape: extra.objectShape !== undefined ? extra.objectShape : "",
            color:       extra.color       !== undefined ? extra.color       : "",
            prevColor:   extra.prevColor   !== undefined ? extra.prevColor   : "",
            fromX: from.x !== undefined ? from.x : "",
            fromY: from.y !== undefined ? from.y : "",
            fromZ: from.z !== undefined ? from.z : "",
            lifetimeMs:  extra.lifetimeMs  !== undefined ? extra.lifetimeMs  : "",
            msSinceTrialStart: extra.msSinceTrialStart !== undefined
                ? extra.msSinceTrialStart
                : (this.trial && !this.trial.endedAt ? now - this.trial.startedAt : ""),
            trial: this.trial ? this.trial.number : "",
            task: extra.task || this.activeTask,
            variant: extra.variant || (this.trial ? this.trial.variant : ""),
            scenario: extra.scenario !== undefined ? extra.scenario
                : (this.trial ? this.trial.scenario : ""),
            attempt: extra.attempt !== undefined ? extra.attempt : this.attemptCount(),
            // Who caused this row to exist. Without it, "the participant paused"
            // and "the wizard paused" are the same silence in the data.
            source: extra.source || "system",
            category: extra.category || "other",
            eventType: type,
            detail,
            value: extra.value !== undefined ? extra.value : "",
            target: extra.target || "",
            posX: pos.x !== undefined ? pos.x : "",
            posY: pos.y !== undefined ? pos.y : "",
            posZ: pos.z !== undefined ? pos.z : "",
            yaw: extra.yaw !== undefined ? extra.yaw : ""
        });
    }

    // ── Scene object lifecycle ───────────────────────────────────────────────
    //
    // Everything the study puts in the world gets a row when it appears, a row
    // every time it changes, and a row when it goes — carrying position, colour
    // and, on the last row, how long it existed.
    //
    // Positions come from the spec's position label until the headset reports
    // the real transform via /scene-event, at which point the registry is
    // corrected and a follow-up row records the measured coordinates. Both are
    // kept: the label is what was asked for, the transform is what happened,
    // and a study about mismatch between intent and outcome should not throw
    // away either one.

    /** Nominal world coordinates for each position label the specs use. */
    static get POSITION_COORDS() {
        return {
            hand:   { x: 0.0, y: 1.4, z:  0.5 },
            offset: { x: 0.0, y: 1.4, z:  0.9 },
            high:   { x: 0.0, y: 2.0, z:  0.3 },
            floor:  { x: 0.0, y: 0.1, z:  0.5 },
            behind: { x: 0.0, y: 1.2, z: -2.5 },
            front:  { x: 0.0, y: 1.4, z:  2.0 },
            origin: { x: 0.0, y: 0.0, z:  0.0 }
        };
    }

    coordsFor(posLabel) {
        return WizardOfOzApp.POSITION_COORDS[posLabel] || { x: "", y: "", z: "" };
    }

    /**
     * The three objects the scene always contains — the sphere, the cube and
     * the campfire the briefings refer to. StudyOutcomes rebuilds them on every
     * clear, so they are re-registered here on every clear too.
     *
     * They have to be in the registry even though the study did not create
     * them: the move and recolour tasks act on the cube and the sphere, and
     * without a record of where they started, "moved from" and "was previously
     * this colour" have nothing to report. Coordinates mirror EnsureSceneObjects
     * in StudyOutcomes.cs (0.5 m either side of a point 1.6 m in front of the
     * origin); the headset corrects them via /scene-event when it reports.
     */
    seedBaselineObjects() {
        const baseline = [
            { shape: "sphere",   color: "#cccccc", x: -0.5, y: 0.1, z: 1.6 },
            { shape: "cube",     color: "#cccccc", x:  0.5, y: 0.1, z: 1.6 },
            { shape: "campfire", color: "#ff7a1a", x:  0.0, y: 0.0, z: 1.6 }
        ];
        for (const b of baseline) {
            const id = `obj${++this.objectCounter}`;
            this.sceneObjects.set(id, {
                id, shape: b.shape, color: b.color, pos: "scene",
                x: b.x, y: b.y, z: b.z, spawnedAt: Date.now(), baseline: true
            });
            this.logEvent("object-spawned", `${b.shape} (scene baseline)`, {
                source: "system", category: "scene",
                objectId: id, objectShape: b.shape, color: b.color,
                target: b.shape, value: "baseline",
                pos: { x: b.x, y: b.y, z: b.z }
            });
        }
    }

    /** Records an object appearing, and starts its clock. */
    trackSpawn(spec, extra = {}) {
        const p = this.coordsFor(spec.pos);
        // count > 1 spawns a cluster; each member is tracked separately so the
        // destroy rows account for all of them rather than one representative.
        const n = Math.max(1, Number(spec.count) || 1);
        const ids = [];
        for (let i = 0; i < n; i++) {
            const id = `obj${++this.objectCounter}`;
            ids.push(id);
            const rec = {
                id,
                shape: spec.shape || "",
                color: spec.color || "",
                pos:   spec.pos || "",
                x: p.x, y: p.y, z: p.z,
                spawnedAt: Date.now()
            };
            this.sceneObjects.set(id, rec);
            this.logEvent("object-spawned", `${rec.shape} at ${rec.pos}`, {
                source: "system", category: "scene",
                objectId: id, objectShape: rec.shape, color: rec.color,
                target: rec.shape, value: rec.pos,
                pos: { x: rec.x, y: rec.y, z: rec.z },
                ...extra
            });
        }
        return ids;
    }

    /** The most recently registered object with this shape/name. */
    findObject(name) {
        const want = String(name || "").toLowerCase();
        if (!want) return null;
        return [...this.sceneObjects.values()].reverse()
            .find(o => String(o.shape).toLowerCase() === want) || null;
    }

    /**
     * Records an object moving, from wherever the registry last had it.
     *
     * Move specs name a destination two different ways: `pos` is one of the
     * fixed position labels, `moveTo` is another object ("move the sphere to
     * the campfire"). Both have to resolve to coordinates or the destination
     * columns stay blank on exactly the tasks that are about moving things.
     *
     * `away: true` is the scripted failure — it went the opposite way. Modelled
     * as the same distance mirrored back through the start, which is what
     * "the other way" means geometrically and what the headset does.
     */
    trackMove(spec, extra = {}) {
        const target = String(spec.target || "").toLowerCase();
        const rec = this.findObject(target);
        const from = rec ? { x: rec.x, y: rec.y, z: rec.z } : {};

        let to = {};
        let destLabel = "";
        if (spec.moveTo) {
            destLabel = String(spec.moveTo);
            const dest = this.findObject(spec.moveTo);
            if (dest) to = { x: dest.x, y: dest.y, z: dest.z };
        } else if (spec.pos) {
            destLabel = String(spec.pos);
            to = this.coordsFor(spec.pos);
        }

        const numeric = v => typeof v === "number" && Number.isFinite(v);
        if (spec.away && numeric(from.x) && numeric(to.x)) {
            to = {
                x: +(2 * from.x - to.x).toFixed(3),
                y: +(2 * from.y - to.y).toFixed(3),
                z: +(2 * from.z - to.z).toFixed(3)
            };
        }

        // Only commit real numbers. Writing an unresolved destination back into
        // the registry blanks the object's position, and every row about it
        // after that loses its coordinates too.
        if (rec && numeric(to.x)) {
            rec.x = to.x; rec.y = to.y; rec.z = to.z;
            rec.pos = destLabel || rec.pos;
        }

        this.logEvent("object-moved",
            `${target || "object"} → ${destLabel || "?"}${spec.away ? " (away — scripted failure)" : ""}`, {
            source: "system", category: "scene",
            objectId: rec ? rec.id : "", objectShape: rec ? rec.shape : target,
            color: rec ? rec.color : "",
            target, value: destLabel + (spec.away ? ":away" : ""),
            from, pos: to,
            ...extra
        });
    }

    /** Records a colour change, keeping the colour it replaced. */
    trackRecolor(spec, extra = {}) {
        const target = String(spec.target || spec.shape || "").toLowerCase();
        const rec = this.findObject(target);
        const prev = rec ? rec.color : "";
        if (rec) rec.color = spec.color || rec.color;
        this.logEvent("object-recoloured", `${target || "object"} ${prev || "?"} → ${spec.color || "?"}`, {
            source: "system", category: "scene",
            objectId: rec ? rec.id : "", objectShape: rec ? rec.shape : target,
            color: spec.color || "", prevColor: prev,
            target, value: spec.color || "",
            pos: rec ? { x: rec.x, y: rec.y, z: rec.z } : {},
            ...extra
        });
    }

    /**
     * Records every live object being destroyed, one row each, with how long it
     * survived. Called by the scene reset, which is the only thing that removes
     * objects — so this is where lifetime is finally known.
     */
    trackDestroyAll(reason = "scene-reset") {
        const now = Date.now();
        for (const rec of this.sceneObjects.values()) {
            this.logEvent("object-destroyed", `${rec.shape} (${reason})`, {
                source: "system", category: "scene",
                objectId: rec.id, objectShape: rec.shape, color: rec.color,
                target: rec.shape, value: reason,
                pos: { x: rec.x, y: rec.y, z: rec.z },
                lifetimeMs: now - rec.spawnedAt
            });
        }
        this.sceneObjects.clear();
    }

    /**
     * One row per completed trial — the primary analysis unit. Carries every
     * field the implementation note asks for, so per-condition comparisons need
     * no joining against the event log.
     */
    logTrial(trial) {
        // Between-subjects: condition is constant within a participant, so the
        // task sequence is what actually varies and what order effects are
        // checked against.
        const order = (this.session.plan || []).map(p => p.task.replace("task", "")).join("-");
        this.logRow("trial-summary", {
            msSinceTrialStart: trial.endedAt ? trial.endedAt - trial.startedAt : "",
            trial: trial.number,
            task: trial.task,
            variant: trial.variant,
            scenario: trial.scenario,
            source: "system",
            category: "trial",
            taskOrder: order,
            startTime: trial.startedAtIso,
            endTime: trial.endedAtIso || "",
            durationMs: trial.endedAt ? trial.endedAt - trial.startedAt : "",
            completionStatus: trial.status,
            attempts: trial.attempts,
            injects: trial.injects,
            attribution: trial.attribution || "",
            correctAttribution: trial.correctAttribution || "",
            attributionCorrect: trial.attribution !== null
                ? (trial.attribution === trial.correctAttribution ? "yes" : "no")
                : "",
            perceivedReparability: trial.perceivedReparability || "",
            // Whether that belief matched the scenario. Blank rather than "no"
            // when unasked, so an unrecorded probe cannot be read as a wrong one.
            reparabilityCorrect: trial.perceivedReparability
                ? (trial.perceivedReparability ===
                   (trial.correctAttribution === "system" ? "no" : "yes") ? "yes" : "no")
                : "",
            noticedFeedback: trial.noticedFeedback || "",
            // First move is the clean primary: it is the one taken on the
            // strength of the feedback alone, before trial and error muddies it.
            firstRepairStrategy: (trial.repairStrategies && trial.repairStrategies[0]) || "",
            repairSequence: (trial.repairStrategies || []).join("|"),
            // Turns spent on a move that cannot work. The efficiency cost of
            // misattribution, in the unit a product would actually count.
            wastedRepairs: (trial.repairStrategies || []).filter(s => s === "verbatim").length,
            repairContainsSlot: trial.repairContainsSlot !== null ? trial.repairContainsSlot : "",
            preInjectHadSlot: trial.preInjectHadSlot ? "yes" : "no",
            msToFirstRepair: trial.msToFirstRepair !== null ? trial.msToFirstRepair : "",
            maxUtteranceSimilarity: (trial.utteranceSimilarities || []).length
                ? Math.max(...trial.utteranceSimilarities) : "",
            utteranceSimilarities: (trial.utteranceSimilarities || []).join("|"),
            missingSlot: trial.missingSlot || ""
        });
    }

    /** Records the assigned plan at the start of a session or a block. */
    logSessionStart() {
        // Between-subjects: condition is constant within a participant, so
        // `conditionOrder` is that one condition. What actually varies — and
        // what order effects are checked against — is the task sequence, which
        // goes in `taskOrder`, the same column the trial summaries use so the
        // two line up without a join.
        const taskOrder = (this.session.plan || [])
            .map(p => p.task.replace("task", "")).join("-");
        // Records the assigned variant too, so the plan actually run is
        // recoverable from the data even if the table is later revised.
        const plan = (this.session.plan || [])
            .map(p => `${p.condition}:${p.task}:${p.variant}`).join(" ");
        this.logRow("session-start", {
            source: "wizard", category: "session",
            conditionOrder: this.session.condition || "", taskOrder, plan
        });
    }

    /**
     * Saves a questionnaire into the participant's file.
     *
     * Item sets differ between the background form and the post-condition one,
     * and dynamic columns are exactly what used to force a new file (and then a
     * retired copy of it) every time an item was added. Answers are held as one
     * JSON object in a single column instead, so any form fits the schema and
     * the questionnaire stays alongside the events it followed.
     */
    saveQuestionnaire(payload) {
        const pid = payload.participantId || this.session.participantId || "unknown";
        const type = payload.questionnaire === "background" ? "background" : "condition";
        const cond = payload.condition
            || (type === "background" ? "" : this.session.condition || "");
        const answers = payload.answers || {};
        const common = {
            participantId: pid,
            condition: cond,
            block: payload.block || (type === "background" ? "" : this.session.block || ""),
            source: "participant", category: "questionnaire",
            questionnaire: payload.questionnaire || type
        };

        // The archive row: every answer exactly as submitted, in one cell. Kept
        // because it is the only representation that cannot be wrong — if the
        // scoring below turns out to have a reversal backwards, this row still
        // has the raw data to rescore from.
        this.logRow("questionnaire", { ...common, answers: JSON.stringify(answers) });

        // One row per item. This is the form an analysis actually wants: no JSON
        // to parse out of a CSV cell, and the reversal already applied and
        // labelled so nobody has to remember which items were negatively worded.
        for (const [itemId, raw] of Object.entries(answers)) {
            if (raw === "" || raw === null || raw === undefined) continue;
            const n = Number(raw);
            const rev = QUESTIONNAIRE_REVERSED.has(itemId);
            const max = QUESTIONNAIRE_MAX[itemId] || 5;
            // Free-text interview answers have no score; they carry their text in
            // `detail` so they read the same way a transcript row does.
            const isText = !Number.isFinite(n);
            this.logRow("questionnaire-item", {
                ...common,
                itemId,
                itemRaw: raw,
                detail: isText ? raw : "",
                itemScore: isText ? "" : (rev ? (max + 1) - n : n),
                itemReversed: rev ? "yes" : "no",
                instrument: ITEM_INSTRUMENT[itemId] || ""
            });
        }

        // One row per scored scale. The numbers that go into the analysis.
        for (const [scaleName, score] of Object.entries(scoreQuestionnaire(answers))) {
            this.logRow("questionnaire-score", {
                ...common, scaleName, scaleScore: score,
                instrument: instrumentForScale(scaleName)
            });
        }

        return `${pid}.csv`;
    }

    registerComponents() {
        // Head pose from the headset. Movement-gated on the Unity side, so a row
        // here means the participant actually moved.
        this.components.telemetryReceiver = new MessageReader(this.scene, TELEMETRY_NETWORK_ID);
        this.components.telemetryReceiver.on("data", (data) => {
            let m;
            try { m = JSON.parse(data.message.toString()); } catch (_) { return; }
            if (!m || m.type !== "HeadPose") return;
            this.lastPose = { x: m.x, y: m.y, z: m.z, yaw: m.yaw, at: Date.now() };
            // Only LOGGED while a trial is live: pose between trials is the
            // researcher carrying the headset around and is noise in the file.
            // The mirror still shows it, because "where are they looking right
            // now" is useful during the briefing too.
            if (!this.trial || this.trial.endedAt) return;
            this.logEvent("head-pose", "", {
                source: "participant", category: "pose",
                pos: { x: m.x, y: m.y, z: m.z }, yaw: m.yaw
            });
        });

        this.components.audioReceiver = new MessageReader(this.scene, STT_NETWORK_ID);
        this.components.transcriptionService = new SpeechToTextService(this.scene, nconf.get());

        // Why the transcript is empty, rather than only that it is. Every one of
        // these used to be a console line the wizard never saw, so a dead
        // microphone, a room too quiet to clear the silence gate, and a
        // transcription server that was refusing connections all presented
        // identically: nothing appears.
        this.components.transcriptionService.on("diagnostic", (info) => {
            this.sttStatus = { ...info, at: Date.now() };
            if (info.kind === "silent" || info.kind === "error") {
                this.logEvent("stt-" + info.kind,
                    info.detail || `${info.durationMs}ms of audio, no speech detected`, {
                    source: "system", category: "warning"
                });
            }
        });
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

            // Drop only what carries no words at all. This used to require five
            // characters, which silently swallowed exactly the utterances the
            // study cares about most: "Red.", "Blue", "No.", "Stop." A dropped
            // transcript is invisible to the participant and to the wizard — the
            // world simply does not respond — and reads as the system not
            // listening, which is the one impression this study must not create
            // by accident.
            if (!/[a-z0-9]/i.test(text)) return;
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
                        source: "system", category: "measure", value: matched ? 1 : 0,
                        msSinceTrialStart: Date.now() - this.trial.startedAt
                    });
                }
            }

            // Before any injection: did they already supply the thing the error
            // is about to say they omitted?
            //
            // The scripted failure fires whatever they said, which is what makes
            // the stimulus identical across participants. The cost is that on a
            // user-fault task, someone who happened to give the missing detail
            // is then told they did not. For them the feedback is false, and
            // "blamed the system" becomes the correct reading of a trial we
            // would otherwise score as a mis-attribution.
            //
            // Flagged here rather than prevented: suppressing the error would
            // cost the trial and unbalance the design. The wizard sees the
            // warning and the analysis can exclude these trials.
            if (this.trial && !this.trial.endedAt && this.trial.injects === 0 &&
                this.trial.preInjectHadSlot !== true) {
                const already = slotMatched(text, this.trial.slotTerms);
                if (already === true) {
                    this.trial.preInjectHadSlot = true;
                    this.logEvent("pre-inject-slot-present",
                        `slot="${this.trial.missingSlot}" already said`, {
                        source: "system", category: "warning",
                        msSinceTrialStart: Date.now() - this.trial.startedAt
                    });
                    console.log(`\x1b[33m[Warning]\x1b[0m participant already gave ` +
                        `"${this.trial.missingSlot}" - the scripted error will ` +
                        `contradict them. Trial flagged.`);
                }
            }

            // Automatic repair evidence, computed before the transcript row is
            // written so the two can be read together.
            if (this.trial && !this.trial.endedAt) {
                const since = Date.now() - this.trial.startedAt;

                // How long after being shown the failure did they act? This is
                // the noticing measure the yes/no manipulation check cannot give:
                // a participant who says "yes I saw it" after twenty seconds of
                // silence did not notice it the way one who reacted in two did.
                if (this.trial.injects > 0 && this.trial.msToFirstRepair === null) {
                    this.trial.msToFirstRepair = since;
                    this.logEvent("first-repair-latency", String(since),
                        { msSinceTrialStart: since, source: "participant",
                          category: "measure", value: since });
                }

                if (this.trial.lastUtterance) {
                    const sim = utteranceSimilarity(this.trial.lastUtterance, text);
                    if (sim !== null) {
                        this.trial.utteranceSimilarities.push(sim);
                        // Word counts go alongside the score because overlap on
                        // its own cannot separate repeating from elaborating:
                        // "create a ball in my hand" inside "create a ball in my
                        // hand when I raise it above my shoulder" scores high, and
                        // that is a textbook good repair, not a repetition. High
                        // overlap AND similar length is a repeat; high overlap
                        // with growth is added detail.
                        const wc = t => String(t || "").trim().split(/\s+/).filter(Boolean).length;
                        this.logEvent("utterance-similarity",
                            `sim=${sim} prevWords=${wc(this.trial.lastUtterance)} ` +
                            `currWords=${wc(text)}`,
                            { msSinceTrialStart: since, source: "system",
                              category: "measure", value: sim });
                    }
                }
                this.trial.lastUtterance = text;
            }

            this.logEvent("transcript", text, {
                msSinceTrialStart: this.trial ? Date.now() - this.trial.startedAt : "",
                source: "participant", category: "speech"
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
        // Written before the reset row, so each object's destruction is on
        // record with its lifetime before the thing that caused it.
        this.trackDestroyAll("scene-reset");
        this.lastFeedback = null;
        this.logEvent("reset", "clear-scene", { source: "wizard", category: "scene" });
        // "clear" removes trial debris and rebuilds the sphere and cube, so the
        // participant always opens on the arrangement the briefing describes.
        this.sendControl("clear");
        // Rebuilt on the headset, so re-registered here — the move and recolour
        // tasks act on these.
        this.seedBaselineObjects();
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

        if (this.trial && !this.trial.endedAt) {
            this.trial.injects += 1;
            // Marks the trial as resolved. Without it the guide cannot tell
            // "still repairing" from "done", and parks on the repair step while
            // the participant waits.
            if (outcome === "success") this.trial.successInjected = true;
        }
        const msSinceTrialStart = this.trial ? Date.now() - this.trial.startedAt : "";

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m ${taskKey}/${vKey} ${outcome}` +
            (outcome === "success" ? "" : `/${scenario}`));
        const approx = this.coordsFor(spec.pos);
        this.logEvent("inject", outcome === "success" ? "success" : `error/${scenario}`, {
            task: taskKey, variant: vKey,
            scenario: outcome === "success" ? "" : scenario,
            msSinceTrialStart, source: "wizard", category: "outcome",
            target: spec.target || spec.shape || "",
            value: spec.pos || spec.action || "",
            objectShape: spec.shape || "",
            color: spec.color || "",
            pos: approx
        });

        // The scene change itself, as its own rows. An inject row says what was
        // asked for; these say what is now in the world and, later, for how
        // long it was there.
        const sceneCtx = { task: taskKey, variant: vKey, msSinceTrialStart };
        if (spec.action === "spawn")   this.trackSpawn(spec, sceneCtx);
        if (spec.action === "move")    this.trackMove(spec, sceneCtx);
        if (spec.action === "recolor" || spec.action === "recolour") {
            this.trackRecolor(spec, sceneCtx);
        }
        // A correct outcome is silent by design, so recording "nothing was shown"
        // matters as much as recording the error text — without this row the log
        // cannot distinguish a silent success from a missing feedback event.
        this.lastFeedback = {
            at: Date.now(),
            condition: this.session.condition || "",
            label: spec.label || "",
            // What condition B puts on the panel.
            errorText: outcome === "success" ? "" : (spec.errorText || ""),
            // What condition C says out loud, before and after.
            agentPre:  outcome === "success" ? "" : (spec.agentPre || ""),
            agentPost: outcome === "success" ? "" : (spec.agentPost || ""),
            silent: outcome === "success",
            task: taskKey, variant: vKey
        };
        this.logEvent("feedback-shown",
            outcome === "success" ? "(silent — correct outcome)" : (spec.errorText || ""), {
            task: taskKey, variant: vKey,
            scenario: outcome === "success" ? "" : scenario,
            source: "system", category: "feedback",
            value: this.session.condition || ""
        });

        // taskKey and variantKey are added here so Unity can look up the
        // pre-recorded voice clip at Resources/AgentVoice/{task}_{variant}_{pre|post}.wav
        // without having the task list compiled into the APK. Variant matters:
        // the agent's line names the object, and each variant uses a different
        // one. Dropping the WAVs into Resources and rebuilding is all that is
        // needed to add or replace voice for condition C.
        this.scene.send(new NetworkId(OUTCOME_NETWORK_ID), {
            type: "StudyOutcome",
            peer: "WizardOfOz",
            data: JSON.stringify({ ...spec, taskKey, variantKey: vKey })
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
        const { WebSocketServer } = require("ws");
        const BROWSER_MIC_PEER = "browser-mic";

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
            // Information sheet and consent. First thing in the session, before
            // the background form: consent recorded after the data it authorises
            // is not consent. Also printable, so the same wording the
            // participant ticks is the wording on the paper form.
            if (req.method === "GET" && url === "/consent") {
                return serveFile(res, path.join(PUBLIC_DIR, "consent.html"), "text/html");
            }
            // Pre-session background questionnaire (once per participant).
            if (req.method === "GET" && url === "/background") {
                return serveFile(res, path.join(PUBLIC_DIR, "background.html"), "text/html");
            }
            // Replays a finished participant as a timeline. Exists because the
            // single most important row in the file — wrong attribution, three
            // wasted turns — is unreadable as a spreadsheet cell, and that row
            // is the study's result.
            if (req.method === "GET" && url === "/replay") {
                return serveFile(res, path.join(PUBLIC_DIR, "replay.html"), "text/html");
            }

            if (req.method === "GET" && url === "/signup") {
                return serveFile(res, path.join(PUBLIC_DIR, "signup.html"), "text/html");
            }

            if (req.method === "GET" && url === "/signup-data") {
                const cfg = readSlots();
                const counts = signupCounts();
                return send(200, {
                    config: cfg.config || {},
                    slots: (cfg.slots || []).map(s => ({
                        ...s, taken: counts[s.id] || 0
                    }))
                });
            }

            if (req.method === "GET" && url === "/scene-state") {
                const variant = this.trial ? this.trial.variant : this.activeVariant;
                const task = TASKS[this.activeTask];
                const v = task && task.variants[variant];
                return send(200, {
                    condition: this.session.condition || "",
                    // The briefing they were read, so the panel shows the task
                    // and the participant's view of it side by side.
                    prompt: v ? v.prompt : "",
                    label:  v ? v.label : "",
                    objects: [...this.sceneObjects.values()].map(o => ({
                        id: o.id, shape: o.shape, color: o.color,
                        x: o.x, y: o.y, z: o.z,
                        baseline: !!o.baseline,
                        ageMs: Date.now() - o.spawnedAt
                    })),
                    pose: this.lastPose
                        ? { ...this.lastPose, stale: Date.now() - this.lastPose.at > 5000 }
                        : null,
                    feedback: this.lastFeedback,
                    transcript: this.lastTranscript
                });
            }

            if (req.method === "GET" && url === "/mirror") {
                return serveFile(res, path.join(PUBLIC_DIR, "mirror.html"), "text/html");
            }

            if (req.method === "GET" && url === "/allocation") {
                return send(200, {
                    conditions: allocationSummary(),
                    assigned: readAllocations()
                });
            }

            if (req.method === "GET" && url === "/participants") {
                const files = fs.existsSync(LOG_DIR)
                    ? fs.readdirSync(LOG_DIR)
                        .filter(f => f.endsWith(".csv"))
                        .map(f => f.replace(/\.csv$/, ""))
                        .sort()
                    : [];
                return send(200, { participants: files, live: this.session.participantId || null });
            }

            if (req.method === "GET" && url === "/replay-data") {
                const q = new URLSearchParams(req.url.split("?")[1] || "");
                const pid = (q.get("pid") || this.session.participantId || "").replace(/[^\w.-]/g, "");
                const file = path.join(this.session.practice ? PRACTICE_DIR : LOG_DIR, `${pid}.csv`);
                if (!pid || !fs.existsSync(file)) {
                    return send(404, { error: `No log for '${pid}'` });
                }
                return send(200, { participantId: pid, rows: parseCsvFile(file) });
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
                    stt: this.sttStatus
                        ? { ...this.sttStatus, stale: Date.now() - this.sttStatus.at > 20000 }
                        : null,
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
                const q = new URLSearchParams(req.url.split("?")[1] || "");
                const pid = q.get("pid") || this.session.participantId;
                const a = allocate(pid, q.get("cond"), { commit: false });
                return send(200, {
                    participantId: pid,
                    allocation: a,
                    plan: buildPlan(a.condition, a.orderIndex, a.variantOffset)
                });
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

                        // The researcher chooses the condition; the system
                        // chooses the task order and variants, taking the
                        // least-used of each within that condition. Committed
                        // here — this is the moment the participant is really
                        // running — and idempotent, so restarting a session
                        // mid-way returns the same assignment rather than a new
                        // one that would contradict the rows already written.
                        // A practice run picks its condition freely and reserves
                        // nothing: the balance table must only ever count real
                        // participants, and a rehearsal that quietly used up
                        // order #1 would be invisible until the counts stopped
                        // adding up at the end of the study.
                        this.session.practice = !!payload.practice;
                        const alloc = allocate(pid, payload.condition,
                                               { commit: !this.session.practice });
                        this.session.allocation = alloc;
                        this.session.condition = alloc.condition;
                        this.session.plan =
                            buildPlan(alloc.condition, alloc.orderIndex, alloc.variantOffset);
                        this.session.block = Number(payload.block) || 1;
                        const planned = this.session.plan[this.session.block - 1];
                        this.session.startedAt = new Date().toISOString();
                        // A restart clears the end-of-session gate; otherwise a
                        // panel reopened for the next participant would still be
                        // asking for the previous one's questionnaire.
                        this.session.awaitingQuestionnaire = false;
                        this.session.finishedAt = null;

                        // Trial numbering runs 1..N within a participant, so a
                        // new participant restarts the count.
                        if (newParticipant) {
                            this.trial = null;
                            this.trialCounter = 0;
                            this.sceneObjects.clear();
                            this.objectCounter = 0;
                        }

                        // Follow the plan for this block unless overridden, so the
                        // Wizard never has to read the master table by hand.
                        if (planned) {
                            this.activeTask    = planned.task;
                            this.activeVariant = planned.variant;
                                            }

                        this.logSessionStart();

                        // The baseline. Everything that follows is measured
                        // against this row, so it is written before the
                        // participant has done anything at all - including the
                        // full assigned plan, so the file records what they were
                        // *supposed* to get as well as what happened.
                        this.sessionStartedAt = Date.now();
                        this.eventSeq = 0;
                        this.logEvent("session-start", `condition=${this.session.condition}`, {
                            source: "wizard", category: "session",
                            msSinceTrialStart: ""
                        });
                        (this.session.plan || []).forEach(b => {
                            this.logEvent("plan-assigned",
                                `block=${b.block} task=${b.task} variant=${b.variant} ` +
                                `scenario=${b.scenario}`,
                                { source: "system", category: "session",
                                  task: b.task, variant: b.variant, scenario: b.scenario,
                                  value: b.block, msSinceTrialStart: "" });
                        });
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
                        const key = normaliseTaskKey(payload.task);
                        if (!key || !TASKS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        this.logEvent("task-change", key, { task: key, attempt: 0, source: "wizard", category: "ui" });
                        return send(200, { activeTask: this.activeTask, plannedTask: this.plannedTask() });
                    }


                    // Selects the content variant (normally set by the plan).
                    if (req.method === "POST" && url === "/variant") {
                        const v = String(payload.variant || "");
                        if (!TASKS[this.activeTask].variants[v]) {
                            return send(400, { error: "Unknown variant: " + v });
                        }
                        this.activeVariant = v;
                        this.logEvent("variant-change", v,
                            { source: "wizard", category: "ui", variant: v });
                        if (this.trial && !this.trial.endedAt) this.trial.variant = v;
                        return send(200, { ok: true, activeVariant: v });
                    }

                    // Start trial: resets the scene, starts the clock, fixes the
                    // (task, variant, error type) triple for this trial.
                    if (req.method === "POST" && url === "/trial/start") {
                        return send(200, this.startTrial(normaliseTaskKey(payload.task), payload.variant));
                    }

                    // End trial, write its summary row, and load the next task.
                    if (req.method === "POST" && url === "/trial/complete") {
                        return send(200, this.completeTrial(
                            payload.status || "completed",
                            // Advancing is the default; pass advance:false to end
                            // a trial without moving on (a re-run, or a stop).
                            { advance: payload.advance !== false }
                        ));
                    }

                    // Finish this task and load the next assigned one.
                    if (req.method === "POST" && url === "/next-block") {
                        return send(200, this.advanceBlock());
                    }

                    // Closes the participant out after the debrief and releases
                    // the panel for the next person.
                    if (req.method === "POST" && url === "/end-study") {
                        return send(200, this.endStudy());
                    }

                    if (req.method === "POST" && url === "/inject") {
                        const taskKey = normaliseTaskKey(payload.task) || this.activeTask;
                        // "error" replays this task's scripted failure (use again if
                        // they repeat the mistake); "success" resolves it.
                        const outcome = payload.outcome || payload.response || "error";
                        return send(200, this.injectResponse(taskKey, outcome, payload.variant));
                    }

                    // Manipulation check: did the feedback actually land?
                    // In A there is none, so this records noticing the failure.
                    if (req.method === "POST" && url === "/noticed") {
                        const val = String(payload.noticed || "").toLowerCase();
                        if (!["yes", "no", "partial"].includes(val)) {
                            return send(400, { error: "noticed must be yes|no|partial" });
                        }
                        if (this.trial) {
                            this.trial.noticedFeedback = val;
                            this.logEvent("manipulation-check", `noticed=${val}`, {
                                source: "wizard", category: "measure", value: val,
                                msSinceTrialStart: Date.now() - this.trial.startedAt
                            });
                        }
                        return send(200, { ok: true, noticed: val });
                    }

                    // What the participant actually DID after the error, coded
                    // live, once per repair attempt.
                    //
                    // This is the measure that carries the practical claim.
                    // Attribution alone is a stated belief; a reviewer can fairly
                    // ask who cares. The repair move is what the belief costs:
                    //
                    //   detail   adds the missing information  (fixes a user fault)
                    //   verbatim repeats it, louder or slower  (fixes nothing, ever)
                    //   scope    reduces or changes the ask    (fixes a system limit)
                    //   question asks the system what went wrong
                    //   gaveup   stops trying, or turns to the experimenter
                    //
                    // "verbatim" is the one that matters most. It is pure waste --
                    // the move people make when they think they were misheard --
                    // and it is what misattributing a system limit to your own
                    // phrasing looks like from outside. A product cannot see
                    // attribution, but it can count repeated identical utterances.
                    if (req.method === "POST" && url === "/repair-strategy") {
                        const val = String(payload.strategy || "").toLowerCase();
                        const allowed = ["detail", "verbatim", "scope", "question", "gaveup"];
                        if (!allowed.includes(val)) {
                            return send(400, { error: "strategy must be " + allowed.join("|") });
                        }
                        if (!this.trial) return send(400, { error: "No active trial" });

                        this.trial.repairStrategies.push(val);
                        const n = this.trial.repairStrategies.length;
                        this.logEvent("repair-strategy", `${n}:${val}`, {
                            source: "wizard", category: "measure", value: n,
                            msSinceTrialStart: Date.now() - this.trial.startedAt
                        });
                        return send(200, {
                            ok: true,
                            strategy: val,
                            sequence: this.trial.repairStrategies
                        });
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
                                msSinceTrialStart: Date.now() - this.trial.startedAt,
                                source: "wizard", category: "measure",
                                value: isCorrect ? 1 : 0, target: correct
                            });
                        }
                        return send(200, { ok: true, attribution: val,
                            correct: this.trial ? this.trial.correctAttribution : "" });
                    }

                    // Follow-up to the attribution probe.
                    //
                    // The graded 0-10 blame split and the confidence rating were
                    // asked here too, once per failure. They moved to the
                    // post-session questionnaire: six repetitions of two extra
                    // spoken scales was more than the moment after a failure can
                    // carry, and a rushed scale is worse than a retrospective
                    // one. The endpoint keeps its name and its shape so nothing
                    // else has to change.
                    if (req.method === "POST" && url === "/attribution-detail") {
                        if (!this.trial) return send(200, { ok: true, ignored: "no active trial" });
                        const out = {};

                        // The belief that drives H2. Someone who thinks a
                        // different wording would have worked has a reason to
                        // repeat themselves; on a system-limitation trial that
                        // belief is false, and the wasted repair follows from it.
                        // Measuring the belief separately from the behaviour is
                        // what lets the two be related rather than assumed.
                        if (payload.reparability !== undefined) {
                            const r = String(payload.reparability).toLowerCase();
                            if (!["yes", "no", "unsure"].includes(r)) {
                                return send(400, { error: "reparability must be yes|no|unsure" });
                            }
                            this.trial.perceivedReparability = r;
                            out.reparability = r;
                            const truth = this.trial.correctAttribution === "system" ? "no" : "yes";
                            this.logEvent("perceived-reparability",
                                `believes rewording would have worked=${r} ` +
                                `(actually ${truth} for this scenario)`, {
                                msSinceTrialStart: Date.now() - this.trial.startedAt,
                                source: "participant", category: "measure",
                                value: r === truth ? 1 : 0, target: truth
                            });
                        }

                        return send(200, { ok: true, ...out });
                    }

                    if (req.method === "POST" && url === "/reset") {
                        return send(200, this.resetScene());
                    }

                    // Unity confirms an object was actually spawned/moved/destroyed
                    // with its real world-space position. Logged as its own row so
                    // the CSV has ground-truth coordinates for every scene change.
                    if (req.method === "POST" && url === "/scene-event") {
                        const evType = String(payload.type || "object-change");
                        const x = payload.x !== undefined ? Number(payload.x) : "";
                        const y = payload.y !== undefined ? Number(payload.y) : "";
                        const z = payload.z !== undefined ? Number(payload.z) : "";
                        const shape = String(payload.shape || "").toLowerCase();

                        // Correct the registry to the transform the headset
                        // actually produced, so any later move or destroy row
                        // carries measured coordinates rather than the nominal
                        // ones the position label implied.
                        const rec = [...this.sceneObjects.values()].reverse()
                            .find(o => String(o.shape).toLowerCase() === shape);
                        if (rec && x !== "") {
                            rec.x = x; rec.y = y; rec.z = z;
                            rec.unityName = payload.name || rec.unityName || "";
                        }

                        this.logEvent(evType + "-confirmed",
                            payload.name || payload.shape || "", {
                            source: "headset", category: "scene",
                            objectId: rec ? rec.id : "",
                            objectShape: payload.shape || (rec ? rec.shape : ""),
                            color: rec ? rec.color : "",
                            target: payload.name || "",
                            value:  payload.shape || payload.action || "",
                            pos:    { x, y, z }
                        });
                        return send(200, { ok: true });
                    }

                    // Researcher fallback for push-to-talk: hold recording open
                    // from the panel when the controller trigger isn't usable.
                    if (req.method === "POST" && url === "/record") {
                        const on = !!payload.recording;
                        this.sendControl("mic", on ? "start" : "stop");
                        this.logEvent("remote-record", on ? "start" : "stop");
                        // The control message is fire-and-forget, so reporting
                        // plain success was reporting that a message was sent —
                        // not that anything received it. With no headset
                        // connected the panel said "recording" to an empty room.
                        // The headset reports mic status every second, so a
                        // stale report means nothing is listening.
                        const heard = this.micStatus &&
                            Date.now() - this.micStatus.at <= 5000;
                        return send(200, {
                            ok: true, recording: on, headsetConnected: !!heard
                        });
                    }

                    if (req.method === "POST" && url === "/event") {
                        this.logEvent(payload.type || "note", payload.detail || "");
                        return send(200, { ok: true });
                    }

                    if (req.method === "POST" && url === "/signup") {
                        const cfg = readSlots();
                        const slot = (cfg.slots || []).find(s => s.id === payload.slotId);
                        if (!slot) return send(400, { ok: false, error: "That slot no longer exists." });

                        // Re-checked here rather than trusting the page, because
                        // two people can be looking at the same last place.
                        const taken = signupCounts()[slot.id] || 0;
                        if (taken >= slot.capacity) {
                            return send(409, { ok: false,
                                error: "Sorry — that slot was just taken. Please pick another." });
                        }

                        appendCsv(SIGNUP_FILE,
                            "bookedAtIso,slotId,slotWhen,name,email",
                            [new Date().toISOString(), slot.id, slot.when,
                             String(payload.name || ""), String(payload.email || "")]
                                .map(csvEscape).join(","));
                        console.log(`\x1b[36m[Signup]\x1b[0m ${payload.name} → ${slot.when}`);
                        return send(200, { ok: true, when: slot.when });
                    }

                    if (req.method === "POST" && url === "/questionnaire") {
                        const file = this.saveQuestionnaire(payload);
                        // Only the post-session form closes anything. Tested
                        // positively rather than as "not background": the
                        // mid-session fatigue check is also not background, and
                        // treating it as the closing form would announce the
                        // session was over halfway through it.
                        const isClosingForm = payload.questionnaire === "post-session";
                        console.log(`\x1b[35m[Questionnaire]\x1b[0m saved → ${file}`);

                        // The post-condition form is the participant's last
                        // task. It does not close the session on its own —
                        // the debrief still has to happen, and the researcher
                        // presses End session after it.
                        if (isClosingForm && this.session.awaitingQuestionnaire) {
                            this.logEvent("questionnaire-complete",
                                `condition ${this.session.condition} questionnaire submitted — ` +
                                `debrief, then end the session`, {
                                source: "participant", category: "session",
                                value: this.session.condition
                            });
                        }

                        return send(200, {
                            ok: true, saved: file,
                            session: this.session,
                            finished: !!this.session.finishedAt
                        });
                    }

                    send(404, { error: "Not found" });
                } catch (e) {
                    send(400, { error: e.message });
                }
            });
        });

        // ── Browser mic WebSocket ─────────────────────────────────────────────
        //
        // The Quest microphone is muted at the OS level whenever the headset is
        // off the participant's head. For desk testing the researcher can stream
        // audio from the browser's own microphone instead — same STT pipeline,
        // no Quest required.
        //
        // Browser connects to ws://localhost:<controlPort>/browser-mic, sends
        // raw PCM16-LE frames at 16 kHz mono. The server treats them exactly
        // like audio arriving from the Quest.
        const wss = new WebSocketServer({ noServer: true });

        server.on("upgrade", (req, socket, head) => {
            if (req.url !== "/browser-mic") { socket.destroy(); return; }
            wss.handleUpgrade(req, socket, head, (ws) => {
                this.components.transcriptionService.recordingStart(BROWSER_MIC_PEER);
                console.log("[BrowserMic] connected — streaming to STT");

                ws.on("message", (data, isBinary) => {
                    if (isBinary) this.components.transcriptionService.addAudioChunk(BROWSER_MIC_PEER, data);
                });
                ws.on("close", () => {
                    this.components.transcriptionService.recordingStop(BROWSER_MIC_PEER);
                    console.log("[BrowserMic] disconnected");
                });
                ws.on("error", () => {
                    this.components.transcriptionService.recordingStop(BROWSER_MIC_PEER);
                });
            });
        });

        // Relay browser-mic transcripts to the normal transcript history so the
        // panel poll loop picks them up without any extra work.
        this.components.transcriptionService.on("response", (buf, peerUUID) => {
            if (peerUUID !== BROWSER_MIC_PEER) return;
            const text = buf.toString().trim();
            if (!text) return;
            const clean = text.replace(/^[>\s]+/, "");
            this.lastTranscript = clean;
            this.transcriptHistory.push({ at: new Date().toISOString(), text: clean });
            if (this.transcriptHistory.length > 50) this.transcriptHistory.shift();
            this.logEvent("transcript", clean, { source: "browser-mic", category: "speech" });
            console.log(`[BrowserMic] transcript: ${clean}`);
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
            // Retiring the file is still the right call — writing rows under a
            // header they do not match corrupts the data silently, and mid-study
            // that would not surface until analysis. But the retired copy moves
            // out of Logs/ so the live file stays the only one for a
            // participant. Since every row is now written through LOG_COLUMNS by
            // name, this should only ever fire when the column list itself is
            // edited between runs.
            const stamp = new Date().toISOString().replace(/[:.]/g, "-");
            fs.mkdirSync(ARCHIVE_DIR, { recursive: true });
            const retired = path.join(
                ARCHIVE_DIR,
                path.basename(file).replace(/\.csv$/, `_pre-${stamp}.csv`)
            );
            fs.renameSync(file, retired);
            console.log(`\x1b[33m[Logs]\x1b[0m column layout changed — ` +
                `previous ${path.basename(file)} moved to archive/${path.basename(retired)}`);
        }
    }
    if (!fs.existsSync(file)) fs.writeFileSync(file, header + "\n");
    fs.appendFileSync(file, row + "\n");
}

/**
 * Turns whatever the panel sent into a real key in TASKS.
 *
 * The panel sends both bare numbers ("1", from the task chips) and full keys
 * ("task1", "practice"). The old rule was "prepend 'task' unless it already
 * starts with 'task'", which silently turned "practice" into "taskpractice" —
 * so starting a practice trial and injecting into one both failed with
 * "Unknown task", and practice looked like an empty, dead mode. Only a bare
 * number should ever be prefixed.
 */
function normaliseTaskKey(task) {
    if (task === undefined || task === null || task === "") return null;
    const s = String(task).trim();
    return /^\d+$/.test(s) ? `task${s}` : s;
}

/**
 * Reads a participant CSV back into objects.
 *
 * Hand-rolled rather than pulled in as a dependency because the writer above is
 * hand-rolled too, and the pair has to agree about exactly one thing: a doubled
 * quote inside a quoted field. A parser that disagreed would corrupt the
 * questionnaire column, which is the one field that always contains commas.
 */
function parseCsvFile(file) {
    const text = fs.readFileSync(file, "utf8");
    const rows = [];
    let field = "", record = [], inQuotes = false;

    for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (inQuotes) {
            if (c === '"') {
                if (text[i + 1] === '"') { field += '"'; i++; }
                else inQuotes = false;
            } else field += c;
        } else if (c === '"') inQuotes = true;
        else if (c === ",") { record.push(field); field = ""; }
        else if (c === "\n") { record.push(field); rows.push(record); record = []; field = ""; }
        else if (c !== "\r") field += c;
    }
    if (field !== "" || record.length) { record.push(field); rows.push(record); }
    if (!rows.length) return [];

    const header = rows[0];
    return rows.slice(1)
        .filter(r => r.length > 1)
        .map(r => Object.fromEntries(header.map((h, i) => [h, r[i] ?? ""])));
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

module.exports = {
    WizardOfOzApp, TASKS, TASK_ATTRIBUTION,
    planForParticipant, buildPlan, allocate, allocationSummary,
    scoreQuestionnaire, LOG_COLUMNS
};

if (require.main === module) {
    const app = new WizardOfOzApp();
    app.start();
}
