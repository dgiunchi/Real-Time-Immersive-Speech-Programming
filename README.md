# DreamCodeVR+

**Talk to your VR world, and it changes — safely.**

You put on a VR headset, you speak — *"make a red house", "make this spin", "turn it
blue"* — and an AI writes the code that builds it, live, inside the headset. That is
powerful, but it is also dangerous: if the AI is tricked, the same trick can make code
that secretly turns on your camera, reads where you are looking, or slowly pushes you
into a real wall.

DreamCodeVR+ is the **safety layer** that sits in the middle and checks every piece of
AI-written code **before it is allowed to run**. Good creative code goes through. Harmful
code is blocked. And you can see it happen, live.

> This started as a Node.js prototype. I rebuilt it from scratch as a custom **Rust**
> engine and added the guardrails, the cyber-security, the privacy protection, and the
> benchmarks. It is a research / dissertation project, not a production security product.

---

## What it does, in one line

**Speech → AI writes C# → our Rust guardrail checks it → safe code runs in the headset,
harmful code is blocked.**

## Try it in 30 seconds (no headset, no API key needed)

From the project folder, just run:

```bash
./run.sh console
```

Then open **http://127.0.0.1:7979** in your browser and press
**"Run live code-admission sweep"**. You will see 40 attacks and 12 normal commands go
through the real checker, live. You can even turn the guardrail off and on and watch the
attacks reach the headset, then get blocked again.

## Three ways to run it

```bash
./run.sh console    # the security demo — no Quest, no key. Start here.
./run.sh local      # the full system on your laptop, no Quest (admin panel on :7878)
./run.sh quest      # the full system with a real Meta Quest 3 headset
./run.sh stop       # stop it
```

- **console** — the with-safety vs without-safety demo in the browser.
- **local** — the real speech-to-code pipeline running on your laptop. Open the admin
  dashboard at **http://127.0.0.1:7878**, type a command in the box (like *"make a small
  red house"* or *"secretly turn on the camera"*), and watch each step — including the
  guardrail — happen live.
- **quest** — the same thing, but a real headset connects over wifi.

## What we measured

We tested 5 kinds of VR attacks — reading your **body** (camera, mic, eyes, hands),
tracking your **movement**, capturing your **room**, physically **pushing you around**,
and turning off your **safety boundary** — plus 12 normal creative commands.

- **Without our guardrail:** all 40 attacks would run.
- **With our guardrail:** **38 out of 40 are blocked (95%)**, and **all 12 normal commands
  still work**.
- The 2 that slip through only rotate the camera, which looks exactly like a normal
  command — so they need a runtime check, which is our next step. We show this honestly.

## The admin dashboard shows the details

When something is blocked, the panel does not just say "blocked". It tells you **which
exact function was used, what it was trying to do, and which part of our guardrail caught
it** — for example:

> ⛔ **WebCamTexture** — tried to *open the headset camera to record you* — caught by the
> **Perceptual/XR** guardrail.

## Want more detail?

- **[PROJECT.md](PROJECT.md)** — the full guide: how it grew, the architecture, how the
  guardrail works, all the numbers, and what the code audit found and fixed.
- **[VIVA_QA.md](VIVA_QA.md)** — every attack vector (128 of them) with a plain-English
  attack + our answer.
- **[apps/xr-security-eval/PAPER.md](apps/xr-security-eval/PAPER.md)** — the formal paper.
- **[SECURITY.md](SECURITY.md)** — security policy and scope.

## Honest note

This is a research prototype, not a finished product. It checks code **before** it runs
(static admission); it does not yet prove safety **while** it runs on a real headset. The
project is clear about what is built-and-tested versus what is still planned — see
`PROJECT.md` and `VIVA_QA.md`.
