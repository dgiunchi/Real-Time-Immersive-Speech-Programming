import re

diagram = """
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
"""

with open("README.md", "r") as f:
    text = f.read()

# Insert after Architecture header
text = text.replace("## 🏗️ Architecture\n", "## 🏗️ Architecture\n\n" + diagram + "\n")

with open("README.md", "w") as f:
    f.write(text)
