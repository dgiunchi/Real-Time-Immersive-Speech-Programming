# Roslyn Semantic Analyzer (Phase 3, deep C# layer)

The deeper semantic C# check. The Rust `dcvr-csharp-policy` crate is a fast
lexical/structural pre-filter; this .NET service uses a real Roslyn `SemanticModel`
to resolve symbols and reject references that resolve into dangerous namespaces (a
**deny-list**, not a complete allow-list — see the contract below).

## Run
```bash
dotnet run --project services/roslyn-analyzer   # listens on :5099
```
Then point the backend at it: the Rust `dcvr-roslyn-client::HttpRoslynAnalyzer`
POSTs `{"csharp": "..."}` to `/analyze` and expects `{"approved":bool,"diagnostics":[...]}`.

## Contract (fail-closed)
- `approved:true` only when there are NO syntax errors and NO reference resolves into
  a **denied** namespace (`System.IO/Net/Reflection/Diagnostics/Threading/Runtime.InteropServices`).
- This is a **deny-list, not a complete allow-list**. Without the full Unity/.NET
  reference assemblies many legitimate symbols cannot be resolved, so the service
  cannot *prove* a program safe — it only catches known-dangerous namespaces.
  C# safety is therefore **defence-in-depth**: the Rust lexical scanner + this
  semantic deny-list + the plan-vs-C# consistency check + the Mode-D sandbox.
  **The Roslyn service alone is NOT a sandbox.**
- Any analyzer error => `approved:false` (fail-closed).

## Status
Source provided; not built in the Rust CI (no .NET toolchain here). The Rust side
(`dcvr-roslyn-client`) is fully built/tested with a mock by default, so the backend
runs without this service; enable it in dev/research mode for the deeper check.
