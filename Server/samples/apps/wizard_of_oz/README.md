# Wizard-of-Oz Server – Study Setup

Replaces the live LLM pipeline with researcher-controlled pre-scripted responses.

## Start

```bash
cd Server
node scripts/start-wizard-of-oz.js
```

## Researcher API (HTTP on port 8181)

| Method | URL | Body | Effect |
|--------|-----|------|--------|
| GET | `/status` | – | Last transcript + active task |
| GET | `/tasks` | – | All tasks and valid response keys |
| POST | `/task` | `{"task":1}` | Set the active task (1–4) |
| POST | `/inject` | `{"task":1,"response":"success"}` | Send pre-scripted code to Unity |

### Response keys per task
- `success` – correct outcome
- `error1` – missing / ambiguous detail
- `error2` – wrong interpretation
- `error3` – physics/collider issue (gradual reveal)
- `error4` – scale or count issue

## Study Conditions

Set `activeCondition` on the `StudyConditionManager` component in Unity before each session:
- **A** – No feedback: participant sees only the scene result.
- **B** – Text panel: `FeedbackPanelController` shows transcript + error description.
- **C** – Embodied agent: `EmbodiedAgentDialogue` speaks pre/post response; panel also visible.

## Keyboard shortcuts (Unity)
- **Space** – hold to record voice (desktop / standalone Unity Editor)
- **F12** – toggle the Wizard-of-Oz researcher panel in VR
