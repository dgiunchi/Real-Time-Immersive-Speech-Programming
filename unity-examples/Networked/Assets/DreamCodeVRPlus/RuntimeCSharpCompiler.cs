using System;
using UnityEngine;
#if DCVR_ROSLYN_ENABLED
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
#endif

namespace DreamCodeVRPlus
{
    /// <summary>
    /// Mode A — runtime C# compilation in Unity (the ORIGINAL DreamCodeVR approach, evolved).
    ///
    /// Compiles a C# MonoBehaviour string (received from the Rust backend on NID 94 as
    /// <c>{"type":"code","data":"&lt;C#&gt;"}</c> when the backend runs with
    /// <c>DCVR_MODE_A=true</c>) into a live in-memory assembly and attaches the resulting
    /// MonoBehaviour to the target GameObject — exactly like the original
    /// <c>CodeGenerationManager</c> + <c>TestRoslyn</c>, EXCEPT the code was already
    /// safety-validated by the fail-closed Rust gate before it ever reached Unity. That is
    /// the DreamCodeVR+ contribution: the original runtime-compile path, made safe.
    ///
    /// This works in the Unity 6 EDITOR (and any Mono/Mono-AOT player) because the Editor
    /// JIT-compiles at runtime. It requires the Roslyn DLLs (Microsoft.CodeAnalysis*) under
    /// Assets/Plugins AND the scripting define <c>DCVR_ROSLYN_ENABLED</c>. Until those are
    /// added this is an inert stub, so the project ALWAYS compiles (wire first, debug later).
    /// Setup: docs/UNITY_INTEGRATION.md
    /// </summary>
    public static class RuntimeCSharpCompiler
    {
        /// <summary>True only when built with the Roslyn DLLs + DCVR_ROSLYN_ENABLED define.</summary>
        public static bool Available
        {
#if DCVR_ROSLYN_ENABLED
            get { return true; }
#else
            get { return false; }
#endif
        }

        /// <summary>
        /// Compile <paramref name="source"/> and attach its first concrete MonoBehaviour to
        /// <paramref name="target"/>. Returns true on success; on failure sets
        /// <paramref name="error"/> to the compiler diagnostics (so the demo can show them).
        /// </summary>
        public static bool CompileAndAttach(string source, GameObject target, out string error)
        {
#if DCVR_ROSLYN_ENABLED
            error = null;
            if (string.IsNullOrWhiteSpace(source)) { error = "empty C# source"; return false; }
            if (target == null) { error = "no target GameObject"; return false; }
            try
            {
                var tree = CSharpSyntaxTree.ParseText(source);

                // Reference every already-loaded assembly that has a file location — in the
                // Editor that includes UnityEngine, the .NET BCL, etc. (no manual ref list).
                var refs = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a =>
                    {
                        try { return MetadataReference.CreateFromFile(a.Location); }
                        catch { return null; }
                    })
                    .Where(r => r != null)
                    .Cast<MetadataReference>()
                    .ToArray();

                var options = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release);

                var compilation = CSharpCompilation.Create(
                    "DCVRGen_" + Guid.NewGuid().ToString("N"),
                    new[] { tree }, refs, options);

                using (var ms = new MemoryStream())
                {
                    var result = compilation.Emit(ms);
                    if (!result.Success)
                    {
                        var errs = result.Diagnostics
                            .Where(d => d.Severity == DiagnosticSeverity.Error)
                            .Select(d => d.ToString());
                        error = "compile error:\n" + string.Join("\n", errs);
                        return false;
                    }
                    ms.Seek(0, SeekOrigin.Begin);
                    var asm = Assembly.Load(ms.ToArray());
                    var mbType = asm.GetTypes()
                        .FirstOrDefault(t => typeof(MonoBehaviour).IsAssignableFrom(t) && !t.IsAbstract);
                    if (mbType == null)
                    {
                        error = "generated code has no concrete MonoBehaviour class";
                        return false;
                    }
                    target.AddComponent(mbType); // its Start()/Update() now run on the target
                    return true;
                }
            }
            catch (Exception e)
            {
                error = "runtime compile exception: " + e.Message;
                return false;
            }
#else
            _ = source;
            _ = target;
            error = "Roslyn runtime compiler not enabled. Import the Microsoft.CodeAnalysis DLLs "
                  + "into Assets/Plugins and add the scripting define DCVR_ROSLYN_ENABLED "
                  + "(see docs/UNITY_INTEGRATION.md).";
            return false;
#endif
        }
    }
}
