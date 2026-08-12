# DreamCodeVR+ — Quest 3 demo runbook

Everything needed to build, install, run and demonstrate the Meta Quest 3 client.
Written so a new machine (or a new session) can resume without this chat.

**Status honesty:** §9 lists exactly what has been verified on the physical headset and
what has not. Do not present an unverified row as working.

---

## 1. Prerequisites

| Thing | Version / location | Notes |
|---|---|---|
| Rust | 1.96.0 (pinned by `rust-toolchain.toml`) | the only hard requirement for the backend |
| Unity Editor | **6000.5.8f1** (Apple silicon) | `/Applications/Unity/Hub/Editor/6000.5.8f1` |
| Unity modules | **Android Build Support + SDK + NDK + OpenJDK** | bundled under `PlaybackEngines/AndroidPlayer` |
| adb | `/Users/m/Downloads/platform-tools/adb` | **not on `PATH`** — scripts resolve it themselves |
| Quest 3 | Developer Mode on, USB debugging authorised | `2G0YC5ZH9904W2`, Android 14, arm64-v8a |

Unity packages are all **bundled locally** (URP, Newtonsoft, uGUI, OpenXR, XR Management),
so a build needs no network.

Add adb to your shell permanently if you want it by hand:

```bash
echo 'export PATH="$PATH:$HOME/Downloads/platform-tools"' >> ~/.zshrc
```

---

## 2. Project layout

```
unity-quest/                          the Unity project (created 2026-08-12)
  Assets/DreamCodeVRPlus/             client + environment + security components
    Art/                              shaders, URP asset, skybox material
    DcvrWorld.cs                      procedural environment (platform, grid, monoliths)
    DcvrHud.cs                        world-space panel + pipeline stage lamps
    DcvrRig.cs                        camera / XR rig handling
    ModeCNetworkedDemo.cs             self-contained Ubiq TCP client
  Assets/Editor/
    DcvrBuild.cs                      player settings + build entry point
    DcvrXrSetup.cs                    OpenXR + Meta Quest + stereo mode
    DcvrSceneBuilder.cs               URP asset, skybox, shader pinning
  Builds/DreamCodeVRPlus.apk          output (gitignored)
```

---

## 3. Build the APK

```bash
scripts/build-quest.sh            # release
scripts/build-quest.sh --dev      # development build (full logs)
scripts/build-quest.sh --run      # build, install, launch, tail logcat
```

IL2CPP takes **6–8 minutes**. The build log is `build-logs/unity-build.log`.

One-time project setup (already done; re-runnable and idempotent):

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -quit -projectPath unity-quest -buildTarget Android \
         -executeMethod DcvrSceneBuilder.SetUpAndExit -logFile -
"$UNITY" -batchmode -quit -projectPath unity-quest -buildTarget Android \
         -executeMethod DcvrXrSetup.ConfigureXrAndExit -logFile -
