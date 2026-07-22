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
const CODE_NETWORK_ID    = 94;  // matches CodeGenerationManager.networkId in Unity
const STT_NETWORK_ID     = 98;

const PUBLIC_DIR = path.join(__dirname, "public");
// Study results live in <project root>/Logs (git-ignored, human-findable).
const LOG_DIR    = path.resolve(__dirname, "..", "..", "..", "..", "Logs");

// Short researcher-facing description of every response key, per task.
// Used to label the buttons on the web control panel.
const DESCRIPTIONS = {
    task1: {
        success: "Ball created correctly at hand",
        error1:  "Ball at world origin, not at hand (missing position)",
        error2:  "Cube created instead of sphere (wrong interpretation)",
        error3:  "Ball falls through floor (collider disabled – gradual reveal)",
        error4:  "Squashed ellipsoid (scale inherited from parent)"
    },
    task2: {
        success: "Ball turns green correctly",
        error1:  "All objects turn green (ambiguous 'it')",
        error2:  "Ball turns teal, not green (wrong shade)",
        error3:  "Colour reverts after 2s (material instance issue)",
        error4:  "New green ball created instead of recolouring"
    },
    task3: {
        success: "Ball orbits the cube correctly",
        error1:  "Ball orbits world origin, not the cube",
        error2:  "Orbit on wrong axis (tilted plane)",
        error3:  "Orbit radius too small; ball hits cube and stops",
        error4:  "Orbit too fast (1000 deg/s) – looks vanished"
    },
    task4: {
        success: "Star + orbiting planet created correctly",
        error1:  "Only the star created (ambiguous 'solar system')",
        error2:  "Planet squashed (non-uniform scale from star)",
        error3:  "Planet drifts away (gravity not disabled)",
        error4:  "50 planets created (over-generated)"
    }
};

// ── Pre-scripted task responses ──────────────────────────────────────────────
// Edit these strings to change what gets injected into the Unity runtime.
// Keys: task1…task4, each with success / error1…error4 entries.

