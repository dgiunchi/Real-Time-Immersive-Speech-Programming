# Running the study on Windows

For a second researcher setting up their own machine.

The flow is the same on Windows as on a Mac: **open the project in Unity, press
Build And Run, then paste one command into a terminal.** Nothing about the study
itself differs. Only the shape of that one command changes, and there is a
`study.cmd` in this repository that makes even that identical.

**One-time setup: about an hour**, most of it Unity downloading in the
background. After that, starting a session takes a minute.

---

## What you need

| | |
|---|---|
| **A Windows laptop** | Where you build from and where the data is saved. |
| **The Quest headset** | In developer mode, with a USB-C cable that carries data. |
| **Wi-Fi** | The laptop and the headset on the **same** network. |

> A charge-only USB cable will never show the headset and looks identical to a
> data one. If the headset is invisible later, suspect the cable first.

---

# Part 1 — One-time setup

## Step 1 — Unity Hub and Unity 6000.3.19f1

1. Get **Unity Hub** from <https://unity.com/download> and install it.
2. Sign in (a free personal licence is fine) and activate it.
3. Hub → **Installs** → **Install Editor** → **Archive** tab → the
   *"download archive"* link.
4. Find **Unity 6000.3.19f1** and click its **Unity Hub** button.

   > **The version has to match exactly.** A different version silently
   > re-imports and upgrades the project, and the build stops matching the one
   > the other researcher is running. Two researchers running different builds
   > is not a study.

5. On the modules screen tick **all three**:
   - **Android Build Support**
   - **OpenJDK** (nested under it)
   - **Android SDK & NDK Tools** (nested under it)

   This is what makes a headset build possible. Adding it later means another
   long download.

6. Install. Several gigabytes, so give it time.

## Step 2 — Node.js

<https://nodejs.org> → the green **LTS** button → Next through the installer.
Nothing needs changing.

## Step 3 — Git

<https://git-scm.com/download/win> → Next through it.

## Step 4 — Get the project

Open **Command Prompt** and run these two lines:

```
cd /d %USERPROFILE%
git clone -b Visualisation-DreamCodeVR-feedback-loop https://github.com/abyyworld/Real-Time-Immersive-Speech-Programming.git dreamcodevr
```

That puts everything in `C:\Users\<you>\dreamcodevr`, which every path below
assumes.

> **Not the Desktop, on purpose.** If OneDrive is backing up your PC — the
> default on a new Windows 11 machine — your Desktop is really
> `%USERPROFILE%\OneDrive\Desktop`, and `%USERPROFILE%\Desktop` is a path that
> does not exist. Every `cd` to it answers *"The system cannot find the path
> specified"*, and the folder you are looking at on screen is not the folder
> the command is looking for. Putting the clone one level up sidesteps the
> whole question. It also keeps OneDrive from trying to sync a Unity project,
> which it is bad at and which will slow your builds down.

> If that URL asks for credentials you do not have, ask for access, or for the
> supervisor's copy of the repository instead. Do not download a ZIP of the
> default branch — the study lives on the branch named above, and a ZIP has no
> `.git` folder, so none of the branch commands here will work on it.

## Step 5 — Headset developer mode

1. Pair the headset in the **Meta Horizon** phone app.
2. Headset → **Settings → System → Developer** → turn developer mode and
   **USB Connection Dialog** on.
3. Plug the headset into the laptop.
4. **Put the headset on.** There is an *"Allow USB debugging?"* prompt waiting
   inside it — tick **Always allow**, tap **Allow**.

   You cannot see or accept that prompt from the laptop. This is the single
   most common reason the headset appears to be invisible.

---

# Part 2 — Building onto the headset

## Step 6 — Open the project

Unity Hub → **Open** → select `C:\Users\<you>\dreamcodevr\Unity`.

Select the **`Unity` folder inside**, not the outer `dreamcodevr` folder.

The first open takes **15–40 minutes** while it compiles the project. It will
look frozen. It is not. Let it finish.

## Step 7 — Switch the platform to Android

**This is the step that catches everyone.**

A fresh clone opens set to **Windows**, because that is the machine it is on.
The active platform is stored in `Library/`, which is deliberately not in the
repository, so it cannot travel with the project.

If you press Build And Run now you get a Windows program on your Desktop in
about twenty seconds, **no error**, and nothing on the headset. It looks like it
worked.

