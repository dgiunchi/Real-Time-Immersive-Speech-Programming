# DreamCodeVR+

![DreamCodeVR+ Banner](docs/images/hero_banner.png)

**Speak it. Generate it. Experience it safely in VR.**

DreamCodeVR+ extends the original DreamCodeVR research project with a modern Quest 3 execution pipeline and a unified, multi-layered security architecture for AI-generated VR behaviour.

A user can say:
> *"Generate me a solar system."*

The system converts the speech to text, asks an AI model to describe and create the scene, validates the result, and then builds it inside VR. 

The critical difference in DreamCodeVR+ is that generated behaviour is **NOT** blindly trusted. The AI proposes the behaviour, but rigorous validation layers and a Docker Sandbox decide what is allowed to execute.

## ✨ What is DreamCodeVR?
DreamCodeVR is prior research by Giunchi et al. ([paper](https://discovery.ucl.ac.uk/id/eprint/10190408/)). It democratised VR behaviour design by allowing users to speak their intent:
`Speech → speech-to-text → LLM → generated Unity C# behaviour → VR world changes`

However, the original system compiled and ran the generated C# directly inside the Unity process at runtime without a validation gate. While highly expressive, running AI-generated code directly inside the user's headset poses massive security and safety risks.

## 🌟 What does DreamCodeVR+ add?
DreamCodeVR+ keeps the magic of the original system but completely rebuilds how the code is validated and executed.

| Area | Original DreamCodeVR | DreamCodeVR+ |
| :--- | :--- | :--- |
| Speech authoring | ✅ | ✅ |
| LLM-generated behaviour | ✅ | ✅ |
| **Rust safety backend** | — | ✅ |
| **Bounded action-plan mode** | — | ✅ |
| **Generated C# validation** | — | ✅ |
| **Docker Sandbox Isolation** | — | ✅ |
| **XR-specific safety rules** | — | ✅ |
| **Quest 3 execution** | Sideloaded Quest 1/2 only | ✅ Native 64-bit IL2CPP |

## 🎓 Scientific Contribution
This project addresses a critical, emerging vulnerability in Generative XR: the blind execution of LLM-generated runtime code in spatially-aware headsets.

The primary contribution is a **Dual-Mode Architecture** that transitions generative behaviour from arbitrary, vulnerable runtime compilation to a safe-by-construction pipeline, backed by a 3-layer security gauntlet.

## 🎙️ The Architecture
Instead of scattering disjointed security systems, DreamCodeVR+ relies on a unified approach to handle AI generation.

### 🟢 Secure Mode (Action Plans)
The default, secure architecture. It completely drops the reliance on runtime C# compilation. The system forces the LLM to generate a bounded JSON `ActionPlan`. The plan restricts operations to exactly 6 known-safe verbs (e.g., `set_color`, `move`, `scale`). Because it relies on passive data instead of active code, it is mathematically immune to arbitrary code execution.

### 🔴 Research Mode (The C# Gauntlet)
Maintained for research and backwards compatibility. If the system generates raw C#, it is pushed through a strict 3-phase security gauntlet before execution:
1. **Tree-Sitter Lexical Scan:** Blocks obvious forbidden APIs.
2. **Roslyn Semantic Analysis:** Resolves aliases and variable disguises.
3. **Docker Sandbox:** Executes the code in an isolated container to catch infinite loops or memory crashes.

## 🧪 XR Security Evaluation
Normal computer security understands things like reading files or opening network connections. But VR introduces APIs that may be perfectly legitimate for a computer, but highly dangerous to the wearer.

We measured the system against a fixed benchmark of 40 malicious VR-specific payloads and 12 benign controls.

| Defence Level | Malicious payloads rejected |
| :--- | :--- |
| No defence | 0 / 40 |
| Conventional code security | 15 / 40 |
| **XR-aware static policy (DreamCodeVR+)** | **38 / 40** |

Our layered defence rejected **95%** of malicious payloads while admitting **100%** of tested benign controls.

### The Five Benchmark Classes & Threat Model
To rigorously evaluate the system, we constructed a deterministic benchmark of 40 adversarial vectors across 5 XR-specific threat classes:

1. **Biometric:** Attacks attempting to exfiltrate eye-tracking, facial expressions, or heart rate data.
2. **Positional:** Attacks attempting to disorient the user or induce cybersickness by violently moving the tracking origin.
3. **Surroundings:** Attacks attempting to map the user's physical room or exfiltrate passthrough camera meshes.
4. **Human Joystick:** Attacks attempting to subtly steer the user into physical walls.
5. **Guardian/Chaperone:** Attacks attempting to blind the safety boundary or draw fake boundaries.

### Why two attacks remain
The only attacks that bypassed the system were in the **Human Joystick** category. Both of these residual attacks use ordinary `Camera.main.transform` movement. A single camera movement is normal Unity code. The danger only appears when small, imperceptible movements accumulate over time to steer a user into a real-world wall. A static validator sees source code, not the full future runtime behaviour, demonstrating the honest limit of static code-scanning.

## 🥽 Running on Quest 3
![VR Environment](docs/images/vr_environment.png)
The project was evaluated both via a deterministic local benchmark (to test the backend validator) and live on a physical Meta Quest 3. 

## 📁 Repository Structure
```
DreamCodeVR+/
 ├── apps/
 │   ├── dreamcodevr-server/   # The main Rust backend server
 │   └── xr-security-eval/     # The deterministic security benchmark
 ├── crates/
 │   ├── command-router/       # Orchestrates the 2-Mode architecture
 │   ├── behaviour-dsl/        # Defines Mode B Action Plans
 │   ├── roomserver/           # Embedded Rust Ubiq server
 │   └── sandbox/              # Docker Sandbox evaluation runtime
 ├── unity-quest/              # The Meta Quest 3 Unity Client
 ├── redteam/                  # The adversarial corpus
 └── scripts/                  # CI, testing, and deployment scripts
```

## 🚀 Getting Started
**Prerequisites:** Rust 1.96+, Unity 6000.5.x.
Copy `.env.example` to `.env` and add your own `OPENAI_API_KEY`.

### macOS Easy Launch
For macOS users, the repository includes double-clickable launcher scripts:
- **`Start-DreamCodeVR.command`**: Boots the live backend.
- **`Run-Security-Benchmark.command`**: Runs the 40-attack deterministic XR security benchmark.
- **`Test-And-Verify-All.command`**: Runs the test suite and verifies pipeline integrity.

🙏 **Credits**
This project builds upon the prior work of Giunchi et al. (See the original [DreamCodeVR Paper](https://discovery.ucl.ac.uk/id/eprint/10190408/)).
