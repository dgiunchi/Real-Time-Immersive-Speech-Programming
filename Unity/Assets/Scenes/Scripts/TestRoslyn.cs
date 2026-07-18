using RoslynCSharp;
using System;
using System.Text.RegularExpressions;
using Ubiq.XR;
using UnityEngine;
using UnityEngine.UI;

public class TestRoslyn : MonoBehaviour
{
    //private
    private string activeCSharpSource = null;
    private ScriptDomain domain = null;
    // Start is called before the first frame update

    public HandController handController;
    
    public Canvas canvas;
    public GameObject connectionPanel;
    public GameObject text;

    public AssemblyReferenceAsset[] assemblyReferences;
    private string cSharpSource;
    private static readonly Regex csharpScriptRegex = new Regex(@"```(?:csharp)?\s*(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    bool codevis = false;
    // Methods
    /// <summary>
    /// Called by Unity.
    /// </summary>
    public void Awake()
    {

    }

    void Start()
    {
        EnsureDomain();
        if (handController != null) handController.TriggerPress.AddListener(showCodePanel);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyUp(KeyCode.V))
        {
            codevis = !codevis;
            showCodePanel(codevis);
        }
    }

    public void showCodePanel(bool show)
    {
        if (show)
        {
            canvas.gameObject.SetActive(show);
            connectionPanel.SetActive(!show);
        } else
        {
            connectionPanel.SetActive(!show);
            canvas.gameObject.SetActive(show);
        }
        
        
        
    }

    public static string Extract(string text)
    {
        Match match = csharpScriptRegex.Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return text;
    }

    public void SetCodeString(string code)
    {
        cSharpSource = code;
    }

    

    public void RunCode(GameObject gameObjectTarget)
    {
        if (!TryCompileAndAttach(gameObjectTarget, cSharpSource, out _, out var error))
            throw new Exception(error);
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
            if (proxy == null || proxy.MonoBehaviourInstance == null) { error = "Roslyn created no MonoBehaviour instance."; return false; }
            activeCSharpSource = source;
            cSharpSource = source;
            if (text != null)
            {
                var label = text.GetComponent<UnityEngine.UI.Text>();
                if (label != null) label.text = source;
            }
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

    private void EnsureDomain()
    {
        if (domain != null) return;
        domain = ScriptDomain.CreateDomain("AgenticXRRuntime", true);
        if (assemblyReferences != null)
        {
            foreach (var reference in assemblyReferences)
                if (reference != null) domain.RoslynCompilerService.ReferenceAssemblies.Add(reference);
        }
        domain.InitializeCompilerService();
    }

    private static bool PassesCapabilityPolicy(string source, out string error)
    {
        string[] denied = { "System.IO", "System.Net", "System.Diagnostics", "System.Reflection", "DllImport", "unsafe ", "Process.", "Application.Quit" };
        foreach (var token in denied)
        {
            if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "Capability policy rejected token: " + token;
                return false;
            }
        }
        error = null;
        return true;
    }

    

    
}
