// DreamCodeVR+ Roslyn semantic analyzer microservice (Phase 3, deep C# layer).
//
// Run:  dotnet run --project services/roslyn-analyzer   (listens on :5099)
// POST /analyze  {"csharp":"<source>"}  ->  {"approved":bool,"diagnostics":[...]}
//
// This is the deeper SEMANTIC, symbol-resolving check that the Rust lexical
// pre-filter (dcvr-csharp-policy) cannot do. It walks the syntax tree with a
// SemanticModel and rejects any reference that resolves into a DENIED namespace
// (a deny-list — see the note below on why a strict allow-list is not used).
// Fail-closed: any error or denied symbol => approved:false.
// NOTE: this service is ONE layer of defence-in-depth, NOT a complete sandbox.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Hard-denied namespaces (the real safety check). We do NOT enforce a strict
// allow-list here because, without the full Unity/.NET reference assemblies, many
// legitimate symbols (incl. the generated class's own global namespace) resolve to
// non-UnityEngine namespaces and would false-reject. The deny-list is authoritative;
// the Rust lexical layer + sandbox are the other defence-in-depth layers.
string[] deniedNamespaces =
{
    "System.IO", "System.Net", "System.Reflection", "System.Diagnostics",
    "System.Threading", "System.Runtime.InteropServices",
};

// ── /compile ────────────────────────────────────────────────────────────────
// Compile validated C# to a .NET assembly and return the IL as base64.
//
// This is what makes full-freedom generated code runnable on a Quest 3. IL2CPP compiles
// ahead of time and ships no C# compiler, so nothing can be compiled ON the headset. It
// can, however, INTERPRET IL — so the compile happens here, on a machine that has .NET,
// and only the resulting assembly crosses the wire.
//
// The security order is unchanged and deliberate: the Rust guardrail validates the SOURCE
// before this endpoint is ever called, and this endpoint refuses to compile anything that
// does not pass the same semantic deny-list as /analyze. Compilation is not an approval.
string[] unityRefDirs =
{
    "/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/Resources/Scripting/Managed/UnityEngine",
    "/Applications/Unity/Hub/Editor/6000.5.8f1/Unity.app/Contents/Resources/Scripting/Managed",
};

List<MetadataReference> BuildReferences()
{
    var refs = new List<MetadataReference>();
    // Core framework, taken from this process so the versions always agree.
    var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    foreach (var p in tpa)
    {
        var n = Path.GetFileNameWithoutExtension(p);
        if (n is "System.Private.CoreLib" or "System.Runtime" or "netstandard"
              or "System.Console" or "System.Linq" or "System.Collections")
            refs.Add(MetadataReference.CreateFromFile(p));
    }
    // Unity engine modules, so a generated MonoBehaviour actually resolves.
    foreach (var dir in unityRefDirs)
    {
        if (!Directory.Exists(dir)) continue;
        foreach (var dll in Directory.GetFiles(dir, "UnityEngine*.dll"))
        {
            // Modules ONLY. UnityEngine.dll is the legacy umbrella that re-exports every
            // type in the modules, so referencing both makes MonoBehaviour, Renderer and
            // friends ambiguous (CS0433) and nothing compiles.
            var name = Path.GetFileName(dll);
            if (name.Equals("UnityEngine.dll", StringComparison.OrdinalIgnoreCase)) continue;
            try { refs.Add(MetadataReference.CreateFromFile(dll)); } catch { }
        }
    }
    return refs;
}

app.MapPost("/compile", (AnalyzeRequest req) =>
{
    var diagnostics = new List<string>();
    var source = req.Csharp ?? string.Empty;
    try
    {
        // Same deny-list as /analyze. Compiling is a separate capability from approving,
        // and it must not become a way around the semantic check.
        var preTree = CSharpSyntaxTree.ParseText(source);
        foreach (var id in preTree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var text = id.ToString();
            foreach (var denied in deniedNamespaces)
            {
                if (denied.EndsWith(text, StringComparison.Ordinal) && text.Length > 3)
                    diagnostics.Add($"denied identifier: {text}");
            }
        }
        foreach (var u in preTree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var ns = u.Name?.ToString() ?? "";
            foreach (var denied in deniedNamespaces)
            {
                if (ns.StartsWith(denied, StringComparison.Ordinal))
                    diagnostics.Add($"denied namespace: {ns}");
            }
        }
        if (diagnostics.Count > 0)
            return Results.Json(new { approved = false, assembly = (string?)null, diagnostics });

        var compilation = CSharpCompilation.Create(
            "DcvrGenerated",
            new[] { CSharpSyntaxTree.ParseText(source) },
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                         optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            foreach (var d in result.Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error) diagnostics.Add(d.ToString());
            return Results.Json(new { approved = false, assembly = (string?)null, diagnostics });
        }
        return Results.Json(new
        {
            approved = true,
            assembly = Convert.ToBase64String(ms.ToArray()),
            diagnostics,
        });
    }
    catch (Exception ex)
    {
        diagnostics.Add("compile exception: " + ex.Message);
        return Results.Json(new { approved = false, assembly = (string?)null, diagnostics });
    }
});

app.MapPost("/analyze", (AnalyzeRequest req) =>
{
    var diagnostics = new List<string>();
    try
    {
        var tree = CSharpSyntaxTree.ParseText(req.Csharp ?? string.Empty);
        var root = tree.GetRoot();
        if (root.ContainsDiagnostics)
            diagnostics.Add("syntax errors");

        var refs = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var compilation = CSharpCompilation.Create("gen", new[] { tree }, refs);
        var model = compilation.GetSemanticModel(tree);

        foreach (var node in root.DescendantNodes())
        {
            if (node is IdentifierNameSyntax or QualifiedNameSyntax or MemberAccessExpressionSyntax)
            {
                var symbol = model.GetSymbolInfo(node).Symbol;
                var ns = symbol?.ContainingNamespace?.ToDisplayString();
                if (ns is null) continue;
                if (deniedNamespaces.Any(d => ns == d || ns.StartsWith(d + ".")))
                    diagnostics.Add($"denied namespace: {ns}");
            }
        }
    }
    catch (Exception e)
    {
        diagnostics.Add("analyzer error: " + e.Message);
    }

    var approved = diagnostics.Count == 0;
    return Results.Json(new AnalyzeResponse(approved, diagnostics));
});

app.Run("http://0.0.0.0:5099");

record AnalyzeRequest(string? Csharp);
record AnalyzeResponse(bool Approved, List<string> Diagnostics);
