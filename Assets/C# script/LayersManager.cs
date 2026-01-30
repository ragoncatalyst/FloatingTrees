using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LayersManager : MonoBehaviour
{
    [Header("层级设置")]
    [SerializeField] Transform rocketTransform;  // Rocket物体
    
    private Dictionary<int, Transform> layers = new Dictionary<int, Transform>();  // 层级映射
    private Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    private Dictionary<Renderer, Material[]> transparentMaterials = new Dictionary<Renderer, Material[]>();
    private int lastPressedLayerButton = -1;  // 上一次按下的层级按键（-1表示无）
    private float transparencySpeed = 5f;  // 透明度变化速度
    
    // 当前目标透明度
    private Dictionary<Renderer, float> targetAlpha = new Dictionary<Renderer, float>();
    
    void Start()
    {
        // 只在Workshop场景生效
        if (SceneManager.GetActiveScene().name != "Workshop")
        {
            enabled = false;
            return;
        }
        
        InitializeLayers();
    }
    
    void InitializeLayers()
    {
        // 查找Rocket物体
        if (rocketTransform == null)
        {
            GameObject rocket = GameObject.Find("Rocket");
            if (rocket != null)
            {
                rocketTransform = rocket.transform;
            }
            else
            {
                Debug.LogError("LayersManager: 未找到Rocket物体！");
                return;
            }
        }
        
        // 获取所有层级（Layer1-Layer5）
        for (int i = 1; i <= 5; i++)
        {
            Transform layer = rocketTransform.Find($"Layer{i}");
            if (layer != null)
            {
                layers[i] = layer;
                Debug.Log($"找到 Layer{i}");
            }
            else
            {
                Debug.LogWarning($"未找到 Layer{i}");
            }
        }
        
        // 收集所有Renderer并保存原始材质
        int totalRenderers = 0;
        foreach (var kvp in layers)
        {
            Renderer[] renderers = kvp.Value.GetComponentsInChildren<Renderer>(true);  // 包括非活动的对象
            Debug.Log($"Layer{kvp.Key} 找到 {renderers.Length} 个Renderer");
            
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                
                // 跳过已经处理过的Renderer
                if (originalMaterials.ContainsKey(renderer)) continue;
                
                // 克隆材质数组，避免共享材质问题
                Material[] originalMats = renderer.sharedMaterials;
                Material[] instanceMats = new Material[originalMats.Length];
                
                for (int i = 0; i < originalMats.Length; i++)
                {
                    instanceMats[i] = new Material(originalMats[i]);
                }
                
                // 应用实例化的材质
                renderer.materials = instanceMats;
                
                // 保存当前材质作为"原始"材质（已经是实例化的）
                originalMaterials[renderer] = renderer.materials;
                
                // 创建透明材质副本
                Material[] transparentMats = new Material[renderer.materials.Length];
                for (int i = 0; i < renderer.materials.Length; i++)
                {
                    transparentMats[i] = new Material(renderer.materials[i]);
                    SetMaterialTransparent(transparentMats[i]);
                }
                transparentMaterials[renderer] = transparentMats;
                
                // 初始化目标透明度为1（完全不透明）
                targetAlpha[renderer] = 1f;
                totalRenderers++;
                
                Debug.Log($"  - {renderer.gameObject.name}: {renderer.materials.Length} 个材质");
            }
        }
        
        Debug.Log($"LayersManager: 初始化完成，共找到 {layers.Count} 个层级，{totalRenderers} 个Renderer");
    }
    
    void Update()
    {
        HandleInput();
        UpdateTransparency();
    }
    
    void HandleInput()
    {
        // 检查数字键1-5
        for (int i = 1; i <= 5; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i - 1))
            {
                HandleLayerButton(i);
                return;
            }
        }
        
        // 检查~键（显示全部）
        if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Tilde))
        {
            ShowAllLayers();
        }
    }
    
    void HandleLayerButton(int layerNumber)
    {
        if (!layers.ContainsKey(layerNumber))
        {
            Debug.LogWarning($"Layer{layerNumber} 不存在！");
            return;
        }
        
        float otherLayersAlpha;
        
        // 判断是否是第二次按下同一个按键
        if (lastPressedLayerButton == layerNumber)
        {
            // 第二次按下：其他层完全隐藏（0%）
            otherLayersAlpha = 0f;
            Debug.Log($"Layer{layerNumber} 二次激活 - 其他层完全隐藏");
        }
        else
        {
            // 第一次按下：其他层半透明（10%）
            otherLayersAlpha = 0.1f;
            Debug.Log($"Layer{layerNumber} 激活 - 其他层半透明");
        }
        
        // 设置所有层的透明度
        foreach (var kvp in layers)
        {
            int currentLayer = kvp.Key;
            float alpha = (currentLayer == layerNumber) ? 1f : otherLayersAlpha;
            SetLayerAlpha(currentLayer, alpha);
        }
        
        // 记录当前按键
        lastPressedLayerButton = layerNumber;
    }
    
    void ShowAllLayers()
    {
        Debug.Log("显示全部层级");
        
        // 所有层恢复100%不透明度
        foreach (var kvp in layers)
        {
            SetLayerAlpha(kvp.Key, 1f);
        }
        
        // 清空记录
        lastPressedLayerButton = -1;
    }
    
    void SetLayerAlpha(int layerNumber, float alpha)
    {
        if (!layers.ContainsKey(layerNumber)) return;
        
        int count = 0;
        // 遍历所有已收集的Renderer，查找属于该层级的
        foreach (var kvp in targetAlpha.ToArray())
        {
            Renderer renderer = kvp.Key;
            if (renderer == null) continue;
            
            // 检查是否属于当前层级
            Transform current = renderer.transform;
            bool belongsToLayer = false;
            while (current != null)
            {
                if (current == layers[layerNumber])
                {
                    belongsToLayer = true;
                    break;
                }
                current = current.parent;
            }
            
            if (belongsToLayer)
            {
                targetAlpha[renderer] = alpha;
                count++;
                
                // 确保所有材质都是透明模式
                foreach (Material mat in renderer.materials)
                {
                    if (mat != null)
                    {
                        SetMaterialTransparent(mat);
                        Color color = mat.color;
                        color.a = alpha;
                        mat.color = color;
                    }
                }
            }
        }
        
        Debug.Log($"Layer{layerNumber} 设置透明度为 {alpha}，影响 {count} 个Renderer");
    }
    
    void UpdateTransparency()
    {
        foreach (var kvp in targetAlpha)
        {
            Renderer renderer = kvp.Key;
            float target = kvp.Value;
            
            if (renderer == null || renderer.materials == null) continue;
            
            // 直接在当前材质上更新透明度，不切换材质
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;
                
                Color color = mat.color;
                float currentAlpha = color.a;
                
                if (Mathf.Abs(currentAlpha - target) > 0.01f)
                {
                    color.a = Mathf.Lerp(currentAlpha, target, Time.deltaTime * transparencySpeed);
                    mat.color = color;
                }
                else if (Mathf.Abs(currentAlpha - target) > 0.001f)
                {
                    color.a = target;
                    mat.color = color;
                }
            }
        }
    }
    
    void SetMaterialTransparent(Material mat)
    {
        // 设置材质为透明模式
        mat.SetFloat("_Mode", 3); // Transparent mode
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }
    
    void OnDestroy()
    {
        // 退出scene时恢复所有方块到100%不透明度
        RestoreAllMaterials();
    }
    
    void OnDisable()
    {
        // 禁用时也恢复
        RestoreAllMaterials();
    }
    
    void RestoreAllMaterials()
    {
        Debug.Log("LayersManager: 恢复所有材质");
        
        // 恢复所有原始材质
        foreach (var kvp in originalMaterials)
        {
            if (kvp.Key != null)
            {
                kvp.Key.materials = kvp.Value;
            }
        }
        
        // 清理透明材质
        foreach (var kvp in transparentMaterials)
        {
            foreach (Material mat in kvp.Value)
            {
                if (mat != null)
                {
                    Destroy(mat);
                }
            }
        }
        
        // 清空记录
        lastPressedLayerButton = -1;
    }
}
