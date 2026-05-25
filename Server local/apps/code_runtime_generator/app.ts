import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import nconf from 'nconf';
import { ApplicationController } from '../../components/application';
import { MessageReader } from '../../components/message_reader';
import { FileServer } from '../../components/file_server';
import { FasterWhisperHttpSttService } from '../../services/speech_to_text/service';
import { CodeGenerationService } from '../../services/code_generation/service';

const STT_CONTROL_PREFIX = '__STT_CONTROL__:';
const DATA_DIR = 'data';
const INPUT_FILE = path.join(DATA_DIR, 'input.txt');

function ensureRuntimeDataFiles(): void {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  if (!fs.existsSync(INPUT_FILE)) {
    fs.writeFileSync(INPUT_FILE, '');
  }
}

class CodeGeneration extends ApplicationController {
  private isGenerating = false;

  registerComponents(): void {
    ensureRuntimeDataFiles();

    this.components.fileServer = new FileServer(DATA_DIR, nconf.get('fileServer:port') || 3000);
    this.components.audioReceiver = new MessageReader(this.scene, 98);
    this.components.transcriptionService = new FasterWhisperHttpSttService(this.scene, nconf.get());
    this.components.codeGenerationService = new CodeGenerationService(this.scene, nconf.get());
  }

  definePipeline(): void {
    this.components.audioReceiver.on('data', (data: { message: Buffer }) => {
      const peerUUID = data.message.subarray(0, 36).toString();
      const pcmChunk = Buffer.from(data.message.subarray(36));

      if (pcmChunk.length <= 64) {
        const control = pcmChunk.toString('utf8');
        if (control.startsWith(STT_CONTROL_PREFIX)) {
          const action = control.slice(STT_CONTROL_PREFIX.length);
          if (action === 'start') {
            this.components.transcriptionService.recordingStart(peerUUID);
          } else if (action === 'stop') {
            this.components.transcriptionService.recordingStop(peerUUID);
          } else {
            console.warn(`Unknown STT control action from ${peerUUID}: ${action}`);
          }
          return;
        }
      }

      this.components.transcriptionService.addAudioChunk(peerUUID, pcmChunk);
    });

    this.components.transcriptionService.on('response', (data: Buffer, identifier: string) => {
      const peer = this.roomClient.peers.get(identifier);
      const peerName = peer ? peer.properties.get('ubiq.samples.social.name') : identifier;
      let response = data.toString().replace(/(\r\n|\n|\r)/gm, '');

      if (response.length <= 10 || this.isGenerating) {
        return;
      }

      ensureRuntimeDataFiles();
      fs.appendFileSync(INPUT_FILE, response);
      console.log(`File ${INPUT_FILE} appended successfully.`);

      if (response.startsWith('>')) {
        response = response.slice(1);
        if (response.trim()) {
          this.isGenerating = true;
          console.log(`${peerName} -> Agent:: ${response}`);
          this.components.codeGenerationService.sendToChildProcess('default', `${response}\n`);
        }
      }
    });

    this.components.codeGenerationService.on('response', (data: Buffer) => {
      let response = data.toString();
      if (!response.startsWith('>')) {
        return;
      }

      console.log(` -> Code:: ${response}`);
      response = response.slice(1);
      this.scene.send(nconf.get('outputNetworkId') || 94, {
        type: 'CodeGenerated',
        peer: 'default',
        data: response,
      });
      this.isGenerating = false;
    });
  }
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const app = new CodeGeneration(path.join(__dirname, 'config.json'));
app.start();