So, before building:

1. **File → Build Profiles**
2. Select **Android**
3. Click **Switch Platform**

10–30 minutes the first time, because it re-compresses every texture for
Android. Once only.

You will know it worked because **Android** now has the Unity logo beside it in
that window.

## Step 8 — Build And Run

Headset plugged in and awake:

**File → Build Profiles → Build And Run**

- The **first** build takes **30–40 minutes**. That is normal — it compiles the
  engine to native code. Later builds are a few minutes.
- **Do not unplug the headset.** If it sleeps and drops off USB near the end,
  the build itself is fine but installing fails. Plug it back in and press
  Build And Run again; the second attempt is quick.

When it finishes, the app launches in the headset by itself.

Afterwards you find it under **Apps → Unknown Sources → DreamCodeVR**. It does
not appear in the normal app list — that is where sideloaded apps live, and is
expected.

---

# Part 3 — Running a session

The whole routine, every time.

## Step 9 — Start the study server

Open **Command Prompt** and paste:

```
%USERPROFILE%\dreamcodevr\study
```

This is the same command the other researcher runs, pointed at your copy.
Windows resolves it to `study.cmd`; a Mac resolves it to the `study` script
beside it. Same instruction, either machine.

It asks which mode you want, then starts everything.

> **If your copy lives somewhere else**, the shape is `cd` to the folder, then
> `study`. This is the line-for-line translation of the Mac one:
>
> | | |
> |---|---|
> | Mac | `cd ~/Desktop/"hci-ai projects"/say-it-again && ./study` |
> | Windows | `cd /d "%USERPROFILE%\Desktop\hci-ai projects\say-it-again" && study` |
>
> Three differences, and only three: `/d` after `cd` so it can cross drives,
> the whole path in **one** pair of quotes because it has a space in it, and
> `study` rather than `./study` — Command Prompt has no `./`, and finds
> `study.cmd` in the folder you are standing in by itself.

> **The first time**, it spends a minute or two installing what the server
> needs. Once only.

> **Windows will ask for firewall permission** — *"Allow Node.js to communicate
> on these networks"*. Tick **Private networks** and **Allow access**. If you
> click Cancel the headset cannot reach the laptop; close the window, run the
> command again, and allow it that time.

**Leave that window open for the whole session.** Closing it stops everything.
Minimising is fine.

## Step 10 — Open the panel

The window prints an address like:

```
Panel: http://192.168.1.42:8181
```

Open it in your browser. That is where you run the session from.

## Step 11 — Put the headset on

Open **DreamCodeVR** (Apps → Unknown Sources). It finds the laptop by itself.
There is no address to type anywhere, and it works on whatever Wi-Fi you are
both on that day.

---

## Do a dry run before your first participant

Do not let the first time you run this be with someone sitting in the room.

With the **USB cable plugged in**, go through steps 9–11 and check that:

- The panel opens in the browser.
- The headset app connects, rather than sitting on "Looking for server".
- The panel shows the microphone picking up when you speak.

Keep the cable in for that test. With the cable in, laptop and headset can
always reach each other regardless of the Wi-Fi, so it separates *is the
software working* from *is the network cooperating*.

---

## Where the data goes

```
C:\Users\<you>\dreamcodevr\Logs\
```

