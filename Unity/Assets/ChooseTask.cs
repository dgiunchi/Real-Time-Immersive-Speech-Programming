using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ChooseTask : MonoBehaviour
{
    public GameObject cube;
    public GameObject cubeT;
    public GameObject sphereT;
    public GameObject targetArea;
    public GameObject menu;

    public GameObject title;
    public GameObject training;
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
        training.SetActive(false);
        user.SetActive(false);
        current.SetActive(false);
        title.GetComponent<UnityEngine.UI.Text>().text = "Time: " + time.ToString("F2");
    }

    public void Select(int task)
    {
        selected = task;
        if (task == 0) 
        {
            cubeT.SetActive(false);
            sphereT.SetActive(false);
            cube.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().material = cube.GetComponent<MeshRenderer>().material;
        }
        else if (task == 1) 
        {
            cubeT.SetActive(false);
            sphereT.SetActive(false);
            cube.GetComponent<MeshRenderer>().enabled = true;
            targetArea.GetComponent<MeshRenderer>().enabled = true;
        }
        else if (task == 2) 
        {
            cubeT.SetActive(false);
            sphereT.SetActive(false);
            cube.SetActive(false);
        }
        else if (task == 3)
        {
            cube.SetActive(false);
            cubeT.GetComponent<MeshRenderer>().enabled = true;
            sphereT.GetComponent<MeshRenderer>().enabled = true;
        }
        menu.SetActive(false);
    }
}
