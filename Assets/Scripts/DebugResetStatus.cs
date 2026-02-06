using UnityEngine;

public class DebugResetStatus : MonoBehaviour
{
    [Header("设置")]
    [SerializeField] private string rocketName = "Rocket";
    [SerializeField] private KeyCode resetKey = KeyCode.Slash; // / 键
    
    void Update()
    {
        // 检测 / 键
        if (Input.GetKeyDown(resetKey))
        {
            ResetAllBlocks();
        }
    }
    
    void ResetAllBlocks()
    {
        // 查找Rocket
        GameObject rocket = GameObject.Find(rocketName);
        if (rocket == null)
        {
            Debug.LogWarning($"[DebugResetStatus] Rocket '{rocketName}' not found!");
            return;
        }
        
        int resetCount = 0;
        
        // 遍历所有Layer（Layer1-Layer5）
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            // 遍历Layer下所有子物体
            foreach (Transform child in layerTransform)
            {
                if (child.name.Contains("Cube"))
                {
                    if (!child.gameObject.activeSelf)
                    {
                        child.gameObject.SetActive(true);
                        resetCount++;
                    }
                }
            }
        }
        
        Debug.Log($"[DebugResetStatus] Reset complete! Enabled {resetCount} blocks.");
        
        // 如果有RocketStateManager，也保存当前状态
        RocketStateManager.Save();
    }
}
