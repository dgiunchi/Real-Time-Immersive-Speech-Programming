# Server local

Clean Windows PowerShell server for this project.

It keeps only runtime pieces needed by current Unity app:

- Ubiq room server, vendored from Ubiq `unity-v1.0.0-pre.16`
- Ubiq-Genie 0.2.3-style TypeScript app/components
- NetworkId `98` audio/control from Unity
- faster-whisper HTTP STT
- OpenAI code generation
- NetworkId `94` generated C# back to Unity
- file server for `data/`

Removed from old `Server`: Azure STT child process, TTS, image/texture generation, conversational samples, old sample assets, old logs, old Python ML dependencies.

## Current Server Analysis

Old `Server` contains many sample apps. Current project runtime only needs this path:

```text
Unity mic/menu
  -> Ubiq NetworkId 98
  -> app audio receiver
  -> faster-whisper HTTP STT
  -> OpenAI code generator
  -> Ubiq NetworkId 94
  -> Unity dynamic compiler
```

Only necessary old files were:

- `samples/apps/code_runtime_generator/app.js`
- `samples/apps/code_runtime_generator/config.json`
- `samples/apps/code_runtime_generator/data/input.txt`
- `samples/services/speech_to_text/service.js`
- `samples/services/code_generation/service.js`
- `samples/services/code_generation/openai_chatgpt_api.py`
- `samples/services/file_server/service.js`
- `components/application.js`
- `components/message_reader.js`
- `components/service.js`
- `package.json`

This folder replaces them with TypeScript equivalents plus vendored Ubiq server v1. Azure keys/scripts are not used.

## Setup

Prereqs:

- Node.js 20+
- Python 3.10+
- PowerShell

Install Node deps:

```powershell
cd "C:\Users\valla\UnityProjects\GitHub\Real-Time-Immersive-Speech-Programming\Server local"
npm install
```

Create Python venv:

```powershell
py -3 -m venv .\venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

Set secrets for current PowerShell:

```powershell
$env:OPENAI_API_KEY="sk-your-key"
$env:OPENAI_MODEL="gpt-5.5"
```

Optional STT override:

```powershell
$env:STT_HTTP_URL="http://130.136.2.161:50101/stt/transcribe"
```

Run:

```powershell
npm start
```

## Ports

- `50000/tcp`: Ubiq room server. Unity must connect here.
- `50001/tcp`: Ubiq WSS fallback.
- `50002/tcp`: Ubiq status endpoint.
- `3000/tcp`: generated/runtime files from `data/`.

Unity config lives in `Unity/Assets/Demos/Server.asset`. For remote server, set address to static IP and port `50000`.

## STT Flow

1. Unity `MicrophoneCapture.cs` sends packet on NetworkId `98`.
2. First 36 bytes = `peerUUID`.
3. Remaining bytes = PCM int16 little-endian mono 16000 Hz, or control text.
4. Left trigger down sends `__STT_CONTROL__:start`.
5. Left trigger up sends `__STT_CONTROL__:stop`.
6. Server wraps recorded PCM into WAV.
7. Server posts `multipart/form-data` to `/stt/transcribe`.
8. STT returns plain text like `>create a rotating cube`.
9. Server strips `>` and sends recognized text + newline to OpenAI codegen.
10. Generated C# returns to Unity on NetworkId `94`.

## Health Checks

```powershell
curl.exe http://130.136.2.161:50101/health
npm run check
```

Expected server logs when speaking:

```text
[FasterWhisperHttpSttService] recording start peerUUID=...
[FasterWhisperHttpSttService] request start peerUUID=...
[FasterWhisperHttpSttService] response peerUUID=...: >create a rotating cube
Bronze Goose -> Agent:: create a rotating cube
 -> Code:: >...
```

## Notes

Do not put API keys in `config.json`. Use `$env:OPENAI_API_KEY`.

If `Activate.ps1` is missing, venv was deleted or recreated incomplete. Re-run `py -3 -m venv .\venv`.

If Ubiq fails with `EISDIR` and a path ending in `...\Server`, update this folder from git. That was caused by passing a config path with spaces through `npm start`; current code starts Ubiq with `node` args directly.

If Ubiq fails with `EADDRINUSE: ... 50000`, another server is already running. Stop the old `node app.js` / `npm start` process, then run this server again.
