using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckResult : MonoBehaviour
{
    // Start is called before the first frame update
    public ChooseTask controller;
    
    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter(Collider collider)
    {
        // Check if the object collided with a specific collider.
        if (collider.tag == "game")
        {
            if(controller.selected == 0)
            {
                Destroy(collider.gameObject);
                GetComponent<MeshRenderer>().material.color = Color.green;
                controller.stop = true;
                controller.DisplayTime();
            } else if (controller.selected == 1)
            {
                if(GetComponent<MeshRenderer>().material.color == collider.gameObject.GetComponent<MeshRenderer>().material.color)
                {
                    Destroy(collider.gameObject);
                    GetComponent<MeshRenderer>().material.color = Color.green;
                    controller.stop = true;
                    controller.DisplayTime();
                } else
                {
                    GetComponent<MeshRenderer>().material.color = Color.black;
                    Destroy(collider.gameObject);
                }
                
            }

        }
    }
}
