# DreamCodeVR
DreamCodeVR is a Unity project developed at UCL based on Ubiq Social VR Platform and Ubiq Genie (plugin for backend services). It is designed to assist users, irrespective of their coding skills, in crafting basic object behavior in VR environments by translating spoken language into
code within an active application. 

![Illustrations of DreamcodeVR concept](DCVR.png)

## Start here

Three steps, identical on Windows and on a Mac. Roughly an hour the first time,
nearly all of it Unity downloading while you do something else.

### 1. Get the project

**The `-b` is not optional.** The study lives on a branch; the default branch is
an older state of the project that does not compile against this Unity version.

Open a terminal — **Command Prompt or PowerShell on Windows, Terminal on a
Mac** — and paste this. It is the same on all three:

```
git clone -b Visualisation-DreamCodeVR-feedback-loop https://github.com/abyyworld/say-it-again.git say-it-again
```

> One command, no `cd`, and nothing that differs between shells, which is
> deliberate. All three open in your home folder, and giving `git clone` the
> destination as its last word means the project lands there — Windows
> `C:\Users\<you>\say-it-again`, Mac `~/say-it-again` — without a second line
> that would need `cd /d` in Command Prompt, `cd` in PowerShell, and `&&` in
> neither the same way. If you have ever pasted a Windows command and been told
> *"The token '&&' is not a valid statement separator"*, that is Command Prompt
> syntax landing in PowerShell, and it is why there is none of it here.

> **Your home folder, not the Desktop, on purpose.** With OneDrive PC backup on
> — the default on a new Windows 11 machine — your Desktop really lives at
> `%USERPROFILE%\OneDrive\Desktop`, and plain `%USERPROFILE%\Desktop` does not
> exist. Anything under it fails with *"The system cannot find the path
> specified"* while the folder sits visible on screen. Cloning to the home
> folder avoids the question, and keeps OneDrive from trying to sync a Unity
> project, which it is bad at and which slows every build down.

> **If you skip the `-b`** you get the default branch, and Unity greets you with
> `The type or namespace name 'Newtonsoft' could not be found` followed by
> `Error building Player because scripts have compile errors in the editor`.
> That error means the branch, not your machine. Re-clone with the line above.

### 2. Build onto the headset

Open **`say-it-again/Unity`** — the inner folder, not the outer one — in Unity
**`6000.3.19f1`**, installed with **Android Build Support**.

Then **File → Build Profiles → Android → Switch Platform**, and only after that
**Build And Run**. A fresh clone opens on the desktop platform, and building
without switching produces a desktop app in twenty seconds with no error and
nothing on the headset.

### 3. Start the server

One command. Which shell you are in decides how it is written:

| | |
|---|---|
| **Command Prompt** | `%USERPROFILE%\say-it-again\study` |
| **PowerShell** | `& $HOME\say-it-again\study.cmd` |
| **macOS / Linux** | `~/say-it-again/study` |

Windows runs `study.cmd`, a Mac the `study` script beside it. Same script, same
questions, same panel. It installs what it needs on first run, asks which mode
you want, and prints the address of the browser panel you drive the session
from. Leave the window open; closing it stops everything.

> **PowerShell is the awkward one**, and it is what Windows 11 opens by
> default, so it is probably what you have. It does not expand `%USERPROFILE%`
> — that is `$HOME` or `$env:USERPROFILE` — and it will not run a path as a
> command without the `&` in front. Typing `cmd` and pressing Enter first drops
> you into Command Prompt, where the shorter line works, and is the easier fix
> if a command from somewhere else is refusing to run.

If your copy is somewhere else, `cd` to it and run the script from there:

| | |
|---|---|
| **Command Prompt** | `cd /d "C:\path with spaces\say-it-again"` then `study` |
| **PowerShell** | `cd "C:\path with spaces\say-it-again"` then `.\study.cmd` |
| **macOS / Linux** | `cd "~/path with spaces/say-it-again"` then `./study` |

Chaining those with `&&` works in Command Prompt and on a Mac, but **not** in
Windows PowerShell 5.1, which answers *"The token '&&' is not a valid statement
separator in this version."* Use `;` there, or just run the two lines.

**Windows users:** [WINDOWS_SETUP.md](WINDOWS_SETUP.md) expands all of this from
a blank laptop, with the headset developer-mode steps and a troubleshooting
list. Printable copy: [docs/WINDOWS_SETUP.pdf](docs/WINDOWS_SETUP.pdf).

---

