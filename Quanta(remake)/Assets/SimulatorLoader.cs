using UnityEngine;

/// <summary>
/// Ensures an XR Interaction Simulator instance exists in the current scene.
/// Does NOT use DontDestroyOnLoad so that each scene gets a fresh simulator
/// bound to that scene's XR Origin and Controllers.
/// </summary>
public class SimulatorLoader : MonoBehaviour
{
    [Tooltip("Drag the 'XR Interaction Simulator' prefab from\n" +
             "Assets/Samples/XR Interaction Toolkit/.../XR Interaction Simulator/ here.")]
    public GameObject simulatorPrefab;

    private void Awake()
    {
        // Check if a simulator already exists in the active scene
        var existing = GameObject.Find("XR Interaction Simulator(Clone)") ?? 
                       GameObject.Find("XR Interaction Simulator") ?? 
                       GameObject.Find("XR Device Simulator");

        if (existing == null && simulatorPrefab != null)
        {
            Instantiate(simulatorPrefab);
        }
    }
}
