// DreamCodeVR+ — run validated, server-compiled C# on a Quest 3.
//
// WHY THIS EXISTS
// IL2CPP compiles ahead of time and ships no C# compiler, so loading freshly emitted IL
// cannot work on the headset — which is why Mode A was previously limited to the Editor
// and to 32-bit Mono sideloads on Quest 1/2. An INTERPRETER, however, is ordinary managed
// code and runs perfectly well under AOT. So the compile moves off the device:
//
//     speech -> LLM -> C# -> Rust guardrail -> Mac compiles to IL -> NID-94 -> interpreted here
//
// The security order is unchanged and this is the point worth being precise about: the
// source is validated by the lexical guardrail and the semantic analyzer BEFORE anything
// is compiled, and the backend refuses to emit an assembly it could not compile from
// approved source. Compilation is a delivery mechanism, never an approval. Nothing about
// moving the compiler off-device weakens or strengthens the guardrail — it only makes the
// validated result reachable on hardware that cannot compile.
//
// HONEST SCOPE. This path executes arbitrary validated code, so its safety rests entirely
// on the guardrail catching what it is shown — the "attack detected" posture, not the
// "attack unrepresentable" one. Mode B (bounded action plans) remains the safe-by-
// construction path and stays the default; this is the Mode-A arm that Mode B is measured
// against, and it is inert unless the backend chooses to send an assembly at all.

using System;
using System.Collections.Generic;
using UnityEngine;
using ILRuntime.CLR.TypeSystem;
using ILRuntime.Runtime.Intepreter;
using ILAppDomain = ILRuntime.Runtime.Enviorment.AppDomain;

namespace DreamCodeVRPlus
{
    public sealed class DcvrHotAssembly : MonoBehaviour
    {
        private static DcvrHotAssembly _instance;

        private ILAppDomain _domain;
        private readonly List<DcvrMonoBehaviourAdapter.Adaptor> _live =
            new List<DcvrMonoBehaviourAdapter.Adaptor>();

        // Assembly streams kept open for as long as the domain that reads from them.
        // A generation is a couple of kilobytes; the whole session's worth is negligible
        // next to a single texture.
        private readonly List<System.IO.MemoryStream> _held =
            new List<System.IO.MemoryStream>();

        /// <summary>Cap on simultaneously running generated scripts. Not a security
        /// control — the guardrail is — but a generated script is unbudgeted work on a
        /// 72 Hz frame target, and an accumulating pile of them degrades the demo long
        /// before it does anything interesting. Oldest is retired to make room.</summary>
        private const int MaxLiveScripts = 8;

