using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ChooseTask : MonoBehaviour
{
    public GameObject cube;
    public GameObject targetArea;
    public GameObject menu;

    public GameObject title;
    public GameObject user;
    public GameObject current;

    public int selected = -1;

    public float time = 0;
    public bool stop = false;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(selected != -1 && stop == false)
        {
            time += Time.deltaTime;
        }
    }

    public void DisplayTime() 
    {
        menu.SetActive(true);
        user.SetActive(false);
        current.SetActive(false);
        title.GetComponent<UnityEngine.UI.Text>().text = "Time: " + time.ToString("F2");
    }

    public void Select(int task)
    {
        selected = task;
        if (task == 0) 
        {
            cube.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().material = cube.GetComponent<MeshRenderer>().material;
        }
        else if (task == 1) 
        {
            cube.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().enabled = true;
        }
        else if (task == 2) 
        { 
        
        }
        menu.SetActive(false);
    }
}