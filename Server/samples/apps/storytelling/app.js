const { NetworkId } = require("ubiq/ubiq/messaging");
const { MessageReader, ApplicationController } = require("ubiq-genie-components");
const { TextToSpeechService, TextGenerationService,ImageGenerationService, SpeechToTextService, FileServer } = require("ubiq-genie-services"); //AudioGenerationService
const nconf = require("nconf");
const fs = require('fs');

class TextureGeneration extends ApplicationController {
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

        // A TextGenerationService to generate text based on text
        this.components.textGenerationService = new TextGenerationService(this.scene, nconf.get());

        // An ImageGenerationService to generate images based on text
        this.components.textureGeneration = new ImageGenerationService(this.scene, nconf.get());
        
        // An ImageGenerationService to generate images based on text
        //this.components.audioGeneration = new AudioGenerationService(this.scene, nconf.get());

        // A MessageReader to receive selection data from peers based on fixed network ID
        // Selection data is stored in a dictionary, where the key is the peer UUID and the value is target object
        this.components.selectionReceiver = new MessageReader(this.scene, 93);

        this.components.textToSpeechService = new TextToSpeechService(this.scene, nconf.get());


        this.lastPeerSelection = {};

        this.commandRegex =
            /(?:transform|create|make|set|change|turn)(?: the| an| some)? (?:(?:(.*?)?(?:(?: to| into| seem| look| appear|))?(?: like|like a|like an| a)? (.*)))/i;
        this.textureTarget = {};

        this.iteration_count = 0;
        this.allowed_iterations = 1;

        this.StoryAndImages = []; // sequence of text and images of the story
        this.totalItems = 0
        this.currentItem = 0;
        