        public static DcvrHotAssembly Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("DCVR_HotAssembly");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<DcvrHotAssembly>();
                }
                return _instance;
            }
        }

        /// <summary>Load a base64 assembly and run its first MonoBehaviour against
        /// <paramref name="target"/>.
        ///
        /// Returns false with a reason rather than throwing. Every failure here is a
        /// failure of OUR pipeline, not an attack — the code was already approved — so the
        /// right behaviour is that nothing happens and the reason reaches the HUD, never a
        /// crash in the middle of a demonstration.</summary>
        public bool LoadAndRun(string base64, GameObject target, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(base64)) { error = "empty assembly"; return false; }

            byte[] il;
            try
            {
                il = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                error = "assembly was not valid base64";
                return false;
            }

            // Cheap sanity check before handing bytes to the loader: every .NET assembly is
            // a PE image and starts "MZ".
            if (il.Length < 2 || il[0] != (byte)'M' || il[1] != (byte)'Z')
            {
                error = "not a PE image";
                return false;
            }

            try
            {
                ILAppDomain domain = EnsureDomain();

                // The stream MUST outlive LoadAssembly. Cecil reads method bodies lazily —
                // nothing is decoded until a method is first called — so disposing the
                // stream after loading appears to work and then throws ObjectDisposedException
                // at the moment the generated script first runs. Held for the domain's life.
                var ms = new System.IO.MemoryStream(il);
                _held.Add(ms);
                domain.LoadAssembly(ms);

                ILType found = FirstBehaviour(domain);
                if (found == null)
                {
                    error = "no MonoBehaviour in the assembly";
                    return false;
                }

                return Attach(domain, found, target, out error);
            }
            catch (Exception e)
            {
                error = "load failed: " + e.Message;
                return false;
            }
        }

        /// <summary>One domain for the session, created lazily.
        ///
        /// A domain per assembly would be tidier, but the adaptor registration and the
        /// interpreter's type cache are per-domain, so rebuilding it per command would
        /// discard that work on every utterance and leak the previous domain while its
        /// scripts were still running under it.</summary>
        private ILAppDomain EnsureDomain()
        {
            if (_domain != null) { return _domain; }
            _domain = new ILAppDomain();
            _domain.RegisterCrossBindingAdaptor(new DcvrMonoBehaviourAdapter());
            _domain.DelegateManager.RegisterMethodDelegate<float>();
            return _domain;
        }

        /// <summary>The ENTRY POINT of the newest assembly.
        ///
        /// Choosing "the newest MonoBehaviour" is wrong as soon as a generated program
        /// declares a helper. A solar system typically contains `GeneratedBehaviour` plus
        /// a small `PlanetOrbit` component that gets attached to each planet — and
        /// `PlanetOrbit` is declared last, so the naive rule ran the helper on the group
        /// root and the entry point never executed. On device that looked like a
        /// successful load that built nothing, which is the least debuggable failure this
        /// path can produce.
        ///
        /// So the rule is, in order: the class the generation contract names
        /// (`GeneratedBehaviour`), then any TOP-LEVEL type (nested types have `/` in their
        /// full name and are helpers by construction), then whatever is newest.</summary>
        private static ILType FirstBehaviour(ILAppDomain domain)
        {
            // Snapshot first. Asking an ILType for its BaseType makes the interpreter
            // resolve that type lazily and register it, which mutates LoadedTypes — so
            // inspecting the collection while enumerating it throws.
            var snapshot = new List<IType>(domain.LoadedTypes.Values);

            ILType named = null;
            ILType topLevel = null;
            ILType newest = null;

            foreach (IType t in snapshot)
            {
                if (!(t is ILType ilt) || !InheritsMonoBehaviour(ilt)) { continue; }
                newest = ilt;

                string full = ilt.FullName ?? "";
                bool nested = full.Contains("/") || full.Contains("+");
                if (!nested) { topLevel = ilt; }

                string leaf = full;
                int slash = leaf.LastIndexOfAny(new[] { '/', '+', '.' });
                if (slash >= 0) { leaf = leaf.Substring(slash + 1); }
                if (!nested && leaf == "GeneratedBehaviour") { named = ilt; }
            }

            return named ?? topLevel ?? newest;
        }

        private static bool InheritsMonoBehaviour(ILType t)
        {
            IType cur = t.BaseType;
            // Bounded: a malformed or cyclic base chain must not hang the frame.
            for (int depth = 0; cur != null && depth < 8; depth++)
            {
                if (cur.FullName == "UnityEngine.MonoBehaviour") { return true; }
                cur = cur.BaseType;
            }
            return false;
        }

        /// <summary>Attach the interpreted type through the cross-binding adaptor.
        ///
        /// This is the ILRuntime instantiation order and it is order-sensitive: build the
        /// instance WITHOUT its CLR side (`false`), add the real adaptor component, then
        /// point the two at each other. Letting ILTypeInstance create the CLR instance
        /// itself would produce an adaptor that Unity never attached, so `gameObject`
        /// inside the script would be null.</summary>
        private bool Attach(ILAppDomain domain, ILType type, GameObject target, out string error)
        {
            error = null;
            try
            {
                GameObject host = target != null ? target : gameObject;

                var adaptor = host.AddComponent<DcvrMonoBehaviourAdapter.Adaptor>();
                var ilInstance = new ILTypeInstance(type, false);
                adaptor.AppDomain = domain;
                adaptor.ILInstance = ilInstance;
                ilInstance.CLRInstance = adaptor;

                TrimTo(MaxLiveScripts - 1);
                _live.Add(adaptor);

                Debug.Log($"[DcvrHotAssembly] running {type.FullName} on '{host.name}' (interpreted)");
                return true;
            }
            catch (Exception e)
            {
                error = "instantiate failed: " + e.Message;
                return false;
            }
        }

        private void TrimTo(int keep)
        {
            while (_live.Count > keep && _live.Count > 0)
            {
                DcvrMonoBehaviourAdapter.Adaptor a = _live[0];
                _live.RemoveAt(0);
                if (a == null) { continue; }
                // Retire BEFORE Destroy. Unity defers component destruction to the end of
                // the frame, so an un-retired adaptor would still tick once more.
                a.Retire();
                Destroy(a);
            }
        }

        /// <summary>Stop every generated script. Wired into the deterministic full-clear so
        /// "remove everything" also means the behaviour stops, not just that the geometry
        /// it spawned disappears — a clear that leaves an invisible script still rotating a
        /// destroyed transform is the kind of thing that only shows up during a viva.</summary>
        public void ClearAll()
        {
            TrimTo(0);
        }

        public int LiveScriptCount => _live.Count;
    }
}