const SCRIPTS = {
    task1: {
        name: "Create a ball at hand position",
        success: wrapClass("Task1_Success", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.15f;
                go.transform.position = transform.position + transform.forward * 0.3f;
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                go.tag = "Interactable";
            }`),
        error1: wrapClass("Task1_Error1_NoPosition", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.15f;
                go.transform.position = Vector3.zero;
                go.AddComponent<Rigidbody>();
            }`),
        error2: wrapClass("Task1_Error2_WrongShape", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = Vector3.one * 0.15f;
                go.transform.position = transform.position + transform.forward * 0.3f;
                go.AddComponent<Rigidbody>();
            }`),
        error3: wrapClass("Task1_Error3_NoCollider", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.15f;
                go.transform.position = transform.position + transform.forward * 0.3f + Vector3.up * 0.5f;
                var col = go.GetComponent<SphereCollider>();
                if (col) col.enabled = false;
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
            }`),
        error4: wrapClass("Task1_Error4_WrongScale", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = new Vector3(0.05f, 0.25f, 0.05f);
                go.transform.position = transform.position + transform.forward * 0.3f;
                go.AddComponent<Rigidbody>();
            }`)
    },

    task2: {
        name: "Change ball colour to green",
        success: wrapClass("Task2_Success", `
            void Start() {
                var renderers = FindObjectsOfType<Renderer>();
                foreach (var r in renderers) {
                    if (r.gameObject.CompareTag("Interactable")) {
                        r.material.color = Color.green;
                        return;
                    }
                }
            }`),
        error1: wrapClass("Task2_Error1_AllGreen", `
            void Start() {
                foreach (var r in FindObjectsOfType<Renderer>())
                    r.material.color = Color.green;
            }`),
        error2: wrapClass("Task2_Error2_WrongColour", `
            void Start() {
                foreach (var r in FindObjectsOfType<Renderer>()) {
                    if (r.gameObject.CompareTag("Interactable")) {
                        r.material.color = new Color(0f, 0.7f, 0.7f);
                        return;
                    }
                }
            }`),
        error3: wrapClass("Task2_Error3_Reverts", `
            void Start() => StartCoroutine(DelayChange());
            System.Collections.IEnumerator DelayChange() {
                Renderer target = null;
                foreach (var r in FindObjectsOfType<Renderer>())
                    if (r.gameObject.CompareTag("Interactable")) { target = r; break; }
                if (target) target.material.color = Color.green;
                yield return new WaitForSeconds(2f);
                if (target) target.material.color = Color.white;
            }`),
        error4: wrapClass("Task2_Error4_NewObject", `
            void Start() {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = Vector3.one * 0.15f;
                go.transform.position = Vector3.up * 0.5f;
                go.GetComponent<Renderer>().material.color = Color.green;
            }`)
    },

    task3: {
        name: "Make the ball orbit the cube",
        success: wrapClass("Task3_Success", `
            private Transform centre;
            private float speed = 60f;
            void Start() {
                foreach (var c in GameObject.FindGameObjectsWithTag("Untagged"))
                    if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
            }
            void Update() {
                if (centre) transform.RotateAround(centre.position, Vector3.up, speed * Time.deltaTime);
            }`),
        error1: wrapClass("Task3_Error1_WrongCentre", `
            void Update() { transform.RotateAround(Vector3.zero, Vector3.up, 60f * Time.deltaTime); }`),
        error2: wrapClass("Task3_Error2_WrongAxis", `
            private Transform centre;
            void Start() {
                foreach (var c in GameObject.FindGameObjectsWithTag("Untagged"))
                    if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
            }
            void Update() {
                if (centre) transform.RotateAround(centre.position, Vector3.forward, 60f * Time.deltaTime);
            }`),
        error3: wrapClass("Task3_Error3_TooClose", `
            private Transform centre;
            private float speed = 60f;
            void Start() {
                foreach (var c in GameObject.FindGameObjectsWithTag("Untagged"))
                    if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
            }
            void Update() {
                if (centre) transform.RotateAround(centre.position, Vector3.up, speed * Time.deltaTime);
            }
            void OnCollisionEnter(Collision col) { speed = 0; }`),
        error4: wrapClass("Task3_Error4_TooFast", `
            private Transform centre;
            void Start() {
                foreach (var c in GameObject.FindGameObjectsWithTag("Untagged"))
                    if (c.GetComponent<BoxCollider>()) { centre = c.transform; break; }
            }
            void Update() {
                if (centre) transform.RotateAround(centre.position, Vector3.up, 1000f * Time.deltaTime);
            }`)
    },

    task4: {
        name: "Create a solar system",
        success: wrapClass("Task4_Success", `
            private GameObject planet;
            void Start() {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = transform.position;
                star.transform.localScale = Vector3.one * 0.4f;
                star.GetComponent<Renderer>().material.color = new Color(1f,0.8f,0f);
                planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planet.transform.position = star.transform.position + Vector3.right * 0.8f;
                planet.transform.localScale = Vector3.one * 0.15f;
                planet.GetComponent<Renderer>().material.color = new Color(0.2f,0.4f,1f);
            }
            void Update() {
                if (planet) planet.transform.RotateAround(transform.position, Vector3.up, 45f * Time.deltaTime);
            }`),
        error1: wrapClass("Task4_Error1_NoPlanet", `
            void Start() {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = transform.position;
                star.transform.localScale = Vector3.one * 0.4f;
                star.GetComponent<Renderer>().material.color = new Color(1f,0.8f,0f);
            }`),
        error2: wrapClass("Task4_Error2_BadScale", `
            private GameObject planet;
            void Start() {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = transform.position;
                star.transform.localScale = new Vector3(0.4f,0.2f,0.4f);
                planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planet.transform.SetParent(star.transform);
                planet.transform.localPosition = Vector3.right * 2f;
            }
            void Update() {
                if (planet) planet.transform.RotateAround(transform.position, Vector3.up, 45f * Time.deltaTime);
            }`),
        error3: wrapClass("Task4_Error3_Drift", `
            void Start() {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = transform.position;
                star.transform.localScale = Vector3.one * 0.4f;
                var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                planet.transform.position = star.transform.position + Vector3.right * 0.8f;
                planet.transform.localScale = Vector3.one * 0.15f;
                var rb = planet.AddComponent<Rigidbody>();
                rb.AddForce(Vector3.forward * 2f, ForceMode.VelocityChange);
            }`),
        error4: wrapClass("Task4_Error4_TooMany", `
            void Start() {
                var star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.transform.position = transform.position;
                star.transform.localScale = Vector3.one * 0.4f;
                for (int i = 0; i < 50; i++) {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    p.transform.position = star.transform.position + Quaternion.Euler(0,i*7.2f,0)*Vector3.right*0.8f;
                    p.transform.localScale = Vector3.one * 0.07f;
                }
            }`)
    }
};

/** Wraps a method body in a minimal MonoBehaviour class. */
function wrapClass(className, body) {
    return `using UnityEngine;\npublic class ${className} : MonoBehaviour {\n${body.trim()}\n}`;
}

// ── Application ───────────────────────────────────────────────────────────────

class WizardOfOzApp extends ApplicationController {
    constructor(configFile = "config.json") {
        super(configFile);
        this.lastTranscript  = "";
        this.transcriptHistory = [];
        this.activeTask      = "task1";
        this.controlPort     = nconf.get("wizardControlPort") || 8181;

        // Session state (set by the researcher on the web panel before each run)
        this.session = {
            participantId: "",
            condition:     "",   // "A" | "B" | "C"
            startedAt:     null
        };

        fs.mkdirSync(LOG_DIR, { recursive: true });
    }

    // ── Data logging ─────────────────────────────────────────────────────────

    /** Appends one row to <participant>_events.csv (creates header if new). */
    logEvent(type, detail = "") {
        // Before a session is started, events are participant testing/warm-up.
        // Keep them out of participant data files by tagging them "warmup".
        const pid = this.session.participantId || "warmup";
        const file = path.join(LOG_DIR, `${pid}_events.csv`);
        const isNew = !fs.existsSync(file);
        const row = [
            new Date().toISOString(),
            pid,
            this.session.condition || "",
            this.activeTask,
            type,
            csvEscape(detail)
        ].join(",");
        if (isNew) fs.writeFileSync(file, "timestamp,participantId,condition,task,eventType,detail\n");
        fs.appendFileSync(file, row + "\n");
    }

    /** Records the session start row in the master sessions.csv. */
    logSessionStart() {
        const file = path.join(LOG_DIR, "sessions.csv");
        const isNew = !fs.existsSync(file);
        if (isNew) fs.writeFileSync(file, "timestamp,participantId,condition\n");
        fs.appendFileSync(file,
            [new Date().toISOString(), this.session.participantId, this.session.condition].join(",") + "\n");
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
            this.logEvent("transcript", text);
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
        const code = wrapClass("WoZResetScene", `
            void Start() {
                foreach (var go in GameObject.FindGameObjectsWithTag("Interactable")) Destroy(go);
                foreach (var go in GameObject.FindGameObjectsWithTag("game")) Destroy(go);
                Destroy(gameObject);
            }`);
        console.log(`\x1b[33m[WoZ Reset]\x1b[0m clearing created objects`);
        this.logEvent("reset", "clear-scene");
        this.scene.send(new NetworkId(CODE_NETWORK_ID), {
            type: "CodeGenerated", peer: "WizardOfOz", data: code
        });
        return { ok: true, reset: true };
    }

    /** Injects a pre-scripted code string into the Unity client. */
    injectResponse(taskKey, responseKey) {
        const task = SCRIPTS[taskKey];
        if (!task) return { ok: false, error: `Unknown task: ${taskKey}` };

        const code = task[responseKey];
        if (!code) return { ok: false, error: `Unknown response: ${responseKey} for task ${taskKey}` };

        console.log(`\x1b[32m[WoZ Inject]\x1b[0m task=${taskKey} response=${responseKey}`);
        this.logEvent("inject", `${taskKey}/${responseKey}`);

        this.scene.send(new NetworkId(CODE_NETWORK_ID), {
            type: "CodeGenerated",
            peer: "WizardOfOz",
            data: code
        });

        return { ok: true, task: taskKey, response: responseKey, codeSent: code.substring(0, 80) + "…" };
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
                return send(200, {
                    session: this.session,
                    lastTranscript: this.lastTranscript,
                    transcriptHistory: this.transcriptHistory.slice(-8),
                    activeTask: this.activeTask,
                    availableTasks: Object.keys(SCRIPTS).map(k => ({ key: k, name: SCRIPTS[k].name }))
                });
            }

            if (req.method === "GET" && url === "/tasks") {
                return send(200, Object.entries(SCRIPTS).map(([k, v]) => ({
                    key: k, name: v.name,
                    responses: Object.keys(v).filter(r => r !== "name").map(r => ({
                        key: r,
                        description: (DESCRIPTIONS[k] && DESCRIPTIONS[k][r]) || r
                    }))
                })));
            }

            // ── Write endpoints ───────────────────────────────────────────────
            let body = "";
            req.on("data", d => (body += d));
            req.on("end", () => {
                try {
                    const payload = body ? JSON.parse(body) : {};

                    if (req.method === "POST" && url === "/session") {
                        this.session.participantId = String(payload.participantId || "").trim();
                        this.session.condition     = String(payload.condition || "").trim().toUpperCase();
                        this.session.startedAt     = new Date().toISOString();
                        this.logSessionStart();
                        console.log(`\x1b[35m[Session]\x1b[0m participant=${this.session.participantId} condition=${this.session.condition}`);
                        return send(200, { ok: true, session: this.session });
                    }

                    if (req.method === "POST" && url === "/task") {
                        const key = `task${payload.task}`;
                        if (!SCRIPTS[key]) return send(400, { error: "Unknown task: " + payload.task });
                        this.activeTask = key;
                        this.logEvent("task-change", key);
                        return send(200, { activeTask: this.activeTask });
                    }

                    if (req.method === "POST" && url === "/inject") {
                        const taskKey     = payload.task     ? `task${payload.task}` : this.activeTask;
                        const responseKey = payload.response || "success";
                        return send(200, this.injectResponse(taskKey, responseKey));
                    }

                    if (req.method === "POST" && url === "/reset") {
                        return send(200, this.resetScene());
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