One CSV per participant — `P01.csv`, `P02.csv` — holding everything that person
did. Alongside it, `Logs\audio\P01\` holds one short sound file per time they
held the trigger and spoke.

**Back up the whole `Logs` folder after every session, audio included.** It is
kept out of version control on purpose: participant data is never uploaded. That
also means nothing is protecting it but you. Copy it to OneDrive or a memory
stick the same day.

> The audio folder is the part people forget, because it is the only data that
> is not in the CSV. It is also the only data that cannot be regenerated or
> approximated from anything else.

**Agree participant numbers with the other researcher before you start**, so you
do not both create a `P02`. The simplest split is odd numbers for one of you and
even for the other.

---

## Troubleshooting

**`The system cannot find the path specified`**, and you cannot find the clone

Almost always OneDrive. With PC backup on — the default on a new Windows 11
machine — your Desktop is `%USERPROFILE%\OneDrive\Desktop`, and plain
`%USERPROFILE%\Desktop` does not exist at all, so anything under it fails no
matter what you put there. The folder is on the screen in front of you and the
path to it is still wrong.

Rather than guess, ask Windows where the project actually is:

```
where /r %USERPROFILE% StoryTellerManager.cs
```

It prints a full path ending `...\Unity\Assets\StoryTellerManager.cs`. Chop off
the last two folders — `\Unity\Assets\...` — and what is left is the folder to
`cd` into. So a result of

```
C:\Users\you\OneDrive\Desktop\dreamcodevr\Unity\Assets\StoryTellerManager.cs
```

means the command you want is

```
cd /d "C:\Users\you\OneDrive\Desktop\dreamcodevr"
```

If it prints nothing at all, the project is not under your user folder — it may
never have been cloned on this machine, or it is on another drive. Go back to
**Step 4** and clone it fresh; nothing is lost by having a second copy, as long
as you do not delete a `Logs\` folder from the first one.

**`fatal: not a git repository`** in a folder that clearly holds the project

It came from a downloaded ZIP rather than `git clone`. A ZIP contains the files
but not the `.git` folder, so it has no branches and no way to switch to one.
Clone it properly with **Step 4** and use the new folder.

**`The type or namespace name 'Newtonsoft' could not be found`**
**`Error building Player because scripts have compile errors in the editor`**

You are on the wrong branch. This is not a Windows problem and there is nothing
to fix in the code — the clone is simply of `main`, which is an older state of
the project that predates the study: Unity 2021.3.16f1, the old Ubiq, and no
JSON library, which is the missing `Newtonsoft` the compiler is complaining
about. The study lives on `Visualisation-DreamCodeVR-feedback-loop`, and `main`
has never been brought up to match it.

Two symptoms travel together, and either one on its own is enough to tell:

- Unity reports the error on **lines 17 and 18** of `StoryTellerManager.cs`.
  On the study branch those imports sit on lines 16 and 17.
- There is no `study.cmd` and no `WINDOWS_SETUP.md` in the folder — this file
  does not exist on `main`, so if you are reading it on screen rather than on
  GitHub, you are already on the right branch and the cause is something else.

Close Unity first, then, in Command Prompt:

```
cd /d "%USERPROFILE%\dreamcodevr"
git checkout Visualisation-DreamCodeVR-feedback-loop
git pull
```

Reopen `dreamcodevr\Unity` in Unity Hub. It re-imports, which takes a
while, and it will want **Unity 6000.3.19f1** rather than the 2021 version
`main` asked for — install that from Step 1 if Hub says it is missing. Then
carry on from **Step 7**, because switching branch does not switch the build
platform back to Android.

> **Do not fix `main` instead.** Adding the JSON package to it would make it
> compile and would still be the wrong thing to build: it is a different app
> from the one running on the Mac, without the study logging, the agent, or
> the panel. Two researchers running two builds is not a study.

**Build And Run finished in 20 seconds and nothing is on the headset**
You are still on the Windows platform. Go back to Step 7.

**The headset sits on "Looking for server"**
Usually the Wi-Fi blocking devices from seeing each other, which is normal on
university networks. Plug the USB cable in and run the `study` command again —
it tunnels the connection down the cable and the Wi-Fi stops mattering.

Otherwise check both are on the same network, not one on eduroam and one on a
guest network.

**It printed the wrong address, or `127.0.0.1`**
Laptops with a VPN or virtual machines sometimes have the wrong adapter picked.
Find the real one with `ipconfig` (it starts `192.168.` or `10.`), then:

```
set STUDY_LAN_IP=192.168.1.42
%USERPROFILE%\dreamcodevr\study
```

using your actual address.

**Unity cannot find the headset**
Charge-only cable, or the *"Allow USB debugging?"* prompt was never accepted.
Put the headset on and look for it.

**The app is not hearing the participant**
Check the headset's microphone is not muted — Quick Settings inside the headset.
This is a headset setting, nothing to do with the study software, and easy to
knock on by accident.

**`'study' is not recognized as an internal or external command`**
Either the path is wrong, or you are in PowerShell rather than Command Prompt.
PowerShell needs `& "%USERPROFILE%\dreamcodevr\study"` with the
ampersand and quotes. Command Prompt is simpler.

---

## If something goes wrong

Screenshot the terminal window with the error in it and send it on. The text in
that window is what makes a problem fixable; "it did not work" on its own is
very hard to act on.

**Never delete anything in `Logs\`.**