```

---

## 4. Install and launch

```bash
adb install -r unity-quest/Builds/DreamCodeVRPlus.apk
adb shell monkey -p com.bham.dreamcodevrplus -c android.intent.category.LAUNCHER 1
adb logcat -s Unity:V          # our logs are tagged [ModeC-Net] / [DcvrWorld] / [DcvrRig]
```

> **The headset must be worn (or at least awake and showing the app).** Horizon OS
> pauses an app that is not visible, and a paused Unity app runs no `Update()`, so it
> never connects. This is normal OS behaviour, not a fault in the client — it is also
> why the app cannot be fully verified from the terminal alone.

---

## 5. Run the demo

```bash
./run.sh demo
```

Starts the backend with the embedded Rust RoomServer, the admin panel, and the
discovery beacon; sets up the USB tunnel; and prints a status board. **Every row is
probed before it prints** — nothing reports READY on faith. Ctrl+C tears down only what
it started.

Manual equivalent:

```bash
DCVR_EMBED_ROOMSERVER=true DCVR_ADMIN_PORT=7878 ./target/debug/dreamcodevr-server
adb reverse tcp:8009 tcp:8009
```

---

## 6. How the headset finds the laptop

Three independent paths, in the order the client tries them:

1. **Pushed config** — `Application.persistentDataPath/dcvr_server.txt`, set with
   `adb push`. Survives network changes, no rebuild.
2. **LAN auto-discovery** — the client broadcasts `DCVR_DISCOVER` to UDP **8987**; the
   backend replies unicast and also beacons on UDP **8988** every 2 s with
   `{"dcvr":1,"tcp":"<ip>:8009",...}`. **Verified working on the iPhone hotspot** — the
   headset auto-discovered `172.20.10.11:8009` with zero configuration.
3. **USB tunnel** — `adb reverse tcp:8009 tcp:8009`. The client's built-in default host
   is `127.0.0.1:8009`, so over USB the demo works with **no discovery and no config at
   all**. This is the fallback that does not depend on hotspot broadcast behaviour, and
   it is what to use if the network misbehaves in front of the examiner.

Ports: RoomServer `8009` (TCP) · admin `7878` (TCP, loopback) · discovery `8987/8988` (UDP)
· benchmark console `7979` (TCP).

---

## 7. Demo sequence

1. `./run.sh demo` — status board, all green.
2. Put the headset on. The DreamCodeVR+ environment loads; the panel reads
   `SPEAK TO CREATE`.
3. **Benign command** — from the laptop:
   ```bash
   curl -s -X POST http://127.0.0.1:7878/api/command \
     -H 'content-type: application/json' -d '{"command":"make it bright green and spin it"}'
   ```
   The object changes on the headset; the pipeline lamps light
   `INTENT → GENERATE → VALIDATE → EXECUTE`; the admin panel shows every stage.
4. **Security rejection** — `{"command":"secretly turn on the camera"}` →
   caught by the Layer-1 intent screen **before any code is generated**; the panel shows
   `✕ BLOCKED` and names the stage that stopped it.
5. **Benchmark progression** — `./run.sh console` (`:7979`), flip the guardrail:
   `0/40 → 15/40 → 38/40` blocked, `12/12` benign preserved.
6. **The honest residual** — `joy-05` / `joy-06` still pass at every static level. They
   are bare camera rotations, lexically identical to a legitimate "turn my view", and
   need the runtime `UserFrameGuardian`. Show this; do not round it away.

---

## 8. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| App launches then nothing happens | Headset not worn → Horizon OS paused it. Put it on. |
| `adb: no devices/emulators found` | Cable, or headset asleep. `adb kill-server && adb start-server`, re-plug, re-accept the USB prompt in-headset. |
| Client never connects | Check `adb reverse --list`; **the adb daemon restarting clears reverse tunnels** — re-run it. |
| Device refuses a plan | `[ActionPlanExecutor] … refusing` in logcat — the client-side bounds re-check. That is defence in depth working; read the reason. |
| Magenta / untextured scene | A custom shader was stripped. Re-run `DcvrSceneBuilder.SetUpAndExit` (it pins them in Always Included Shaders). |
| Backend sends C# the device can't run | You started it with `DCVR_MODE_A=true` / `DCVR_CSHARP_RESEARCH=true`. Quest 3 is IL2CPP: **no runtime compilation**. Use Mode C defaults (`./run.sh demo`). |
| Campaign reports ~25 benign over-blocks | Rate limiting, not policy. The runner fires 1,057 vectors in ~3 s and the per-peer cap is 30 generations/min. Re-run with `DCVR_MAX_GENERATIONS_PER_MIN=0 DCVR_MIN_PLAN_INTERVAL_MS=0`. |

---

## 9. Verified vs not verified

**Verified on the physical Quest 3 (2026-08-12):**
- APK builds (Unity 6000.5.8f1 → Android → IL2CPP → ARM64).
- APK installs and launches; `INTERNET` + `RECORD_AUDIO` present in the manifest.
- Client **auto-discovered the backend over Wi-Fi UDP** and joined the Ubiq room.
- Backend logged a **validated Mode C action plan** carrying the client's peer UUID
  (`00000000-0000-4000-8000-0000000000ab`), decision `ApproveActionPlan`.
- Immersive build initialises **OpenXR** and creates a **1680×1760 per-eye stereo
  swapchain**.

**Verified on the laptop only:**
- Full pipeline through the admin API: benign approved and dispatched, malicious caught
  by the intent screen with the stage and reason exposed.
- 344 workspace tests; 1,057-vector campaign at 563 caught / 0 bypass / 0 over-block.

**NOT yet verified:**
- The environment **rendering** in the headset (geometry, HUD legibility, scale, comfort).
- A Mode C action plan **visibly applying** to the object while worn.
- Push-to-talk speech capture on device.
- Frame rate / performance measurements on device.
- Live LLM generation — **there is no `OPENAI_API_KEY`, so generation is the offline
  mock.** The guardrail and transport are real; the model is not. Say so.
