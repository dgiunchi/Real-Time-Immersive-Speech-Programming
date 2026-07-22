const { EventEmitter } = require("events");
const FormData = require("form-data");

const DEFAULT_SAMPLE_RATE = 16000;
const DEFAULT_CHANNELS = 1;
const DEFAULT_BITS_PER_SAMPLE = 16;
const DEFAULT_FINALIZE_AFTER_MS = 1200;
const DEFAULT_MIN_AUDIO_MS = 300;
const DEFAULT_MAX_AUDIO_MS = 20000;

function getNumber(name, fallback) {
    const value = Number(process.env[name]);
    return Number.isFinite(value) && value > 0 ? value : fallback;
}

function getBoolean(name, fallback) {
    const value = process.env[name];
    if (value == undefined || value === "") {
        return fallback;
    }
    return value === "1" || value.toLowerCase() === "true" || value.toLowerCase() === "yes";
}

function durationMsForBytes(bytes, sampleRate, channels, bitsPerSample) {
    const bytesPerSampleFrame = channels * (bitsPerSample / 8);
    return (bytes / bytesPerSampleFrame / sampleRate) * 1000;
}

function createWavBuffer(pcmBuffer, sampleRate, channels, bitsPerSample) {
    const header = Buffer.alloc(44);
    const byteRate = sampleRate * channels * (bitsPerSample / 8);
    const blockAlign = channels * (bitsPerSample / 8);

    header.write("RIFF", 0);
    header.writeUInt32LE(36 + pcmBuffer.length, 4);
    header.write("WAVE", 8);
    header.write("fmt ", 12);
    header.writeUInt32LE(16, 16);
    header.writeUInt16LE(1, 20);
    header.writeUInt16LE(channels, 22);
    header.writeUInt32LE(sampleRate, 24);
    header.writeUInt32LE(byteRate, 28);
    header.writeUInt16LE(blockAlign, 32);
    header.writeUInt16LE(bitsPerSample, 34);
    header.write("data", 36);
    header.writeUInt32LE(pcmBuffer.length, 40);

    return Buffer.concat([header, pcmBuffer]);
}

function postWav(url, wavBuffer) {
    return new Promise((resolve, reject) => {
        const form = new FormData();
        form.append("file", wavBuffer, {
            filename: "utterance.wav",
            contentType: "audio/wav",
            knownLength: wavBuffer.length,
        });
        form.append("language", "en");
        form.append("beam_size", "1");

        form.submit(url, (error, response) => {
            if (error) {
                reject(error);
                return;
            }

            const chunks = [];
            response.on("data", (chunk) => chunks.push(chunk));
            response.on("error", reject);
            response.on("end", () => {
                const body = Buffer.concat(chunks).toString();
                if (response.statusCode < 200 || response.statusCode >= 300) {
                    reject(new Error(`STT HTTP ${response.statusCode}: ${body}`));
                    return;
                }
                resolve(body);
            });
        });
    });
}

class FasterWhisperHttpSttService extends EventEmitter {
    constructor(scene, config = {}) {
        super();

        this.name = "FasterWhisperHttpSttService";
        this.url = process.env.STT_HTTP_URL || (config.stt && config.stt.httpUrl);
        if (!this.url) {
            throw new Error(
                "STT_HTTP_URL is required. Set it to a reachable Faster Whisper endpoint, " +
                "for example http://127.0.0.1:50101/stt/transcribe. No remote fallback is used."
            );
        }
        this.sampleRate = getNumber("STT_SAMPLE_RATE", DEFAULT_SAMPLE_RATE);
        this.channels = getNumber("STT_CHANNELS", DEFAULT_CHANNELS);
        this.bitsPerSample = getNumber("STT_BITS_PER_SAMPLE", DEFAULT_BITS_PER_SAMPLE);
        this.finalizeAfterMs = getNumber("STT_FINALIZE_AFTER_MS", DEFAULT_FINALIZE_AFTER_MS);
        this.minAudioMs = getNumber("STT_MIN_AUDIO_MS", DEFAULT_MIN_AUDIO_MS);
        this.maxAudioMs = getNumber("STT_MAX_AUDIO_MS", DEFAULT_MAX_AUDIO_MS);
        this.requireExplicitRecording = getBoolean("STT_REQUIRE_RECORDING", true);

        this.sessions = new Map();
        this.childProcesses = {};

        this.roomClient = scene && scene.getComponent ? scene.getComponent("RoomClient") : null;
        this.registerRoomClientEvents();

        console.log(
            `[FasterWhisperHttpSttService] ready url=${this.url} ` +
            `format=${this.sampleRate}Hz/${this.channels}ch/${this.bitsPerSample}bit ` +
            `finalizeAfterMs=${this.finalizeAfterMs} minAudioMs=${this.minAudioMs} ` +
            `maxAudioMs=${this.maxAudioMs} requireExplicitRecording=${this.requireExplicitRecording}`
        );
    }

    registerRoomClientEvents() {
        if (!this.roomClient) {
            return;
        }

        this.roomClient.addListener("OnPeerRemoved", (peer) => {
            this.clearSession(peer.uuid, "peer removed");
        });
    }

