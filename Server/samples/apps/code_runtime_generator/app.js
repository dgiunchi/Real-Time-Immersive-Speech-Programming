const { NetworkId } = require("ubiq/ubiq/messaging");
const { MessageReader, ApplicationController } = require("ubiq-genie-components");
const {  CodeGenerationService, SpeechToTextService, FileServer } = require("ubiq-genie-services");
const fs = require("fs");
const nconf = require("nconf");

class CodeGeneration extends ApplicationController {
    constructor(configFile = "config.json") {
        super(configFile);
    }

    registerComponents() {
        // A FileServer to serve image files to clients
        this.components.fileServer = new FileServer("data");

        // A MessageReader to read audio data from peers based on fixed network ID
        this.components.audioReceiver = new MessageReader(this.scene, 98);

        // A SpeechToTextService to transcribe audio coming from peers
        this.components.transcriptionService = new SpeechToTextService(this.scene, nconf.get());

        // A CodeGenerationService to generate text based on text
        this.components.codeGenerationService = new CodeGenerationService(this.scene, nconf.get());

        this.functionality = "";

        this.isGenerating = false;

    }

    definePipeline() {
        // Step 1: When we receive audio data from a peer, split it into a peer UUID and audio data, and send it to the transcription service
        this.components.audioReceiver.on("data", (data) => {
            // Split the data into a peer_uuid (36 bytes) and audio data (rest)
            const peerUUID = data.message.subarray(0, 36).toString();
            const audio_data = data.message.subarray(36, data.message.length);
            // Send the audio data to the transcription service
            this.components.transcriptionService.sendToChildProcess(
                peerUUID,
                JSON.stringify(audio_data.toJSON()) + "\n"
            );
        });

        // Step 2: When we receive a transcription from the transcription service, send it to the image generation service
        this.components.transcriptionService.on("response", (data, identifier) => {
            // roomClient.peers is a Map of all peers in the room
            // Get the peer with the given identifier
            const peer = this.roomClient.peers.get(identifier);
            const peerName = peer.properties.get("ubiq.samples.social.name");

            var response = data.toString();
            var threshold = 10;

            if (response.length != 0 && response.length > threshold && this.isGenerating == false) {
                // Remove all newlines from the response
                response = response.replace(/(\r\n|\n|\r)/gm, "");
                
                const filename = "data/input.txt";
                const exists = fs.existsSync(filename);

                // If the file exists, append the data
                if (exists) {
                    fs.appendFileSync(filename, response);
                    console.log(`File ${filename} appended successfully.`);
                } else {
                    // If the file does not exist, create it and write the data
                    fs.writeFileSync(filename, response);
                }


                if (response.startsWith(">")) {
                    response = response.slice(1); // Slice off the leading '>' character
                    if (response.trim()) {
                        this.isGenerating = true;
                        console.log(peerName + " -> Agent:: " + response);

                        // this.components.textToSpeechService.sendToChildProcess("default", response + "\n");
                        this.components.codeGenerationService.sendToChildProcess("default", response + "\n");
                    }
                }
                response = "";
            }
        });

        // Step 3: When we receive a response from the text generation service, send it to the text to speech service
        this.components.codeGenerationService.on("response", (data, identifier) => {
            var response = data.toString();
            //console.log("Received text generation response from child process " + identifier);
            if (response.startsWith(">")) {
                console.log(" -> Code:: " + response);
                response = response.slice(1);
                
                this.scene.send(94, {
                        type: "CodeGenerated",
                        peer: identifier,
                        data: response,
                    });
                this.isGenerating = false;
            }
        });
    }
}

module.exports = { CodeGeneration };

if (require.main === module) {
    const app = new CodeGeneration();
    app.start();
}
