using RoslynCSharp;
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AgenticCache
{
    [DisallowMultipleComponent]
    public class AgenticRuntimeCompiler : MonoBehaviour
    {
        public AssemblyReferenceAsset[] assemblyReferences;

        private ScriptDomain domain;
        private string cSharpSource;
        private static readonly Regex CsharpScriptRegex = new Regex(
            @"```(?:csharp)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public static string Extract(string text)
        {
            var match = CsharpScriptRegex.Match(text ?? string.Empty);
            return match.Success ? match.Groups[1].Value : text;
        }

        public void SetCodeString(string code) => cSharpSource = code;

        public void RunCode(GameObject target)
        {
            if (!TryCompileAndAttach(target, cSharpSource, out _, out var error)) throw new Exception(error);
        }

        public bool TryCompileAndAttach(GameObject target, string source, out ScriptProxy proxy, out string error)
        {
            proxy = null;
            error = null;
            if (target == null) { error = "Target object no longer exists."; return false; }
            source = Extract(source);
            if (string.IsNullOrWhiteSpace(source)) { error = "The artifact did not contain C# source."; return false; }
            if (!PassesCapabilityPolicy(source, out error)) return false;
            try
            {
                EnsureDomain();
                var type = domain.CompileAndLoadMainSource(source, ScriptSecurityMode.UseSettings, assemblyReferences);
                if (type == null)
                {
                    error = !domain.RoslynCompilerService.LastCompileResult.Success
                        ? "Roslyn compilation failed. Check the Unity console for compiler diagnostics."
                        : !domain.SecurityResult.IsSecurityVerified
                            ? "The generated artifact failed Roslyn security verification."
                            : "The source must define one MonoBehaviour class.";
                    return false;
                }
                if (!type.IsMonoBehaviour) { error = "The generated class must inherit MonoBehaviour."; return false; }
                proxy = type.CreateInstance(target);
                if (proxy == null || proxy.MonoBehaviourInstance == null)
                { error = "Roslyn created no MonoBehaviour instance."; return false; }
                cSharpSource = source;
                OnSourceAttached(source);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                if (proxy != null) proxy.Dispose();
                proxy = null;
                return false;
            }
        }

        protected virtual void OnSourceAttached(string source) { }

        protected void EnsureDomain()
        {
            if (domain != null) return;
            domain = ScriptDomain.CreateDomain("AgenticXRRuntime", true);
            if (assemblyReferences != null)
                foreach (var reference in assemblyReferences)
                    if (reference != null) domain.RoslynCompilerService.ReferenceAssemblies.Add(reference);
            domain.InitializeCompilerService();
        }

        private static bool PassesCapabilityPolicy(string source, out string error)
        {
            var withoutComments = Regex.Replace(source, @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
            var compact = Regex.Replace(withoutComments, @"\s+", string.Empty).ToLowerInvariant();
            string[] denied = {
                "system.io", "system.net", "system.diagnostics", "system.reflection",
                "system.runtime.interopservices", "unityengine.networking", "dllimport",
                "unsafe", "stackalloc", "process.", "activator.", "assembly.", "type.gettype",
                "application.quit", "application.openurl", "environment.exit", "gc.collect"
            };
            foreach (var token in denied)
            {
                if (!compact.Contains(token)) continue;
                error = "Capability policy rejected token: " + token;
                return false;
            }
            string[] allowedNamespaces = { "UnityEngine", "System", "System.Collections", "System.Collections.Generic" };
            foreach (Match match in Regex.Matches(withoutComments,
                @"\busing\s+(?:static\s+)?([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;"))
            {
                var requested = match.Groups[1].Value;
                var allowed = false;
                foreach (var candidate in allowedNamespaces)
                    if (requested == candidate || requested.StartsWith(candidate + ".", StringComparison.Ordinal))
                    { allowed = true; break; }
                if (allowed) continue;
                error = "Capability policy rejected namespace: " + requested;
                return false;
            }
            error = null;
            return true;
        }
    }
}
