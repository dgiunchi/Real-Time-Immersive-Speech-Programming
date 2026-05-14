const fs = require("fs");
const path = require("path");
const { ServiceController } = require("ubiq-genie-components");

function getPythonCommand() {
    const samplesRoot = path.resolve(__dirname, "..", "..");
    const candidates = process.platform === "win32"
        ? [path.join(samplesRoot, "venv", "Scripts", "python.exe")]
        : [path.join(samplesRoot, "venv", "bin", "python")];

    for (const candidate of candidates) {
        if (fs.existsSync(candidate)) {
            return candidate;
        }
    }

    return "python";
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

        this.registerChildProcess("default", getPythonCommand(), [
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
        ]);
    }
}

module.exports = {
    CodeGenerationService,
};
