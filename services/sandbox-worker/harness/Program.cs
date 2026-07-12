// DreamCodeVR+ Mode-D sandbox job harness.
// Reads an untrusted C# program from STDIN, compiles it IN-MEMORY with Roslyn,
// and executes its entry point inside this (already hardened, network-isolated,
// read-only, unprivileged, wall-clock-bounded) container. Only stdout/stderr and
// the exit code leave the container; the Rust DockerSandboxRunner turns those
// into a structured SandboxReport. The host backend never runs this code.
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string source = await Console.In.ReadToEndAsync();
if (string.IsNullOrWhiteSpace(source))
{
    Console.Error.WriteLine("harness: empty source on stdin");
    return 64;
}

// Reference the full set of runtime assemblies so ordinary C# compiles offline
// (no NuGet restore at run time — packages are baked into the image at build).
var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
    .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
    .ToList();

var tree = CSharpSyntaxTree.ParseText(source);
var compilation = CSharpCompilation.Create(
    assemblyName: "SandboxJob",
    syntaxTrees: new[] { tree },
    references: tpa,
    options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

using var peStream = new MemoryStream();
var emit = compilation.Emit(peStream);
if (!emit.Success)
{
    foreach (var d in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        Console.Error.WriteLine($"compile error: {d.GetMessage()}");
    return 65;
}

peStream.Seek(0, SeekOrigin.Begin);
var assembly = Assembly.Load(peStream.ToArray());
var entry = assembly.EntryPoint;
if (entry is null)
{
    Console.Error.WriteLine("harness: program has no entry point");
    return 66;
}

try
{
    var parameters = entry.GetParameters().Length == 1
        ? new object[] { Array.Empty<string>() }
        : null;
    var result = entry.Invoke(null, parameters);
    return result is int code ? code : 0;
}
catch (TargetInvocationException ex)
{
    Console.Error.WriteLine($"runtime exception: {ex.InnerException?.Message ?? ex.Message}");
    return 70;
}
