const { ServiceController } = require("ubiq-genie-components");

class CodeGenerationService extends ServiceController {
    constructor(scene, config = {}) {
        super(scene, "CodeGenerationService", config);

        this.registerChildProcess("default", "python", [
            "-u",
            "../../services/code_generation/codellama_service.py",
            "--preprompt",
            config.preprompt,
            "--prompt_suffix",
            config.prompt_suffix,
            "--key",
            config.key
        ]);
    }
}

module.exports = {
    CodeGenerationService,
};
