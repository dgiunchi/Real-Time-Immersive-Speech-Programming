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

## Step 3 — Git, and it must be on the PATH

<https://git-scm.com/download/win> → Next through it, but **do not change the
PATH screen**. Leave it on the default, *"Git from the command line and also
from 3rd-party software"*.

That screen is the whole reason this step has a warning. Unity does not have Git
built in: it shells out to the `git` on your PATH. Two of this project's
packages are fetched from GitHub rather than from Unity's registry —

```
com.ucl.ubiq                 github.com/UCL-VR/ubiq.git
com.unity.webrtc-ubiq-fork   github.com/UCL-VR/unity-webrtc-ubiq-fork.git
```

— so if Unity cannot run `git`, it cannot resolve them, and **the project will
not open.** Depending on the version it either hangs on the loading bar, closes
again immediately, or opens with the scene empty and errors in the console. None
of those says "install Git properly", which is why this costs people an evening.

It does not happen on macOS, because git is on the PATH there by default. It is a
Windows-only failure and it looks like the project is broken.

**Verify before going further.** Open a *new* Command Prompt — a window opened
before the install still has the old PATH — and run:

```
git --version
```

A version number means Unity will find it. *"'git' is not recognized"* means it
will not: re-run the Git installer and choose the default PATH option. If Unity
was already open, quit and reopen it, because it reads the PATH at startup.

Then one line in Command Prompt, before you clone anything:

```
git config --global core.longpaths true
```

> **This one is not optional on Windows.** The longest file in this repository
> is 125 characters:
>
> ```
> Assets/RoslynCSharp/Scripts/RoslynCSharp.Compiler/Runtime/AssemblyReference/AssemblyReferenceFromAssemblyObject.cs.meta
> ```
>
> Windows will not create a path longer than 260, which leaves 134 characters
> for the folder you clone into. `C:\Users\<you>\say-it-again` is around 27, so
> the clone in Step 4 is safe — but clone somewhere deeper, or into a OneDrive
> folder carrying your university's full name, and git writes most of the
> project and then stops with
>
> ```
> error: unable to create file Assets/RoslynCSharp/... : Filename too long
> fatal: unable to checkout working tree
> ```
>
> The trap is that it half-succeeds. You are left with a folder that looks like
> the project, quietly missing a dozen `.cs` and `.cs.meta` files. A Unity
> project missing `.meta` files does not report an error — it regenerates them
> with brand new GUIDs, and every reference that pointed at the old ones breaks
> instead. `core.longpaths` removes the limit, and the setting is global, so
> this is the only time you will need it.

## Step 4 — Get the project

Open **Command Prompt** and run this one line:

```
git clone --depth 1 https://github.com/abyyworld/say-it-again.git %USERPROFILE%\say-it-again
```

That puts everything in `C:\Users\<you>\say-it-again`, which every path below
assumes.

> **Do not drop the `--depth 1`.** The full history is about **1.3 GB** — old
> Unity scenes, compiler DLLs and a 31 MB test recording, all still present in
> past commits. The files you need are 108 MB. A plain clone downloads the lot
> and, on anything less than a solid connection, dies partway with
>
> ```
> fatal: fetch-pack: invalid index-pack output
> fetch-pack: unexpected disconnect while reading sideband packet
> ```
>
> That looks like a corrupt repository. It is a dropped download. `--depth 1`
> fetches the current version of each file and skips the history, about 76 MB,
> and gives you everything the study needs.

> **One line, no `cd`.** Giving `git clone` the destination as its last argument
> means it works the same whether you are in Command Prompt, PowerShell, or a
> Mac Terminal. A separate `cd` would need `cd /d` in one and `cd` in another,
> and joining the two with `&&` fails outright in PowerShell.

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

Unity Hub → **Open** → select `C:\Users\<you>\say-it-again\Unity`.

Select the **`Unity` folder inside**, not the outer `say-it-again` folder. The
outer one is not a Unity project, and the Hub will not say so clearly — it
simply will not open.

Select the **`Unity` folder inside**, not the outer `say-it-again` folder.

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
%USERPROFILE%\say-it-again\study
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
C:\Users\<you>\say-it-again\Logs\
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

**The project will not open. Pressing Open in Unity Hub does nothing, hangs on
the loading bar, or closes straight back to the Hub**

