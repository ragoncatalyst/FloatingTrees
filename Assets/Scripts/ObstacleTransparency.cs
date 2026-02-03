using System.Collections.Generic;
using UnityEngine;

public class ObstacleTransparency : MonoBehaviour
{
    [Header("目标设置")]
    [SerializeField] private Transform target; // 要观察的目标（火箭）
    
    [Header("透明设置")]
    [SerializeField] private float obstacleTransparency = 0.3f;     // 障碍物透明度（70%透明）
    [SerializeField] private LayerMask obstacleLayer = -1;          // 障碍物图层
    
    private float transitionTime = 2f;  // 透明度变化时间（2秒）
    
    // 障碍物透明处理
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> transparentMaterials = new Dictionary<Renderer, Material[]>();
    private HashSet<Renderer> currentObstacles = new HashSet<Renderer>();

    void Start()
    {
        // 硬编码查找名为Rocket的物体
        GameObject rocket = GameObject.Find("Rocket");
        if (rocket != null)
        {
            target = rocket.transform;
            Debug.Log("[ObstacleTransparency] 已找到目标: Rocket");
        }
        else
        {
            Debug.LogError("[ObstacleTransparency] 未找到名为'Rocket'的物体!");
        }
    }

    void LateUpdate()
    {
        if (target == null) return;
        
        HandleObstacles();
    }
    
    /// <summary>
    /// 处理视线被遮挡的障碍物透明化和恢复
    /// </summary>
    void HandleObstacles()
    {
        HashSet<Renderer> newObstacles = new HashSet<Renderer>();
        
        // 从摄像头向火箭发射射线检测障碍物
        Vector3 direction = target.position - transform.position;
        float distance = direction.magnitude;
        
        RaycastHit[] hits = Physics.RaycastAll(transform.position, direction.normalized, distance, obstacleLayer);
        
        // 收集当前遮挡的物体
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == target || hit.transform.IsChildOf(target))
                continue;
                
            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer != null)
            {
                newObstacles.Add(renderer);
                
                // 如果是新障碍物，初始化透明材质
                if (!transparentMaterials.ContainsKey(renderer))
                {
                    InitializeTransparentMaterials(renderer);
                }
            }
        }
        
        // 处理所有已知的透明物体
        List<Renderer> toRemove = new List<Renderer>();
        
        foreach (var pair in transparentMaterials)
        {
            Renderer renderer = pair.Key;
            if (renderer == null)
            {
                toRemove.Add(renderer);
                continue;
            }
            
            Material[] materials = pair.Value;
            float targetAlpha = newObstacles.Contains(renderer) ? obstacleTransparency : 1f;
            float speed = 1f / transitionTime;
            bool fullyRestored = true;
            
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].HasProperty("_Color"))
                {
                    Color color = materials[i].color;
                    color.a = Mathf.MoveTowards(color.a, targetAlpha, speed * Time.deltaTime);
                    materials[i].color = color;
                    
                    if (targetAlpha >= 1f && color.a < 0.99f)
                        fullyRestored = false;
                }
            }
            
            // 如果完全恢复，还原原始材质并清理
            if (targetAlpha >= 1f && fullyRestored)
            {
                if (originalMaterials.ContainsKey(renderer))
                {
                    renderer.materials = originalMaterials[renderer];
                    toRemove.Add(renderer);
                    Debug.Log($"[ObstacleTransparency] {renderer.name} 已完全恢复并清理");
                }
            }
        }
        
        // 清理已恢复的物体
        foreach (Renderer renderer in toRemove)
        {
            CleanupRenderer(renderer);
        }
        
        currentObstacles = newObstacles;
    }
    
    void InitializeTransparentMaterials(Renderer renderer)
    {
        originalMaterials[renderer] = renderer.sharedMaterials;
        
        // 创建透明材质副本
        Material[] transparentMats = new Material[renderer.sharedMaterials.Length];
        for (int i = 0; i < renderer.sharedMaterials.Length; i++)
        {
            transparentMats[i] = new Material(renderer.sharedMaterials[i]);
            SetMaterialTransparent(transparentMats[i]);
        }
        transparentMaterials[renderer] = transparentMats;
        
        // 应用透明材质
        renderer.materials = transparentMats;
        Debug.Log($"[ObstacleTransparency] 初始化透明材质: {renderer.name}");
    }
    
    void CleanupRenderer(Renderer renderer)
    {
        if (transparentMaterials.ContainsKey(renderer))
        {
            foreach (Material mat in transparentMaterials[renderer])
            {
                if (mat != null)
                    Destroy(mat);
            }
            transparentMaterials.Remove(renderer);
        }
        
        originalMaterials.Remove(renderer);
    }
    
    void SetMaterialTransparent(Material mat)
    {
        // 设置为透明模式
        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 3); // Transparent mode
        }
        
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        
        // 初始设置为完全不透明
        if (mat.HasProperty("_Color"))
        {
            Color color = mat.color;
            color.a = 1f;
            mat.color = color;
        }
    }
    
    void OnDestroy()
    {
        // 清理所有透明材质
        foreach (var pair in transparentMaterials)
        {
            foreach (Material mat in pair.Value)
            {
                if (mat != null)
                    Destroy(mat);
            }
        }
        
        // 恢复所有原始材质
        foreach (var pair in originalMaterials)
        {
            if (pair.Key != null)
                pair.Key.materials = pair.Value;
        }
    }
}