    getSession(peerUUID) {
        let session = this.sessions.get(peerUUID);
        if (!session) {
            session = {
                chunks: [],
                bytes: 0,
                timer: null,
                transcribing: false,
                recording: false,
            };
            this.sessions.set(peerUUID, session);
            this.childProcesses[peerUUID] = session;
        }
        return session;
    }

    addAudioChunk(peerUUID, audioChunk) {
        if (!peerUUID || !audioChunk) {
            console.warn("[FasterWhisperHttpSttService] dropping chunk: missing peerUUID or audioChunk");
            return false;
        }

        const chunk = Buffer.isBuffer(audioChunk) ? audioChunk : Buffer.from(audioChunk);
        if (chunk.length === 0) {
            return false;
        }

        let session = this.sessions.get(peerUUID);
        if (this.requireExplicitRecording && (!session || !session.recording)) {
            return false;
        }

        session = session || this.getSession(peerUUID);

        session.chunks.push(chunk);
        session.bytes += chunk.length;

        const durationMs = durationMsForBytes(session.bytes, this.sampleRate, this.channels, this.bitsPerSample);

        this.resetFinalizeTimer(peerUUID, session);

        if (durationMs >= this.maxAudioMs) {
            this.finalizePeer(peerUUID, "max duration");
        }

        return true;
    }

    resetFinalizeTimer(peerUUID, session) {
        if (session.timer) {
            clearTimeout(session.timer);
        }

        session.timer = setTimeout(() => {
            this.finalizePeer(peerUUID, "silence timeout");
        }, this.finalizeAfterMs);
    }

    recordingStart(peerUUID) {
        this.clearSession(peerUUID, "recording start");
        const session = this.getSession(peerUUID);
        session.recording = true;
        console.log(`[FasterWhisperHttpSttService] recording start peerUUID=${peerUUID}`);
    }

    recordingStop(peerUUID) {
        const session = this.sessions.get(peerUUID);
        if (session) {
            session.recording = false;
        }
        console.log(`[FasterWhisperHttpSttService] recording stop peerUUID=${peerUUID}`);
        this.finalizePeer(peerUUID, "recording stop");
    }

    async finalizePeer(peerUUID, reason = "finalize") {
        const session = this.sessions.get(peerUUID);
        if (!session || session.bytes === 0) {
            console.log(`[FasterWhisperHttpSttService] no audio to transcribe peerUUID=${peerUUID} reason=${reason}`);
            return false;
        }

        if (session.timer) {
            clearTimeout(session.timer);
            session.timer = null;
        }

        const chunks = session.chunks;
        const bytes = session.bytes;
        const durationMs = durationMsForBytes(bytes, this.sampleRate, this.channels, this.bitsPerSample);

        session.chunks = [];
        session.bytes = 0;
        session.transcribing = true;

        if (durationMs < this.minAudioMs) {
            console.log(
                `[FasterWhisperHttpSttService] discard short utterance peerUUID=${peerUUID} ` +
                `durationMs=${durationMs.toFixed(0)} bytes=${bytes}`
            );
            session.transcribing = false;
            this.deleteIdleSession(peerUUID, session);
            return false;
        }

        const pcmBuffer = Buffer.concat(chunks, bytes);
        const wavBuffer = createWavBuffer(pcmBuffer, this.sampleRate, this.channels, this.bitsPerSample);

        console.log(
            `[FasterWhisperHttpSttService] request start peerUUID=${peerUUID} reason=${reason} ` +
            `audioMs=${durationMs.toFixed(0)} pcmBytes=${bytes} wavBytes=${wavBuffer.length}`
        );

        try {
            const responseText = await postWav(this.url, wavBuffer);
            console.log(`[FasterWhisperHttpSttService] response peerUUID=${peerUUID}: ${responseText}`);
            this.emit("response", Buffer.from(responseText), peerUUID);
        } catch (error) {
            console.error(`[FasterWhisperHttpSttService] request error peerUUID=${peerUUID}: ${error.message}`);
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

    deleteIdleSession(peerUUID, session) {
        if (!session.transcribing && !session.recording && session.bytes === 0) {
            this.sessions.delete(peerUUID);
            delete this.childProcesses[peerUUID];
        }
    }

    clearSession(peerUUID, reason = "clear") {
        const session = this.sessions.get(peerUUID);
        if (!session) {
            return false;
        }

        if (session.timer) {
            clearTimeout(session.timer);
        }

        console.log(`[FasterWhisperHttpSttService] clear session peerUUID=${peerUUID} reason=${reason}`);
        this.sessions.delete(peerUUID);
        delete this.childProcesses[peerUUID];
        return true;
    }

    sendAudioChunk(peerUUID, audioChunk) {
        return this.addAudioChunk(peerUUID, audioChunk);
    }

    sendToChildProcess(peerUUID, data) {
        try {
            const payload = typeof data === "string" ? JSON.parse(data) : data;
            const bytes = payload && payload.data ? payload.data : payload;
            return this.addAudioChunk(peerUUID, Buffer.from(bytes));
        } catch (error) {
            console.warn(
                `[FasterWhisperHttpSttService] could not parse legacy audio payload ` +
                `peerUUID=${peerUUID}: ${error.message}`
            );
            return false;
        }
    }
}

module.exports = {
    FasterWhisperHttpSttService,
    SpeechToTextService: FasterWhisperHttpSttService,
};
