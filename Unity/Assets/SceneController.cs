using System.Collections.Generic;
using UnityEngine;

public class SceneController : MonoBehaviour
{
    private Dictionary<GameObject, GameObject> parentLookup;
    private Dictionary<GameObject, List<GameObject>> childLookup;

    private Dictionary<GameObject, List<Component>> componentLookup;
    private Dictionary<Component, List<string>> variableLookup;

    public static SceneController Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        parentLookup = new Dictionary<GameObject, GameObject>();
        childLookup = new Dictionary<GameObject, List<GameObject>>();

        componentLookup = new Dictionary<GameObject, List<Component>>();
        variableLookup = new Dictionary<Component, List<string>>();
    }

    public void RegisterGameObject(GameObject obj)
    {
        // Store parent
        parentLookup[obj] = obj.transform.parent.gameObject;

        // Store children
        childLookup[obj] = new List<GameObject>();
        foreach (Transform child in obj.transform)
        {
            childLookup[obj].Add(child.gameObject);
        }

        // Store components
        componentLookup[obj] = new List<Component>(obj.GetComponents<Component>());

        // Store public variables
        foreach (Component component in componentLookup[obj])
        {
            variableLookup[component] = new List<string>();
            var fields = component.GetType().GetFields();
            foreach (var field in fields)
            {
                if (field.IsPublic)
                {
                    variableLookup[component].Add(field.Name);
                }
            }
        }
    }

    public GameObject GetParent(GameObject obj)
    {
        return parentLookup[obj];
    }

    public List<GameObject> GetChildren(GameObject obj)
    {
        return childLookup[obj];
    }

    public List<Component> GetComponents(GameObject obj)
    {
        return componentLookup[obj];
    }

    public List<string> GetVariables(Component component)
    {
        return variableLookup[component];
    }

    public void SetParent(GameObject obj, GameObject newParent)
    {
        // Update parent lookup
        parentLookup[obj] = newParent;

        // Update child lookup on old and new parents
        childLookup[parentLookup[obj]].Remove(obj);
        childLookup[newParent].Add(obj);

        // Actually change parent in scene
        obj.transform.SetParent(newParent.transform);
    }

    public void SetVariable(Component component, string variable, object value)
    {
        var field = component.GetType().GetField(variable);
        field.SetValue(component, value);
    }
}