Dreamcode VR is based on Ubiq and Ubiq Genie. Ubiq is a framework developed by UCL for social VR experiments, and Ubiq-Genie is a framework that enables you to build server-assisted collaborative mixed reality applications with Unity using the [Ubiq](https://ubiq.online) framework.

## Current Setup

Reference detail for the current DreamCodeVR fork. To just get it running, use
[Start here](#start-here) above — this section is the longer form.

### Required tools

- Unity `6000.3.19f1` — the version in `Unity/ProjectSettings/ProjectVersion.txt`,
  and it has to match: opening in another version re-imports and upgrades the
  project, so two researchers stop running the same build
- Node.js with npm
- Python `3.12` recommended for `Server/samples/venv`
- OpenAI API key for code generation
- Access to faster-whisper STT HTTP at `http://130.136.2.161:50101`

### Install server dependencies

```powershell
cd Server
npm install
```

```powershell
cd samples
py -3.12 -m venv .\venv
.\venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
```

If PowerShell blocks activation:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

### Configure API keys

Set secrets in the same terminal before starting the server. Do not commit API keys.

```powershell
$env:OPENAI_API_KEY="sk-proj-your-real-key"
$env:OPENAI_MODEL="gpt-5.5"
$env:OPENAI_MAX_COMPLETION_TOKENS="1000"
```

Current STT uses faster-whisper HTTP, not Azure Speech STT. Defaults:

```powershell
$env:STT_HTTP_URL="http://130.136.2.161:50101/stt/transcribe"
$env:STT_SAMPLE_RATE="16000"
$env:STT_CHANNELS="1"
$env:STT_BITS_PER_SAMPLE="16"
$env:STT_REQUIRE_RECORDING="true"
```

Health check:

```powershell
curl.exe http://130.136.2.161:50101/health
```

### Run DreamCodeVR

```powershell
cd Server\samples\apps\code_runtime_generator
node app.js
```

Then open the Unity project from the `Unity` folder with Unity `6000.3.19f1`, open `Unity/Assets/Demos/DynamicCompiler/DynamicCompiler.unity`, verify the `Room Client` points to the server IP and TCP port `8009`, then press Play or build to device.

In VR, hold the left controller trigger to record speech. Release it to send the utterance to STT. Point at an object with the ray to select it; red ray means selected target.

### Git hygiene

Generated folders such as `Server/samples/venv`, `node_modules`, Unity `Library`, `Temp`, `obj`, runtime logs, and sample input/output files are ignored. If any of those are already tracked, remove them from Git with `git rm --cached` so they stay on disk but stop being pushed.

## Features

Ubiq's goal is to enable your networked project. It includes message passing, room management, rendezvous and matchmaking, object spawning, shared binary blobs, multiple synchronisation models, lighweight XR interaction examples, customisable avatars and voice chat across Windows, Linux, Android, MacOS, and Javascript running in the browser.

## Quick Start

These instruction will get you a copy of the project up and running on your local machine to run the samples and to start building your own applications. Alternatively, [GitHub Codespaces](https://docs.github.com/en/codespaces) can be used to quickly set up a server and run the server-side components of the samples (in this case, you may skip installing Node.js, step 2, and step 3).

0. Install [Unity](https://unity3d.com/get-unity/download) and [Node.js](https://nodejs.org/en/download/).

1. Clone this repository somewhere on your local PC.

```
git clone git@github.com:UCL-VR/ubiq-genie.git Ubiq-Genie
```

2. Open a terminal in the `Genie` folder and run `npm install` to install the dependencies. This includes the Node.js server of Ubiq.

3. Create a virtual environment and install the Python dependencies:

    ```
    python -m venv venv
    source venv/bin/activate
    pip install -r requirements.txt
    ```

    Note: on Windows, run `venv\Scripts\activate.bat` instead of `source venv/bin/activate`.

4. In Unity, open the `Unity` folder. To add Ubiq to the `Unity Hub`, open the `Unity Hub`, click `Add`, then navigate to `/Ubiq/Unity` and click `Select Folder`.

5. Read the README file in the corresponding folder in the `Server/samples/apps` folder for further setup instructions. For a list of available samples, see the [Samples](#samples) section below.

## Code Runtime Generator

The `Server/samples` folder contains a number of samples that demonstrate how to use Ubiq-Genie and so DreamcodeVR. For more information on how to use these samples, please refer to the README files in the corresponding folders. Currently, the following collaborative samples are available:

- [**DreameCodeVR**](Server/samples/apps/code_runtime_generator/README.md): generates a code that will be used to build procedural behaviours inside the 3D environment.

## Other Samples from Ubiq Genie

The `Genie/samples` folder contains a number of samples that demonstrate how to use Ubiq-Genie. For more information on how to use these samples, please refer to the README files in the corresponding folders. Currently, the following collaborative samples are available:

- [**Texture Generation**](Server/samples/apps/texture_generation/README.md): generates a texture based on voice-based input and an optional ray to select target objects
- [**Multi-user Conversational Agent**](Server/samples/apps/conversational_agent/README.md): a conversational agent that can be interacted with by multiple users
- [**Transcription**](Server/samples/apps/transcription/README.md): transcribes audio streams of users in a room

For a demo video of the samples, please refer to the [Ubiq-Genie demo video](https://youtu.be/cGz0z9BIgQk).


## to do list

- add components (done)

- add objects (done)

- track all the objects in the scene

- track all components in the scene

- replace object (versioning)

- replace component (versioning)

- a custom component defined in framework needs to be known by compiler

- positioning in the hierarchy and the 3D space

- change values of public variables of the components (include linking objects)

- make an interaction between two (or more) objects (need a visible ID for the object known by the model)

- ​improve prompt programming (probably needs to be dynamic; prompt needs to change during the session)

- add interaction for checking only when use activate the recording session

- upgrade to ubiq 0.5.0

 

General Aspects to be tackled

- clashing between valid instructions, conflict handler 

- how to deal with code that does not work or not doing the required action (but can build, which visual feedback? how to edit?)

- security (we can inject what we want in a Quest)

- for the collaborative aspect, make all networked objects
    -- collaborative dynamic programming
       - the object has to be networked for the parameters need to be shared (gpt this for creating a prompt https://ucl-vr.github.io/ubiq/creatinganetworkedobject/)
       - no clashing


- store the created scene  (do not know if it is possible - versioning) (and so restore)
