using AgenticCache;
using Ubiq.XR;
using UnityEngine;

public class TestRoslyn : AgenticRuntimeCompiler
{
    public HandController handController;
    public Canvas canvas;
    public GameObject connectionPanel;
    public GameObject text;
    private bool codevis;

    void Start()
    {
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

    protected override void OnSourceAttached(string source)
    {
        if (text != null)
        {
            var label = text.GetComponent<UnityEngine.UI.Text>();
            if (label != null) label.text = source;
        }
    }
}
