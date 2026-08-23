# DreamCodeVR+ (Secure Architecture)

![DreamCodeVR+ Banner](docs/images/hero_banner.png)

**DreamCodeVR+** is a secure, prompt-to-scene architecture for in-headset creative programming. 

When users speak or type creative commands in VR, the backend translates them into actions within the virtual world. Because generated code from an LLM can be malicious, this project implements a rigorous, layered security boundary to keep headsets safe from arbitrary code execution.

This repository contains the complete, unified **Rust backend**, **security benchmark**, and **Unity Quest 3 client** developed to solve the security flaws of the original DreamCodeVR system.

---

## 🔒 The Two Modes

The system has been completely refactored into two execution profiles that form the core comparison of this project:

### 🔴 Mode A: The Baseline (Original DreamCodeVR)
The baseline generates raw C# directly from the user's prompt and sends it to the headset for runtime compilation. It has no security guardrails and blindly trusts the LLM. It is maintained strictly as the vulnerable comparison baseline.

### 🟢 Mode B: Secure DreamCodeVR+ (The Default)
The secure, unified architecture. It completely drops the reliance on runtime C# compilation where possible, favouring a safe-by-construction approach.
1. **Action Plans**: The system first attempts to generate a bounded JSON `ActionPlan`. The plan restricts operations to 6 known-safe verbs (e.g. `set_color`, `move`, `scale`) and is bounded by strict numeric limits before execution.
2. **C# Fallback**: If a creative command cannot be expressed as a bounded action plan, the LLM falls back to generating C#. 
3. **The Guardrail**: Any generated C# must pass a rigorous, multi-layered security gauntlet:
   - **Lexical Scan**: Checked against a Tree-sitter powered denylist, blocking `System.IO`, reflection, dynamic dispatch, and XR boundary evasion.
   - **Roslyn Semantic Check**: A .NET Roslyn analyzer formally verifies the syntax tree.
   - **Docker Sandbox**: The code is evaluated in an isolated Linux container before approval.

If the fallback C# violates any layer of the guardrail, it is safely rejected and the attack is neutralized.

---

## 🏗️ Architecture

![Guardrail Concept](docs/images/guardrail_concept.png)


```mermaid
flowchart TD
    User([VR User]) -->|Voice/Text Command| Router(Command Router)
    
    subgraph Backend [Rust Backend]
        Router -->|Parse| ModeDecision{Mode?}
        
        %% Mode A Path
        ModeDecision -->|Mode A| LLM_A[LLM: Generate C#]
        LLM_A -->|Raw C#| UnityA[Unity: Runtime Compile]
        
        %% Mode B Path
        ModeDecision -->|Mode B| LLM_B[LLM: Generate Action Plan]
        LLM_B -->|Plan JSON| ValidatePlan{Valid Plan?}
        
        ValidatePlan -->|Yes| Executor[Unity: Action Executor]
        ValidatePlan -->|No| LLM_C[LLM: Fallback to C#]
        
        LLM_C -->|Generated C#| Guardrail[Security Guardrail]
        
        subgraph Guardrail System
            Guardrail --> Lexical[Tree-Sitter Lexical Scan]
            Lexical -->|Pass| Semantic[Roslyn Semantic Analysis]
            Semantic -->|Pass| Sandbox[Docker Sandbox]
        end
        
        Sandbox -->|Approved C#| UnityB[Unity: Compile & Execute]
        
        Lexical -.->|Fail| Blocked([Attack Neutralized])
        Semantic -.->|Fail| Blocked
        Sandbox -.->|Fail| Blocked
    end

    classDef danger fill:#fee,stroke:#f66,stroke-width:2px,color:#900
    classDef safe fill:#efe,stroke:#6f6,stroke-width:2px,color:#090
    classDef neutral fill:#f4f4f4,stroke:#333,stroke-width:2px
    
    class UnityA danger
    class Executor,UnityB safe
    class Blocked neutral
```


Everything lives in one Cargo workspace, heavily optimized for safety and performance:

- **Single Binary**: The entire system—Switchboard, STT, LLM Pipeline, Guardrail, and Ubiq RoomServer—compiles down to a single Rust binary. No Node.js required.
- **Per-Peer Routing**: Connections from multiple headsets are processed concurrently. A slow LLM call from one user no longer blocks another user's pipeline.
- **Transport Security**: Opt-in signed message envelopes (HMAC / Ed25519) with replay protection.

## 📊 Security Results

The embedded benchmark pushes 40 hand-written red-team attacks across 5 threat classes, plus 12 benign complex commands, directly through the unified guardrail.

| Profile            | Attacks Blocked | Benign Preserved |
| ------------------ | --------------- | ---------------- |
| Mode A (Baseline)  | 0 / 40 (0%)     | 12 / 12 (100%)   |
| Mode B (Secure)    | **38 / 40 (95%)** | **12 / 12 (100%)** |

To run the deterministic benchmark suite locally:
```bash
cargo run -p xr-security-eval --bin xr-security-eval
```

---

## 🚀 Quick Start

### Requirements
- Rust 1.96 (pinned via `rust-toolchain.toml`)
- Bash
- Optional: `OPENAI_API_KEY` for live LLM tests (offline mocks are used by default)

### 1. Build and Test
```bash
cargo build --workspace
cargo test --workspace
```

### 2. Verify the Whole System
One command checks every feature end-to-end: the quality gate, the pipeline, the admin panel, the guardrails, the red-team suite, and the legacy STT fallbacks.
```bash
bash scripts/verify-all.sh
```

### 3. Launching

The `run.sh` script automates the full launch sequence for the various targets:

```bash
./run.sh console   # Security benchmark in the browser, no headset required
./run.sh local     # Full pipeline locally, admin panel on :7878
./run.sh demo      # Starts the backend, RoomServer, and Quest 3 status board
./run.sh quest     # Full pipeline serving a real Meta Quest over WiFi
./run.sh stop      # Kills background tasks
```

## 🥽 On a Meta Quest 3

![VR Environment](docs/images/vr_environment.png)

DreamCodeVR+ runs on a physical Quest 3. Because Quest uses IL2CPP (Ahead-of-Time compilation) and cannot compile C# at runtime, the original DreamCodeVR (Mode A) would inherently fail on modern hardware. 

**Mode B (Secure)** natively supports the Quest 3 because it relies on the bounded JSON Action Plans for standard operations, entirely bypassing the need for runtime compilation on the headset.

For build and deploy instructions to the Quest, see [`QUEST_DEMO.md`](QUEST_DEMO.md).

## 📁 Repository Layout

```
crates/       Rust workspace components:
                roomserver         Embedded Ubiq-compatible RoomServer
                csharp-policy       C# lexical and semantic guardrail
                code-policy         Bounded action-plan validator
                command-router      Pipeline and orchestration
                protocol            Wire codec and message envelopes
apps/         dreamcodevr-server (Backend) and xr-security-eval (Benchmark)
unity/        Unity client drop-in scripts
unity-quest/  The Unity 6 Quest 3 project (scene, shaders, XR rig)
scripts/      Launch, verify, and evaluation helpers
```

## License

Apache-2.0. See `LICENSE` and `NOTICE`. Derived from UCL's DreamCodeVR and Ubiq-Genie.
