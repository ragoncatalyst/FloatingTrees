using UnityEngine;

public class DebugCubeStructure : MonoBehaviour
{
    [Header("设置")]
    [SerializeField] private string rocketName = "Rocket";
    [SerializeField] private KeyCode debugKey = KeyCode.F1;
    
    void Update()
    {
        if (Input.GetKeyDown(debugKey))
        {
            DebugFirstCube();
        }
    }
    
    void DebugFirstCube()
    {
        GameObject rocket = GameObject.Find(rocketName);
        if (rocket == null) return;
        
        // 找到Layer5的第一个Cube
        Transform layer5 = rocket.transform.Find("Layer5");
        if (layer5 == null) return;
        
        Transform firstCube = null;
        foreach (Transform child in layer5)
        {
            if (child.name.Contains("Cube"))
            {
                firstCube = child;
                break;
            }
        }
        
        if (firstCube == null) return;
        
        Debug.Log($"====== Analyzing {firstCube.name} ======");
        Debug.Log($"Active: {firstCube.gameObject.activeSelf}");
        Debug.Log($"Child count: {firstCube.childCount}");
        
        // 检查Renderer
        Renderer[] renderers = firstCube.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"Total renderers (including children): {renderers.Length}");
        
        foreach (Renderer r in renderers)
        {
            Debug.Log($"  - Renderer on: {r.gameObject.name}, enabled: {r.enabled}, active: {r.gameObject.activeSelf}");
        }
        
        // 检查子物体
        if (firstCube.childCount > 0)
        {
            Debug.Log($"Children:");
            foreach (Transform child in firstCube)
            {
                Debug.Log($"  - {child.name}, active: {child.gameObject.activeSelf}");
            }
        }
    }
}
