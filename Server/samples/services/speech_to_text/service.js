const { EventEmitter } = require("events");
const FormData = require("form-data");

const DEFAULT_HTTP_URL = "http://130.136.2.161:50101/stt/transcribe";
const DEFAULT_SAMPLE_RATE = 16000;
const DEFAULT_CHANNELS = 1;
const DEFAULT_BITS_PER_SAMPLE = 16;
// End-of-utterance pause. 800ms was cutting people off mid-sentence: a task
// instruction like "put a red ball ... on the table" has a natural pause in it
// that is longer than that, so the first half was transcribed and sent while
// the participant was still talking. 1500ms is past the length of an ordinary
// planning pause and still short enough not to feel laggy.
const DEFAULT_FINALIZE_AFTER_MS = 1500;
const DEFAULT_MIN_AUDIO_MS = 300;
const DEFAULT_MAX_AUDIO_MS = 20000;

// RMS below this counts as silence. 16-bit PCM normalised to 0..1.
//
// This was 0.012, and that number is why the system "could not hear" people:
// with a quieter mic gain, ordinary speech never crossed it, every chunk was
// classified as silence, and whole utterances were discarded — the logs show
// 20 seconds of audio finalised as "no speech detected". 0.005 is comfortably
// above digital silence and dither but low enough for a quiet headset mic.
//
// Lowering a silence gate normally risks the opposite failure — room noise
// counted as speech, sent to Whisper, and hallucinated into a sentence — so it
// is paired with MIN_SPEECH_CHUNKS below. Tunable via STT_SILENCE_RMS.
const DEFAULT_SILENCE_RMS = 0.005;

// How many chunks must cross the threshold before the audio counts as speech.
// A single blip — a door, a cough, the headset being adjusted — clears an
// amplitude gate easily; a sentence clears it many times over. This is what
// keeps the lower threshold safe.
const DEFAULT_MIN_SPEECH_CHUNKS = 3;

/**
 * Mean amplitude of a PCM16 chunk, normalised to 0..1.
 *
 * This is what makes the finalize timer mean what its name says. The timer used
 * to be reset by every arriving chunk, but chunks arrive continuously for as
 * long as the mic is armed — silence is still audio data. So "silence timeout"
 * only ever detected the absence of *packets*, which happens when the network
 * stalls, not when the participant stops talking. Push-to-talk hid the bug
 * because releasing the trigger finalizes explicitly; any mode that holds the
 * mic open (the panel's record fallback, practice) waited the full maxAudioMs.
 */
