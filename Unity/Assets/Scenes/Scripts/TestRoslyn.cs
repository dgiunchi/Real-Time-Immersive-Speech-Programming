using RoslynCSharp;
using RoslynCSharp.Example;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Trivial.Mono.Cecil.Cil;
using UnityEngine;
using UnityEngine.UI;
using static System.Net.Mime.MediaTypeNames;

public class TestRoslyn : MonoBehaviour
{
    //private
    private string activeCSharpSource = null;
    private ScriptProxy activeCrawlerScript = null;
    private ScriptDomain domain = null;
    // Start is called before the first frame update


    public AssemblyReferenceAsset[] assemblyReferences;
    private string cSharpSource;
    private static readonly Regex csharpScriptRegex = new Regex(@"`csharp\n(.*?)\n`", RegexOptions.Multiline);
    // Methods
    /// <summary>
    /// Called by Unity.
    /// </summary>
    public void Awake()
    {

    }

    void Start()
    {
        // Create the domain
        domain = ScriptDomain.CreateDomain("myDom", true);

        // Add assembly references
        foreach (AssemblyReferenceAsset reference in assemblyReferences)
            domain.RoslynCompilerService.ReferenceAssemblies.Add(reference);

    }

    // Update is called once per frame
    void Update()
    {

    }

    public static string Extract(string text)
    {
        Match match = csharpScriptRegex.Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    public void SetCodeString(string code)
    {
        cSharpSource = code;
    }

    

    public void RunCode(GameObject gameObjectTarget)
    {
        // Get the C# code from the input field
        //cSharpSource = "using UnityEngine;\r\n\r\npublic class ColorObject : MonoBehaviour\r\n{\r\n    private void Start()\r\n    {\r\n        // Create a new material with the desired color\r\n        Material material = new Material(Shader.Find(\"Standard\"));\r\n        material.color = Color.red;\r\n\r\n        // Assign the new material to the object\'s Renderer component\r\n        Renderer renderer = GetComponent<Renderer>();\r\n        if (renderer != null)\r\n        {\r\n            renderer.material = material;\r\n        }\r\n    }\r\n}\n\n";

        // Dont recompile the same code
        if (activeCSharpSource != cSharpSource)
        {
            //try
            {
                // Compile code
                ScriptType type = domain.CompileAndLoadMainSource(cSharpSource, ScriptSecurityMode.UseSettings, assemblyReferences);
                
                // Check for null
                if (type == null)
                {
                    if (domain.RoslynCompilerService.LastCompileResult.Success == false)
                        throw new Exception("Maze crawler code contained errors. Please fix and try again");
                    else if (domain.SecurityResult.IsSecurityVerified == false)
                        throw new Exception("Maze crawler code failed code security verification");
                    else
                        throw new Exception("Maze crawler code does not define a class. You must include one class definition of any name that inherits from 'RoslynCSharp.Example.MazeCrawler'");
                }

                ScriptProxy p = type.CreateInstance(gameObjectTarget);
                if (p != null)
                {
                    Debug.Log("Created instance");
                }


            }
            //catch (Exception e)
            //{
            //    // Show the code editor window
            //    codeEditorWindow.SetActive(true);
            //    throw e;
            //}
        }
        else
        {
        }
    }

    

    
}