Almost always **Git is not on the PATH**, so Unity cannot fetch the two packages
it pulls from GitHub. See Step 3. Check it in a *new* Command Prompt:

```
git --version
```

No version number means that is the cause. Re-run the Git installer, keep the
default PATH option, then quit Unity Hub completely and reopen it.

To confirm it rather than guess, open the Package Manager log — it names the
failure outright, usually `Cannot perform upm operation` or a git error against
`github.com/UCL-VR/...`:

```
%LOCALAPPDATA%\Unity\Editor\upm.log
```

Two other causes worth ruling out, in order:

- **You selected the wrong folder.** It must be `say-it-again\Unity`, not the
  outer `say-it-again`. The outer folder is not a Unity project and the Hub will
  not tell you so clearly.
- **It is working and looks frozen.** The first open takes 15–40 minutes with no
  progress for long stretches. Before deciding it has failed, leave it a full
  hour and check whether `Unity\Library` is growing on disk. If it is, it is
  importing, not stuck.

**The build sits on "Compiling libil2cpp" / "il2cpp arm64" for ages**

Usually it is working. IL2CPP translates the whole game and engine to C++ and
then compiles that for the headset's processor. On this project that is **780
generated .cpp files and about 2.8 GB of output**, so the first build on a
machine takes **30–45 minutes**, most of it on this one step with no progress
bar moving. Later builds are a few minutes because the work is cached.

**Tell working from stuck** rather than guessing. In Explorer, watch:

```
say-it-again\Unity\Library\Bee\artifacts\Android
```

If its size is climbing, it is compiling. Task Manager showing a busy CPU across
several cores says the same thing. Neither moving for ten minutes means stuck.

**If it is genuinely slow, it is almost always the antivirus.** Windows Defender
scans every one of those thousands of generated files as it is written, and that
can double or triple the build. Add an exclusion for the project folder:

*Windows Security → Virus & threat protection → Manage settings →
Exclusions → Add an exclusion → Folder →* `C:\Users\<you>\say-it-again`

This is safe — it is your own source tree, not downloaded binaries — and it is
the single biggest build-time win on Windows.

Two other things that stall this step:

- **Disk space.** The Bee cache alone reaches about 13 GB, and IL2CPP needs
  room for the C++ on top. Under ~20 GB free, the build can stop without a
  clear error.
- **Sleep.** If the laptop sleeps mid-build the toolchain does not always
  recover. Set it not to sleep on mains power before starting the first one.

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
%USERPROFILE%\say-it-again\study
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
The path is wrong. Check the folder is where you think it is — see the first
entry in this list.

**`The token '&&' is not a valid statement separator in this version`**
**`A positional parameter cannot be found that accepts argument ...`**
**`Missing opening '(' after keyword 'for'`**

You are in **PowerShell**, and every command in this guide is written for
**Command Prompt**. Windows 11 opens PowerShell by default, and its prompt
starts `PS C:\Users\you>` — that `PS` is the tell.

The two shells are not compatible, and the errors say so in a way that sounds
like your command is malformed rather than pasted into the wrong program.
`cd /d`, `&&`, `where /r` and `for /f` are all Command Prompt syntax and none of
them exist in PowerShell.

The fix is one word. Type:

```
cmd
```

and press Enter. The prompt changes to `C:\Users\you>` with no `PS`, and every
command in this guide works as written. Type `exit` to go back.

If you would rather stay in PowerShell, the same three commands are:

| Command Prompt | PowerShell |
|---|---|
| `%USERPROFILE%\say-it-again\study` | `& $HOME\say-it-again\study.cmd` |
| `cd /d "C:\some\folder"` | `cd "C:\some\folder"` |
| `where /r %USERPROFILE% StoryTellerManager.cs` | `Get-ChildItem $HOME -Filter StoryTellerManager.cs -Recurse -ErrorAction SilentlyContinue \| % FullName` |

PowerShell does not expand `%USERPROFILE%` — use `$HOME` — and it will not run
a path as a program without `&` in front of it.

---

## If something goes wrong

Screenshot the terminal window with the error in it and send it on. The text in
that window is what makes a problem fixable; "it did not work" on its own is
very hard to act on.

**Never delete anything in `Logs\`.**
