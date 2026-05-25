import { EventEmitter } from 'node:events';
import FormData from 'form-data';

const DEFAULT_HTTP_URL = 'http://130.136.2.161:50101/stt/transcribe';
const DEFAULT_SAMPLE_RATE = 16000;
const DEFAULT_CHANNELS = 1;
const DEFAULT_BITS_PER_SAMPLE = 16;
const DEFAULT_FINALIZE_AFTER_MS = 1200;
const DEFAULT_MIN_AUDIO_MS = 300;
const DEFAULT_MAX_AUDIO_MS = 20000;

type Session = {
  chunks: Buffer[];
  bytes: number;
  timer?: NodeJS.Timeout;
  transcribing: boolean;
  recording: boolean;
};

function getNumber(name: string, fallback: number): number {
  const value = Number(process.env[name]);
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

function getBoolean(name: string, fallback: boolean): boolean {
  const value = process.env[name];
  if (value === undefined || value === '') {
    return fallback;
  }
  return value === '1' || value.toLowerCase() === 'true' || value.toLowerCase() === 'yes';
}

function durationMsForBytes(bytes: number, sampleRate: number, channels: number, bitsPerSample: number): number {
  return (bytes / (channels * (bitsPerSample / 8)) / sampleRate) * 1000;
}

function createWavBuffer(pcmBuffer: Buffer, sampleRate: number, channels: number, bitsPerSample: number): Buffer {
  const header = Buffer.alloc(44);
  const byteRate = sampleRate * channels * (bitsPerSample / 8);
  const blockAlign = channels * (bitsPerSample / 8);

  header.write('RIFF', 0);
  header.writeUInt32LE(36 + pcmBuffer.length, 4);
  header.write('WAVE', 8);
  header.write('fmt ', 12);
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(channels, 22);
  header.writeUInt32LE(sampleRate, 24);
  header.writeUInt32LE(byteRate, 28);
  header.writeUInt16LE(blockAlign, 32);
  header.writeUInt16LE(bitsPerSample, 34);
  header.write('data', 36);
  header.writeUInt32LE(pcmBuffer.length, 40);

  return Buffer.concat([header, pcmBuffer]);
}

function postWav(url: string, wavBuffer: Buffer): Promise<string> {
  return new Promise((resolve, reject) => {
    const form = new FormData();
    form.append('file', wavBuffer, {
      filename: 'utterance.wav',
      contentType: 'audio/wav',
      knownLength: wavBuffer.length,
    });
    form.append('language', 'en');
    form.append('beam_size', '1');

    form.submit(url, (error, response) => {
      if (error) {
        reject(error);
        return;
      }

      const chunks: Buffer[] = [];
      response.on('data', (chunk) => chunks.push(Buffer.from(chunk)));
      response.on('error', reject);
      response.on('end', () => {
        const body = Buffer.concat(chunks).toString();
        if ((response.statusCode || 500) < 200 || (response.statusCode || 500) >= 300) {
          reject(new Error(`STT HTTP ${response.statusCode}: ${body}`));
          return;
        }
        resolve(body);
      });
    });
  });
}

export class FasterWhisperHttpSttService extends EventEmitter {
  name = 'FasterWhisperHttpSttService';
  url: string;
  sampleRate: number;
  channels: number;
  bitsPerSample: number;
  finalizeAfterMs: number;
  minAudioMs: number;
  maxAudioMs: number;
  requireExplicitRecording: boolean;
  sessions = new Map<string, Session>();

  constructor(scene: any, config: any = {}) {
    super();
    this.url = process.env.STT_HTTP_URL || config?.stt?.httpUrl || DEFAULT_HTTP_URL;
    this.sampleRate = getNumber('STT_SAMPLE_RATE', DEFAULT_SAMPLE_RATE);
    this.channels = getNumber('STT_CHANNELS', DEFAULT_CHANNELS);
    this.bitsPerSample = getNumber('STT_BITS_PER_SAMPLE', DEFAULT_BITS_PER_SAMPLE);
    this.finalizeAfterMs = getNumber('STT_FINALIZE_AFTER_MS', DEFAULT_FINALIZE_AFTER_MS);
    this.minAudioMs = getNumber('STT_MIN_AUDIO_MS', DEFAULT_MIN_AUDIO_MS);
    this.maxAudioMs = getNumber('STT_MAX_AUDIO_MS', DEFAULT_MAX_AUDIO_MS);
    this.requireExplicitRecording = getBoolean('STT_REQUIRE_RECORDING', true);

    const roomClient = scene?.getComponent ? scene.getComponent('RoomClient') : null;
    roomClient?.addListener?.('OnPeerRemoved', (peer: { uuid: string }) => this.clearSession(peer.uuid, 'peer removed'));

    console.log(`[FasterWhisperHttpSttService] ready url=${this.url} format=${this.sampleRate}Hz/${this.channels}ch/${this.bitsPerSample}bit finalizeAfterMs=${this.finalizeAfterMs} minAudioMs=${this.minAudioMs} maxAudioMs=${this.maxAudioMs} requireExplicitRecording=${this.requireExplicitRecording}`);
  }

  private getSession(peerUUID: string): Session {
    let session = this.sessions.get(peerUUID);
    if (!session) {
      session = { chunks: [], bytes: 0, transcribing: false, recording: false };
      this.sessions.set(peerUUID, session);
    }
    return session;
  }

  addAudioChunk(peerUUID: string, audioChunk: Buffer): boolean {
    const chunk = Buffer.isBuffer(audioChunk) ? audioChunk : Buffer.from(audioChunk);
    if (!peerUUID || chunk.length === 0) {
      return false;
    }

    let session = this.sessions.get(peerUUID);
    if (this.requireExplicitRecording && (!session || !session.recording)) {
      return false;
    }

    session = session || this.getSession(peerUUID);
    session.chunks.push(chunk);
    session.bytes += chunk.length;
    this.resetFinalizeTimer(peerUUID, session);

    const durationMs = durationMsForBytes(session.bytes, this.sampleRate, this.channels, this.bitsPerSample);
    if (durationMs >= this.maxAudioMs) {
      this.finalizePeer(peerUUID, 'max duration');
    }

    return true;
  }

  recordingStart(peerUUID: string): void {
    this.clearSession(peerUUID, 'recording start');
    const session = this.getSession(peerUUID);
    session.recording = true;
    console.log(`[FasterWhisperHttpSttService] recording start peerUUID=${peerUUID}`);
  }

  recordingStop(peerUUID: string): void {
    const session = this.sessions.get(peerUUID);
    if (session) {
      session.recording = false;
    }
    console.log(`[FasterWhisperHttpSttService] recording stop peerUUID=${peerUUID}`);
    this.finalizePeer(peerUUID, 'recording stop');
  }

  private resetFinalizeTimer(peerUUID: string, session: Session): void {
    if (session.timer) {
      clearTimeout(session.timer);
    }
    session.timer = setTimeout(() => this.finalizePeer(peerUUID, 'silence timeout'), this.finalizeAfterMs);
  }

  async finalizePeer(peerUUID: string, reason = 'finalize'): Promise<boolean> {
    const session = this.sessions.get(peerUUID);
    if (!session || session.bytes === 0) {
      console.log(`[FasterWhisperHttpSttService] no audio to transcribe peerUUID=${peerUUID} reason=${reason}`);
      return false;
    }

    if (session.timer) {
      clearTimeout(session.timer);
      session.timer = undefined;
    }

    const chunks = session.chunks;
    const bytes = session.bytes;
    const durationMs = durationMsForBytes(bytes, this.sampleRate, this.channels, this.bitsPerSample);
    session.chunks = [];
    session.bytes = 0;
    session.transcribing = true;

    if (durationMs < this.minAudioMs) {
      console.log(`[FasterWhisperHttpSttService] discard short utterance peerUUID=${peerUUID} durationMs=${durationMs.toFixed(0)} bytes=${bytes}`);
      session.transcribing = false;
      this.deleteIdleSession(peerUUID, session);
      return false;
    }

    const wavBuffer = createWavBuffer(Buffer.concat(chunks, bytes), this.sampleRate, this.channels, this.bitsPerSample);
    console.log(`[FasterWhisperHttpSttService] request start peerUUID=${peerUUID} reason=${reason} audioMs=${durationMs.toFixed(0)} pcmBytes=${bytes} wavBytes=${wavBuffer.length}`);

    try {
      const responseText = await postWav(this.url, wavBuffer);
      console.log(`[FasterWhisperHttpSttService] response peerUUID=${peerUUID}: ${responseText}`);
      this.emit('response', Buffer.from(responseText), peerUUID);
    } catch (error) {
      console.error(`[FasterWhisperHttpSttService] request error peerUUID=${peerUUID}: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      session.transcribing = false;
      if (session.bytes > 0) {
        this.resetFinalizeTimer(peerUUID, session);
      } else {
        this.deleteIdleSession(peerUUID, session);
      }
    }

    return true;
  }

  clearSession(peerUUID: string, reason = 'clear'): boolean {
    const session = this.sessions.get(peerUUID);
    if (!session) {
      return false;
    }
    if (session.timer) {
      clearTimeout(session.timer);
    }
    console.log(`[FasterWhisperHttpSttService] clear session peerUUID=${peerUUID} reason=${reason}`);
    this.sessions.delete(peerUUID);
    return true;
  }

  private deleteIdleSession(peerUUID: string, session: Session): void {
    if (!session.transcribing && !session.recording && session.bytes === 0) {
      this.sessions.delete(peerUUID);
    }
  }
}
