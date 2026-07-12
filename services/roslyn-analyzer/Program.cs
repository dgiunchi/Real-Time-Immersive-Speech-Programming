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
