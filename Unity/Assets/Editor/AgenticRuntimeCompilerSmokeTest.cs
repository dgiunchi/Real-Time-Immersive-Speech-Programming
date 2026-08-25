using System;
using System.Collections.Generic;
using System.Linq;
using RoslynCSharp;
using AgenticCache;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Executes the runtime compile-and-attach path that the rest of the project only
/// ever compiled. Roslyn needs a JIT, so this runs in the Editor, where Mono
/// provides one; it is not evidence about a standalone IL2CPP build.
///
/// Written as a static entry point rather than an NUnit test because AgenticCache
/// has no assembly definition and therefore lives in Assembly-CSharp, which a test
/// assembly cannot reference. This matches the existing BuildStudyScene pattern and
/// runs the same way in batch mode.
///
///   Unity -batchmode -nographics -quit -projectPath &lt;path&gt; \
///     -executeMethod AgenticRuntimeCompilerSmokeTest.Run -logFile &lt;log&gt;
///
/// Exits non-zero on any failure so a build system can gate on it.
/// </summary>
public static class AgenticRuntimeCompilerSmokeTest
{
    private const string Tag = "[AgenticRuntimeCompilerSmokeTest]";

    private static readonly List<string> Failures = new List<string>();
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (condition) Debug.Log($"{Tag} ok: {message}");
        else { Failures.Add(message); Debug.LogError($"{Tag} FAILED: {message}"); }
    }

    [MenuItem("AgenticXR/Run Runtime Compiler Smoke Test")]
    public static void Run()
    {
        Failures.Clear();
        checks = 0;
        var scratch = new List<GameObject>();

        try
        {
            var host = new GameObject("agenticxr-compiler-host");
            scratch.Add(host);
            var compiler = host.AddComponent<AgenticRuntimeCompiler>();
            Check(compiler != null, "AgenticRuntimeCompiler can be added to a GameObject");

            // Wired exactly as AgenticXRStudySceneBuilder wires it. Without these the
            // compiler has no reference to UnityEngine at all and every compile fails
            // with CS0246, which looks like a broken mechanism but is a broken setup.
            compiler.assemblyReferences = AssetDatabase
                .FindAssets("t:AssemblyReferenceAsset", new[] { "Assets/RoslynCSharp/AssemblyReferences" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AssemblyReferenceAsset>)
                .Where(asset => asset != null).ToArray();
            Check(compiler.assemblyReferences.Length > 0,
                $"assembly reference assets are available ({compiler.assemblyReferences.Length} found)");

            // 1. The core mechanism: generated C# compiled and attached at runtime.
            var target = new GameObject("agenticxr-target");
            scratch.Add(target);
            const string generated =
                "using UnityEngine;\n" +
                "public class GeneratedProbeBehaviour : MonoBehaviour\n" +
                "{\n" +
                "    public int Marker = 4242;\n" +
                "}\n";

            var attached = compiler.TryCompileAndAttach(target, generated, out var proxy, out var error);
            Check(attached, $"generated C# compiles and attaches at runtime ({error})");
            Check(proxy != null, "a script proxy is returned");
            Check(proxy != null && proxy.MonoBehaviourInstance != null, "a MonoBehaviour instance is created");

            var component = target.GetComponent("GeneratedProbeBehaviour");
            Check(component != null, "the generated behaviour is present on the target GameObject");

            // 2. Fenced code, which is how a model actually returns it.
            const string fenced = "Here you go:\n```csharp\nusing UnityEngine;\npublic class GeneratedFencedBehaviour : MonoBehaviour { }\n```\nDone.";
            Check(AgenticRuntimeCompiler.Extract(fenced).Contains("GeneratedFencedBehaviour"),
                "Extract pulls source out of a fenced model response");
            var fencedTarget = new GameObject("agenticxr-fenced-target");
            scratch.Add(fencedTarget);
            Check(compiler.TryCompileAndAttach(fencedTarget, fenced, out _, out var fencedError),
                $"a fenced model response compiles and attaches ({fencedError})");

            // 3. The capability allowlist, which the paper describes as defence in
            // depth and which had never been executed.
            foreach (var denied in new[] { "System.IO", "System.Net", "System.Reflection", "System.Diagnostics" })
            {
                var deniedTarget = new GameObject($"agenticxr-denied-{denied}");
                scratch.Add(deniedTarget);
                var source =
                    $"using UnityEngine;\nusing {denied};\npublic class GeneratedDenied : MonoBehaviour {{ }}\n";
                var blocked = !compiler.TryCompileAndAttach(deniedTarget, source, out _, out var deniedError);
                Check(blocked, $"the allowlist blocks {denied}");
                Check(blocked && !string.IsNullOrEmpty(deniedError), $"blocking {denied} reports a reason");
                Check(deniedTarget.GetComponent("GeneratedDenied") == null,
                    $"nothing is attached when {denied} is blocked");
            }

            // 4. Source that is not a MonoBehaviour is refused rather than attached.
            var plainTarget = new GameObject("agenticxr-plain-target");
            scratch.Add(plainTarget);
            Check(!compiler.TryCompileAndAttach(plainTarget, "public class NotABehaviour { }", out _, out _),
                "a class that does not inherit MonoBehaviour is refused");

            // 5. Source that does not compile is reported, not thrown.
            var brokenTarget = new GameObject("agenticxr-broken-target");
            scratch.Add(brokenTarget);
            var brokenHandled = !compiler.TryCompileAndAttach(brokenTarget,
                "using UnityEngine;\npublic class Broken : MonoBehaviour { this is not c# }", out _, out var brokenError);
            Check(brokenHandled, "source that does not compile is refused");
            Check(!string.IsNullOrEmpty(brokenError), "a compile failure reports a reason");

            // 6. Empty and missing input are handled rather than crashing.
            Check(!compiler.TryCompileAndAttach(target, "   ", out _, out _), "blank source is refused");
            Check(!compiler.TryCompileAndAttach(null, generated, out _, out _), "a null target is refused");
        }
        catch (Exception exception)
        {
            Failures.Add($"unhandled exception: {exception}");
            Debug.LogError($"{Tag} unhandled exception: {exception}");
        }
        finally
        {
            foreach (var item in scratch) if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }

        if (Failures.Count == 0)
        {
            Debug.Log($"{Tag} PASS ({checks} checks)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"{Tag} FAIL ({Failures.Count} of {checks} checks failed)");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