function chunkRms(buffer, bitsPerSample) {
    if (bitsPerSample !== 16 || buffer.length < 2) return 0;
    const samples = Math.floor(buffer.length / 2);
    let sumSquares = 0;
    for (let i = 0; i < samples; i++) {
        const s = buffer.readInt16LE(i * 2) / 32768;
        sumSquares += s * s;
    }
    return Math.sqrt(sumSquares / samples);
}

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
        this.url = process.env.STT_HTTP_URL || (config.stt && config.stt.httpUrl) || DEFAULT_HTTP_URL;
        this.sampleRate = getNumber("STT_SAMPLE_RATE", DEFAULT_SAMPLE_RATE);
        this.channels = getNumber("STT_CHANNELS", DEFAULT_CHANNELS);
        this.bitsPerSample = getNumber("STT_BITS_PER_SAMPLE", DEFAULT_BITS_PER_SAMPLE);
        this.finalizeAfterMs = getNumber("STT_FINALIZE_AFTER_MS", DEFAULT_FINALIZE_AFTER_MS);
        this.minAudioMs = getNumber("STT_MIN_AUDIO_MS", DEFAULT_MIN_AUDIO_MS);
        this.maxAudioMs = getNumber("STT_MAX_AUDIO_MS", DEFAULT_MAX_AUDIO_MS);
        this.silenceRms = getNumber("STT_SILENCE_RMS", DEFAULT_SILENCE_RMS);
        this.minSpeechChunks = getNumber("STT_MIN_SPEECH_CHUNKS", DEFAULT_MIN_SPEECH_CHUNKS);
        this.requireExplicitRecording = getBoolean("STT_REQUIRE_RECORDING", true);

        this.sessions = new Map();
        this.childProcesses = {};

        this.roomClient = scene && scene.getComponent ? scene.getComponent("RoomClient") : null;
        this.registerRoomClientEvents();

        console.log(
            `[FasterWhisperHttpSttService] ready url=${this.url} ` +
            `format=${this.sampleRate}Hz/${this.channels}ch/${this.bitsPerSample}bit ` +
            `finalizeAfterMs=${this.finalizeAfterMs} minAudioMs=${this.minAudioMs} ` +
            `maxAudioMs=${this.maxAudioMs} silenceRms=${this.silenceRms} ` +
            `minSpeechChunks=${this.minSpeechChunks} ` +
            `requireExplicitRecording=${this.requireExplicitRecording}`
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
                heardVoice: false,   // guards against transcribing pure silence
                // Measured, not assumed. When an utterance is thrown away as
                // silent, the only question worth answering is "how loud was it
                // actually?" — without that number, tuning the threshold is
                // guesswork and "it cannot hear me" has no evidence attached.
                peakRms: 0,
                speechChunks: 0,
                totalChunks: 0,
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

        // Arm the end-of-utterance timer on speech only. A chunk of silence must
        // NOT push the deadline back, or the utterance never ends while the mic
        // is held open. Silence arriving after speech is exactly what should let
        // the timer run down and fire.
        const rms = chunkRms(chunk, this.bitsPerSample);
        session.totalChunks += 1;
        if (rms > session.peakRms) session.peakRms = rms;
        if (rms >= this.silenceRms) {
            session.speechChunks += 1;
            // One loud chunk is a noise; several is a sentence. Holding
            // heardVoice back until the count is met is what lets the threshold
            // sit low enough for a quiet mic without feeding Whisper a room.
            if (session.speechChunks >= this.minSpeechChunks) session.heardVoice = true;
            this.resetFinalizeTimer(peerUUID, session);
        }

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
        session.heardVoice = false;
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
        const heardVoice = session.heardVoice;
        const peakRms = session.peakRms;
        const speechChunks = session.speechChunks;
        const totalChunks = session.totalChunks;
        const durationMs = durationMsForBytes(bytes, this.sampleRate, this.channels, this.bitsPerSample);

        session.chunks = [];
        session.bytes = 0;
        session.heardVoice = false;
        session.peakRms = 0;
        session.speechChunks = 0;
        session.totalChunks = 0;
        session.transcribing = true;

        // Never send pure silence. Whisper does not return empty for silent
        // audio — it hallucinates a plausible sentence ("Thank you.", "you"),
        // which would land in the transcript as if the participant had spoken
        // and would be counted as a repair attempt.
        if (!heardVoice) {
            // The peak is the actionable half of this message. "No speech
            // detected" alone cannot distinguish a dead microphone from a live
            // one whose level sits under the threshold, and those need opposite
            // fixes. peak=0.000 means no signal; peak=0.004 against a 0.005
            // threshold means the gate is set wrong, and by how much.
            const detail =
                `${durationMs.toFixed(0)}ms of audio, no speech detected ` +
                `(peak level ${peakRms.toFixed(4)}, threshold ${this.silenceRms}, ` +
                `${speechChunks}/${totalChunks} chunks over)`;
            console.log(
                `[FasterWhisperHttpSttService] discard silent utterance ` +
                `peerUUID=${peerUUID} ${detail} reason=${reason}`
            );
            // Also announced, not just logged. An utterance dropped here looks
            // identical from the control panel to one that was never spoken:
            // the transcript simply never changes. Saying which happened is the
            // difference between "the mic is dead" and "it heard only silence".
            this.emit("diagnostic", {
                kind: "silent", peerUUID, durationMs: Math.round(durationMs),
                reason, detail,
                peakRms: +peakRms.toFixed(4), speechChunks, totalChunks,
                threshold: this.silenceRms
            });
            session.transcribing = false;
            this.deleteIdleSession(peerUUID, session);
            return false;
        }

        if (durationMs < this.minAudioMs) {
            console.log(
                `[FasterWhisperHttpSttService] discard short utterance peerUUID=${peerUUID} ` +
                `durationMs=${durationMs.toFixed(0)} bytes=${bytes}`
            );
            this.emit("diagnostic", {
                kind: "short", peerUUID, durationMs: Math.round(durationMs), reason,
                detail: `${durationMs.toFixed(0)}ms of audio, too short to transcribe ` +
                        `(minimum ${this.minAudioMs}ms, peak level ${peakRms.toFixed(4)})`,
                peakRms: +peakRms.toFixed(4)
            });
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

        this.emit("diagnostic", {
            kind: "sending", peerUUID, durationMs: Math.round(durationMs), reason
        });

        try {
            const responseText = await postWav(this.url, wavBuffer);
            console.log(`[FasterWhisperHttpSttService] response peerUUID=${peerUUID}: ${responseText}`);
            this.emit("response", Buffer.from(responseText), peerUUID);
        } catch (error) {
            console.error(`[FasterWhisperHttpSttService] request error peerUUID=${peerUUID}: ${error.message}`);
            // A transcription server that is down or unreachable produced no
            // visible symptom at all beyond an empty transcript.
            this.emit("diagnostic", {
                kind: "error", peerUUID, detail: error.message, url: this.url
            });
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
