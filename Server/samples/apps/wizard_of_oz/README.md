# Wizard-of-Oz Study Server

Replaces the live LLM pipeline with researcher-controlled pre-scripted responses,
serves the researcher control panel and participant questionnaire, and logs all
data to CSV.

> Full run instructions are in the top-level [`STUDY_GUIDE.md`](../../../../STUDY_GUIDE.md).

## Start (one command)

```bash
cd Server
npm run study
```

This installs deps if needed, copies certs, starts the server, and opens the
control panel at **http://localhost:8181**.

## Web pages

| URL | Purpose |
|-----|---------|
| `http://localhost:8181/` | Researcher control panel (session, transcript, inject, log) |
| `http://localhost:8181/questionnaire` | Participant questionnaire (SUS + presence + custom) |

## HTTP API

| Method | URL | Body | Effect |
|--------|-----|------|--------|
| GET | `/status` | – | Session, last transcript, active task |
| GET | `/tasks` | – | Tasks with response keys + descriptions |
| POST | `/session` | `{"participantId":"P01","condition":"B"}` | Start/update session |
| POST | `/task` | `{"task":1}` | Set active task (1–4) |
| POST | `/inject` | `{"task":1,"response":"error2"}` | Send pre-scripted code to Unity |
| POST | `/event` | `{"type":"note","detail":"…"}` | Log an event/note |
| POST | `/questionnaire` | `{participantId,condition,answers}` | Save questionnaire CSV |

### Response keys per task
`success`, `error1` (missing detail), `error2` (wrong interpretation),
`error3` (physics/collider – gradual reveal), `error4` (scale/count).

## Data output

`<project root>/Logs/` — `sessions.csv`, `<PID>_events.csv`,
`<PID>_questionnaire.csv` (folder is git-ignored so participant data is never committed).

## Editing the scripted responses

Edit the `SCRIPTS` object in [`app.js`](app.js) — each task maps `success` /
`error1…error4` to a C# MonoBehaviour string that Roslyn compiles and runs in
the Unity scene. Update `DESCRIPTIONS` to change the button labels.
