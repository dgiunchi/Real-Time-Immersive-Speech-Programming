using System.Collections.Generic;
using Ubiq.Networking;
using UnityEngine;

public class SceneController : MonoBehaviour
{
    private static SceneController instance;

    // Dictionary to store object connections
    private Dictionary<GameObject, List<GameObject>> spatialConnections;
    private Dictionary<GameObject, List<Component>> linkedConnections;

    public static SceneController Instance
    {
        get
        {
            if (instance == null)
            {
                // Search for an existing instance in the scene
                instance = FindObjectOfType<SceneController>();

                if (instance == null)
                {
                    // Create a new instance if one doesn't exist
                    GameObject go = new GameObject("SceneController");
                    instance = go.AddComponent<SceneController>();
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        // Ensure there is only one instance in the scene
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        // Initialize dictionaries
        spatialConnections = new Dictionary<GameObject, List<GameObject>>();
        linkedConnections = new Dictionary<GameObject, List<Component>>();
        instance = this;
        DontDestroyOnLoad(this.gameObject);

        // Populate the dictionaries with connections
        PopulateConnectionData();
    }

    // Add spatial connections to the dictionary
    private void PopulateConnectionData()
    {
        // Clear existing dictionaries
        spatialConnections.Clear();
        linkedConnections.Clear();

        // Iterate through all objects in the scene
        GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            // Create a list to store spatial connections
            List<GameObject> spatialList = new List<GameObject>();

            // Add logic here to determine spatial connections based on your criteria.
            // For example, you might use distance checks or other criteria to determine
            // which objects are spatially connected to 'obj'.

            // Add the spatial connections to the dictionary
            spatialConnections[obj] = spatialList;

            // Create a list to store linked connections (components)
            List<Component> linkedList = new List<Component>();

            // Add logic here to determine linked connections based on your criteria.
            // For example, you might check for specific components attached to 'obj'.

            // Add the linked connections to the dictionary
            linkedConnections[obj] = linkedList;
        }
    }

    // Example function to retrieve spatial connections for a specific object
    public List<GameObject> GetSpatialConnections(GameObject obj)
    {
        if (spatialConnections.ContainsKey(obj))
        {
            return spatialConnections[obj];
        }
        else
        {
            Debug.LogWarning("Object not found in spatial connections dictionary.");
            return null;
        }
    }

    // Example function to retrieve linked connections (components) for a specific object
    public List<Component> GetLinkedConnections(GameObject obj)
    {
        if (linkedConnections.ContainsKey(obj))
        {
            return linkedConnections[obj];
        }
        else
        {
            Debug.LogWarning("Object not found in linked connections dictionary.");
            return null;
        }
    }

    // Example function to change a float variable in a component
    public void ChangeFloatVariable(GameObject obj, string componentName, string variableName, float newValue)
    {
        if (linkedConnections.ContainsKey(obj))
        {
            // Find the component by name
            Component[] components = linkedConnections[obj].ToArray();
            foreach (var component in components)
            {
                if (component.GetType().Name == componentName)
                {
                    // Use reflection to set the variable value
                    var field = component.GetType().GetField(variableName);
                    if (field != null && field.FieldType == typeof(float))
                    {
                        field.SetValue(component, newValue);
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("Object not found in linked connections dictionary.");
        }
    }

    // Example function to change the reference to another object in a component
    public void ChangeObjectReference(GameObject obj, string componentName, string variableName, GameObject newObject)
    {
        if (linkedConnections.ContainsKey(obj))
        {
            // Find the component by name
            Component[] components = linkedConnections[obj].ToArray();
            foreach (var component in components)
            {
                if (component.GetType().Name == componentName)
                {
                    // Use reflection to set the variable value
                    var field = component.GetType().GetField(variableName);
                    if (field != null && field.FieldType == typeof(GameObject))
                    {
                        field.SetValue(component, newObject);
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("Object not found in linked connections dictionary.");
        }
    }
}