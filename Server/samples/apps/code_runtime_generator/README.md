# Code Runtime Generator Sample

This sample runs DreamCodeVR: Unity sends selected-object context and push-to-talk audio to the Node app. The server transcribes speech with faster-whisper HTTP, sends recognized text to OpenAI, extracts one C# `MonoBehaviour`, and sends it back to Unity to compile and attach in the scene.

## Requirements

- Node.js with npm
- Python `3.10` recommended
- Unity `2021.3.16f1`
- OpenAI API key
- Faster-whisper STT backend reachable at `http://130.136.2.161:50101`

Azure Speech STT is no longer used by this sample.

## Install

From the repo root:

```powershell
cd Server
npm install
```

Create the Python venv in `Server/samples/venv`:

```powershell
cd samples
py -3.10 -m venv .\venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip setuptools wheel
pip install -r requirements.txt
```

The default `requirements.txt` in `Server/samples` is intentionally minimal for this sample and currently installs only the Python OpenAI client used by `services/code_generation/openai_chatgpt_api.py`.

If PowerShell blocks activation:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## Configure

Set secrets and model config in the same terminal that starts `node app.js`:

```powershell
$env:OPENAI_API_KEY="sk-proj-your-real-key"
$env:OPENAI_MODEL="gpt-5.5"
$env:OPENAI_MAX_COMPLETION_TOKENS="1000"
```

Optional STT config:

```powershell
$env:STT_HTTP_URL="http://130.136.2.161:50101/stt/transcribe"
$env:STT_SAMPLE_RATE="16000"
$env:STT_CHANNELS="1"
$env:STT_BITS_PER_SAMPLE="16"
$env:STT_FINALIZE_AFTER_MS="1200"
$env:STT_MIN_AUDIO_MS="300"
$env:STT_MAX_AUDIO_MS="20000"
$env:STT_REQUIRE_RECORDING="true"
```

Health check:

```powershell
curl.exe http://130.136.2.161:50101/health
```

Do not store real API keys in `config.json`. Environment variables override config values.

## Run Server

From `Server/samples/apps/code_runtime_generator`:

```powershell
node app.js
```

The app starts a Ubiq room server using `config.json`:

- TCP port: `8009`
- WSS port: `8010`
- Unity STT input network id: `98`
- Unity codegen output network id: `94`

## Run Unity

1. Open the `Unity` folder with Unity `2021.3.16f1`.
2. Open `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity`.
3. In the scene, select `Network Scene` / `Room Client`.
4. Set server address to the machine running Node. Use `localhost` only when Unity Editor and server run on the same PC.
5. Ensure TCP port is `8009`.
6. Press Play, or build to device.

## Usage Flow

1. Point at a scene object tagged `game`.
2. Red ray means current target object is selected.
3. Hold left controller trigger to record speech.
4. Release left trigger to send utterance to STT.
5. Server receives plain text like `>make this sphere red`.
6. Server strips `>`, sends command to OpenAI, receives C# code, and sends it to Unity.
7. Unity compiles and attaches generated behaviour to the selected object.

## Troubleshooting

- `No connection` in headset: check `Room Client` IP/port and firewall. Device must reach the server PC on TCP `8009`.
- `Missing OpenAI API key`: set `$env:OPENAI_API_KEY` before `node app.js`.
- `Unsupported parameter: max_tokens`: use current code; GPT-5 models use `max_completion_tokens`.
- `Incorrect API key`: key is invalid, expired, copied with typo, or from wrong OpenAI project.
- STT returns nothing: check `curl.exe http://130.136.2.161:50101/health`, microphone permission, and left-trigger push-to-talk.
- Command splits into pieces: hold left trigger for the whole sentence, then release once.
