const { ServiceController } = require("ubiq-genie-components");

class AudioGenerationService extends ServiceController {
    constructor(scene, config = {}) {
        super(scene, "AudioGenerationService", config)

        this.registerRoomClientEvents();
    }

    // Register events to start the child process when the first peer joins the room, and to kill the child process when the last peer leaves the room.
    registerRoomClientEvents() {
        if (this.roomClient == undefined) {
            throw "RoomClient must be added to the scene before AudioGenerationService";
        }

        this.roomClient.addListener(
            "OnPeerAdded",
            function (peer) {
                if (!("default" in this.childProcesses)) {
                    this.registerChildProcess("default", "python", [
                        "-u",
                        "../../services/text_to_audio/text_2_audio.py",
                        "--output_folder",
                        "../../apps/storytelling/data"
                    ]);
                }
            }.bind(this)
        );

        this.roomClient.addListener(
            "OnPeerRemoved",
            function (peer) {
                if (this.roomClient.peers.size == 0) {
                    this.killChildProcess("default");
                }
            }.bind(this)
        );
    }
}

module.exports = {
    AudioGenerationService,
};
