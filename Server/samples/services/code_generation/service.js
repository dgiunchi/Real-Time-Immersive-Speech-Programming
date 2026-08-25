const fs = require("fs");
const { spawnSync } = require("child_process");
const path = require("path");
const { ServiceController } = require("ubiq-genie-components");

function getPythonCommand() {
    const samplesRoot = path.resolve(__dirname, "..", "..");
    const venvRoot = path.join(samplesRoot, "venv");
    const candidates = process.platform === "win32"
        ? [{ command: path.join(venvRoot, "Scripts", "python.exe"), config: path.join(venvRoot, "pyvenv.cfg") }]
        : [{ command: path.join(venvRoot, "bin", "python"), config: path.join(venvRoot, "pyvenv.cfg") }];

    for (const candidate of candidates) {
        if (fs.existsSync(candidate.command) && fs.existsSync(candidate.config)) {
            return candidate.command;
        }
    }

    console.warn("[CodeGenerationService] Python venv not found or incomplete. Recreate it with: cd Server\\samples; py -3.10 -m venv .\\venv; .\\venv\\Scripts\\Activate.ps1; python -m pip install --upgrade pip setuptools wheel; pip install -r requirements.txt");
    // "python" exists on Windows but not on macOS or most Linux distributions,
    // where the interpreter is "python3". Falling back to "python" there fails
    // with "spawn python ENOENT" after speech has already been transcribed, so
    // the turn dies silently between recognition and generation.
    const fallbacks = process.platform === "win32" ? ["python", "python3"] : ["python3", "python"];
    for (const candidate of fallbacks) {
        const probe = spawnSync(candidate, ["--version"], { stdio: "ignore" });
        if (!probe.error) return candidate;
    }
    return fallbacks[0];
}

class CodeGenerationService extends ServiceController {
    constructor(scene, config = {}) {
        super(scene, "CodeGenerationService", config);

        const apiKey =
            process.env.OPENAI_API_KEY ||
            (config.credentials && config.credentials.openAI && config.credentials.openAI.key) ||
            config.key ||
            "";

        const model =
            process.env.OPENAI_MODEL ||
            (config.credentials && config.credentials.openAI && config.credentials.openAI.model) ||
            config.openAIModel ||
            "gpt-4o-mini";

        this.pythonCommand = getPythonCommand();
        this.pythonOptions = [
            "-u",
            path.join(__dirname, "openai_chatgpt_api.py"),
            "--preprompt",
            config.preprompt,
            "--prompt_suffix",
            config.prompt_suffix,
            "--key",
            apiKey,
            "--model",
            model
        ];

        this.ensureDefaultChildProcess();
    }

    ensureDefaultChildProcess() {
        if (this.childProcesses.default == undefined) {
            this.registerChildProcess("default", this.pythonCommand, this.pythonOptions);
        }
    }

    sendToChildProcess(identifier, data) {
        if (identifier === "default") {
            this.ensureDefaultChildProcess();
        }

        return super.sendToChildProcess(identifier, data);
    }
}

module.exports = {
    CodeGenerationService,
};