        this.storyprompt = "";
    }


    storyTell(identifier){
        if(this.currentItem == this.totalItems) return;

        // send a signal to Unity to display the image
        this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
            type: "DisplayImage",
            target: "default",
            data:  this.StoryAndImages[this.currentItem][1],
            peer: identifier,
        });

        this.components.textToSpeechService.sendToChildProcess("default", this.StoryAndImages[this.currentItem][0] + "\n");
        this.currentItem +=1;

    }

    sendImages(identifier){
        // send to Unity all  the list and start the TTS
        this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
            type: "StoryTelling",
            target: "default",
            data:  JSON.stringify(this.StoryAndImages),
            peer: identifier,
        });

        this.currentItem = 0; // reset to zero for storytelling
    }

    
    textureGen(identifier){
        if(this.currentItem == this.totalItems){
            this.sendImages(identifier);
            
            var that = this;
            setTimeout(function() {
                that.storyTell(identifier);    
              }, 10000);

            return;
        }
        //consumer producer like for images
        var element = this.StoryAndImages[this.currentItem];
        
        //========================================= IMAGE GEN
        // here send to create images
        var peerUUID = identifier;
    

        let textureTarget = "storytellingboard"

        //this.scene.send(nconf.get("outputNetworkId"), {
        //    type: "GenerationStarted",
        //    target: textureTarget,
        //    data: "",
        //    peer: peerUUID,
        //});

        const time = new Date().getTime();
        const targetFileName = peerUUID + "_" + textureTarget + "_" + time;
        console.log(targetFileName + ' generation');
        this.components.textureGeneration.sendToChildProcess(
            "default",
            JSON.stringify({
                //prompt: commandMatch[2],
                prompt: element[1] + ", realistic, well detailed, no tiled",
                output_file: targetFileName,
            }) + "\n"
        );
    }

    definePipeline() {
        // Step 1: When we receive audio data from a peer, split it into a peer UUID and audio data, and send it to the transcription service
        this.components.audioReceiver.on("data", (data) => {
            // Split the data into a peer_uuid (36 bytes) and audio data (rest)
            const peerUUID = data.message.subarray(0, 36).toString();
            const audio_data = data.message.subarray(36, data.message.length);
            
            if(data.message.length>0)
            {
                //console.log("from audio receiver to TTS: " + peerUUID + " " + data.message.length);
                // Send the audio data to the transcription service
                this.components.transcriptionService.sendToChildProcess(
                    peerUUID,
                    JSON.stringify(audio_data.toJSON()) + "\n"
                );
            }
            
        });
        
        // Step 2: When we receive a transcription from the transcription service, send it to the image generation service
        this.components.transcriptionService.on("response", (data, identifier) => {
            // roomClient.peers is a Map of all peers in the room
            // Get the peer with the given identifier
            const peer = this.roomClient.peers.get(identifier);
            const peerName = peer.properties.get("ubiq.samples.social.name");

            //@@ HIJACKED
            var response = data.toString();
            if (response.startsWith(">") && this.iteration_count < this.allowed_iterations){
                //this.storyprompt = response
                console.log("=================HIJACKED")
                this.storyprompt = "a cat in a chocolate castle"
                //response = ">create a story more than 300 words and less than 350 words long about a cat in a chocolate castle. Remember to put a conclusion and don't be too long, and put a @END@. For each sentence of the story, include, in square brackets. This description must be unique and written as if it were to be fed to a diffusion model for image generation. Do not put proper names inside the square brackets.";
                response = ">create a story more than 300 words and less than 350 words long about " + this.storyprompt + ". Remember to put a conclusion and don't be too long, and put a @END@. It is mandatory that for each sentence of the story, include, in square brackets, a different description of a picture representing that sentence. This description must be written  as if it were to be fed to a diffusion model for image generation. Do not put proper names inside the square brackets.";
                this.iteration_count += 1;

            } else response = "";
            
            // Remove all newlines from the response
            response = response.replace(/(\r\n|\n|\r)/gm, "");
            if (response.startsWith(">")) {
                response = response.slice(1); // Slice off the leading '>' character
                if (response.trim()) {
                    console.log(peerName + " -> Agent:: " + response);

                    this.components.textGenerationService.sendToChildProcess("default", response + "\n");
                }
            }
        });

        // Step 3: When we receive a response from the text generation service, send it to the text to speech service
        this.components.textGenerationService.on("response", (data, identifier) => {
            var response = data.toString();
            if (response.startsWith(">")) {
                console.log("Received text generation response from child process " + identifier);
                // here some logic to grab images to do from the text, for example if instructed well the service can put in square brackets the description
                // of the images. And we can pass them to a loop for the generation. Simultaneusly in Unity there is a way to handle them and present according
                // to the text arrived @@@@
                //======================================== TEXT GENERATED
                
                //@@ HIJACKED
                console.log("=================HIJACKED")
                //data = "In a faraway land, there stood a mystical Chocolate Castle, a delectable marvel that beckoned explorers with its sweet allure. [A picture of a grand castle made entirely of mouthwatering chocolate, adorned with colorful candy accents]Inside the castle, an adventurous little cat named Whiskers found herself lost in this sugary wonderland after curiously following a tantalizing aroma. [A picture of Whiskers, a cute calico cat, walking through a corridor of chocolate walls, sniffing the air with wonder]As Whiskers roamed the halls, she discovered rooms filled with candy fountains, marshmallow pillows, and licorice vines hanging from the ceiling. [A picture showcasing the cat's amazement as she witnesses a room with chocolate fountains, marshmallows scattered like cushions, and licorice vines hanging down]But Whiskers' eyes widened in awe when she stumbled upon the Chocolate River, flowing smoothly with melted goodness. [A picture of Whiskers sitting by the Chocolate River, her eyes reflecting the mesmerizing sight]With a cautious step, Whiskers dipped her paw into the river, savoring the velvety touch of liquid chocolate on her furry paw. [A picture of Whiskers dipping her paw into the river, her expression showing delight]Just then, the sweet aroma led Whiskers to the Chocolate Garden, a wondrous place where the flowers were made of gummy bears and the trees were lollipop forests. [A picture showcasing Whiskers in the Chocolate Garden, surrounded by gummy bear flowers and lollipop trees]In this paradise, Whiskers encountered a friendly group of sugar fairies, who invited her to a delightful tea party filled with cocoa treats and candy pastries. [A picture of Whiskers sitting at a tiny table, surrounded by sugar fairies, all enjoying the enchanting tea party]As the day waned, Whiskers realized it was time to leave this magical land, but she knew she would forever carry the memories of her adventure in the Chocolate Castle. [A picture of Whiskers bidding farewell to the sugar fairies, her heart filled with fond memories]With a contented heart and a belly full of sweet delights, Whiskers ventured back home, carrying the magic of the Chocolate Castle within her. [A picture of Whiskers walking away from the castle, a sense of happiness and wonder evident on her face]From that day on, Whiskers became the keeper of the secret Chocolate Castle, sharing her tale with her feline friends, igniting their curiosity, and reminding them that adventure can always be found in the most unexpected places. [A picture of Whiskers surrounded by other cats, narrating her story with animated gestures]And so, the legend of the cat in the Chocolate Castle lived on, forever etched in the hearts and imaginations of those who heard her enchanting tale. [A picture of Whiskers' legend being passed down through generations, with cats huddled together, listening in awe]" ;
                response = "In a faraway land, there stood a mystical Chocolate Castle, a delectable marvel that beckoned explorers with its sweet allure. [A picture of a grand castle made entirely of mouthwatering chocolate, adorned with colorful candy accents]Inside the castle, an adventurous little cat named Whiskers found herself lost in this sugary wonderland after curiously following a tantalizing aroma. [A picture of Whiskers, a cute calico cat, walking through a corridor of chocolate walls, sniffing the air with wonder]As Whiskers roamed the halls, she discovered rooms filled with candy fountains, marshmallow pillows, and licorice vines hanging from the ceiling. [A picture showcasing the cat's amazement as she witnesses a room with chocolate fountains, marshmallows scattered like cushions, and licorice vines hanging down]" ;
                
                //save locally
                fs.writeFile('data/storyscript.txt', response, (err) => {
                    if (err) throw err;
                    console.log('Text saved!');
                    });

                let data_clean = response.replace(/\r?\n/g, '');

                const pattern = /(.*?)\[(.*?)\]/g;
                let result = [];
                let match;

                while ((match = pattern.exec(data_clean)) !== null) {
                    result.push([match[1].trim(), match[2]]);
                }

                this.StoryAndImages = result.slice();
                this.totalItems = this.StoryAndImages.length;
                this.textureGen(identifier);

                //audio generation
                /*this.components.audioGeneration.sendToChildProcess(
                    "default",
                    JSON.stringify({
                        prompt: "ambient music based on a story of " + this.storyprompt,
                        output_file: "BackgroundAudio",
                    }) + "\n"
                );*/
                
                //========================================= END IMAGE GEN
                //all the images sent, the last is not yet generated... need a check to understand when it is finished, store a len


                // ======================================== COORDINATION
                // here all the images are generated and it should send both to the Unity and to the TTS, in a scheduled way (with some delays)
                //@@ to enable
                //this.components.textToSpeechService.sendToChildProcess("default", response + "\n");
            }
        });
        
        // Step 4: When we receive a response from the audio generation service, send a message to clients with the audio file name.
        /*this.components.audioGeneration.on("response", (data, identifier) => {
            data = data.toString();
            if (data.includes(".wav")) {
                //const [peerUUID, target, time] = data.split("_");
                //this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
                //    type: "TextureGeneration",
                //    target: target,
                //    data: data,
                //    peer: peerUUID,
                //});
                console.log('Background audio saved!');
                // send to Unity all  the list and start the TTS
                this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
                    type: "BackgroundAudio",
                    target: "default",
                    data:  JSON.stringify(data),
                    peer: identifier,
                });
                
                this.textureGen(identifier);
                
            }

        });*/

        // Step 5: When we receive a response from the image generation service, send a message to clients with the image file name.
        this.components.textureGeneration.on("response", (data, identifier) => {
            data = data.toString();
            if (data.includes(".png")) {
                //const [peerUUID, target, time] = data.split("_");
                //this.scene.send(new NetworkId(nconf.get("outputNetworkId")), {
                //    type: "TextureGeneration",
                //    target: target,
                //    data: data,
                //    peer: peerUUID,
                //});

                this.StoryAndImages[this.currentItem][1] =  data.replace(/\r?\n/g, '');
                this.currentItem+=1;
                
                this.textureGen(identifier);
                
            }

        });
        
 

        this.components.textureGeneration.on("error", (err) => {
            console.log(err.toString());
        });

        this.components.textToSpeechService.on("response", (data, identifier) => {
            var response = data;
            console.log("Received TTS response from child process " + identifier);

            this.scene.send(nconf.get("outputNetworkId"), {
                type: "AudioInfo",
                targetPeer: "Blue Hawk",
                audioLength: data.length,
            });

            while (response.length > 0) {
                // console.log("Sending audio data to peers. Audio data length: " + this.audioData.length + " bytes");
                this.scene.send(nconf.get("outputNetworkId"), response.slice(0, 16000));
                response = response.slice(16000);
                //console.log("Sent audio data to peers. Audio data length: " + response.length + " bytes");
            }

            var that = this;
            setTimeout(function() {
                that.storyTell(identifier);    
              }, 10000);

        });
    }
}

module.exports = { TextureGeneration };

if (require.main === module) {
    const app = new TextureGeneration();
    app.start();
}
