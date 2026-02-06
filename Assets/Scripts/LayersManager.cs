using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 图层管理器 - 控制Workshop场景中的图层显示
/// 优化版：动态获取Renderer，直接修改材质alpha，避免与BlockEditor冲突
/// </summary>
public class LayersManager : MonoBehaviour
{
    [Header("场景设置")]
    [SerializeField] private string workshopSceneName = "Workshop";
    
    [Header("层级设置")]
    [SerializeField] private string[] layerNames = { "Layer1", "Layer2", "Layer3", "Layer4", "Layer5" };
    
    private Dictionary<int, GameObject> layerObjects = new Dictionary<int, GameObject>();
    private int lastPressedLayerButton = -1; // -1表示没有按过，1-5表示对应层级
    private bool isInWorkshop = false;
    
    // 存储原始的alpha值
    private Dictionary<Material, float> originalAlphas = new Dictionary<Material, float>();
    
    void Start()
    {
        // 检查是否在Workshop场景
        isInWorkshop = SceneManager.GetActiveScene().name == workshopSceneName;
        
        if (!isInWorkshop)
        {
            enabled = false;
            return;
        }
        
        Debug.Log("[LayersManager] Workshop场景已加载，初始化层级管理");
        InitializeLayers();
    }
    
    void InitializeLayers()
    {
        layerObjects.Clear();
        originalAlphas.Clear();
        
        // 查找所有层级物体
        for (int i = 0; i < layerNames.Length; i++)
        {
            int layerIndex = i + 1; // 1-5
            GameObject layerObj = transform.Find(layerNames[i])?.gameObject;
            
            if (layerObj != null)
            {
                layerObjects[layerIndex] = layerObj;
                Debug.Log($"[LayersManager] 初始化 {layerNames[i]}");
            }
        }
    }
    
    void Update()
    {
        if (!isInWorkshop) return;
        
        // 检测数字键1-5
        if (Input.GetKeyDown(KeyCode.Alpha1)) HandleLayerPress(1);
        if (Input.GetKeyDown(KeyCode.Alpha2)) HandleLayerPress(2);
        if (Input.GetKeyDown(KeyCode.Alpha3)) HandleLayerPress(3);
        if (Input.GetKeyDown(KeyCode.Alpha4)) HandleLayerPress(4);
        if (Input.GetKeyDown(KeyCode.Alpha5)) HandleLayerPress(5);
        
        // 检测~键（显示全部层级）
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            ShowAllLayers();
        }
        
        // 检测/键（只显示Layer1的Cube13）
        if (Input.GetKeyDown(KeyCode.Slash))
        {
            ShowOnlyCube13();
        }
    }
    
    void HandleLayerPress(int layerIndex)
    {
        if (!layerObjects.ContainsKey(layerIndex))
        {
            return;
        }
        
        // 通知BlockEditor清除hover状态
        ClearBlockEditorHover();
        
        // 判断是否是重复按下同一层级
        if (lastPressedLayerButton == layerIndex)
        {
            // 第二次按下：其他层完全透明
            SetLayerAlpha(layerIndex, 1.0f);
            
            for (int i = 1; i <= 5; i++)
            {
                if (i != layerIndex && layerObjects.ContainsKey(i))
                {
                    SetLayerAlpha(i, 0f);
                }
            }
        }
        else
        {
            // 第一次按下：其他层10%透明
            SetLayerAlpha(layerIndex, 1.0f);
            
            for (int i = 1; i <= 5; i++)
            {
                if (i != layerIndex && layerObjects.ContainsKey(i))
                {
                    SetLayerAlpha(i, 0.1f);
                }
            }
        }
        
        lastPressedLayerButton = layerIndex;
    }
    
    void ShowAllLayers()
    {
        Debug.Log("[LayersManager] 显示全部层级");
        
        // 通知BlockEditor清除hover状态
        ClearBlockEditorHover();
        
        // 恢复所有层级到100%不透明
        for (int i = 1; i <= 5; i++)
        {
            if (layerObjects.ContainsKey(i))
            {
                SetLayerAlpha(i, 1.0f);
            }
        }
        
        lastPressedLayerButton = -1;
    }
    
    void ShowOnlyCube13()
    {
        Debug.Log("[LayersManager] 只显示Layer1的Cube13");
        
        // 通知BlockEditor清除hover状态
        ClearBlockEditorHover();
        
        // 遍历所有Layer
        for (int layer = 1; layer <= 5; layer++)
        {
            if (!layerObjects.ContainsKey(layer)) continue;
            
            Transform layerTransform = layerObjects[layer].transform;
            
            // 遍历该Layer下的所有子物体
            foreach (Transform child in layerTransform)
            {
                if (child.name.Contains("Cube"))
                {
                    // 只有Layer1的Cube13保持开启
                    if (layer == 1 && child.name == "Cube13")
                    {
                        child.gameObject.SetActive(true);
                        Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                        foreach (Renderer r in renderers)
                        {
                            r.enabled = true;
                        }
                    }
                    else
                    {
                        child.gameObject.SetActive(false);
                        Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                        foreach (Renderer r in renderers)
                        {
                            r.enabled = false;
                        }
                    }
                }
            }
        }
        
        // 恢复所有层级的透明度
        for (int i = 1; i <= 5; i++)
        {
            if (layerObjects.ContainsKey(i))
            {
                SetLayerAlpha(i, 1.0f);
            }
        }
        
        lastPressedLayerButton = -1;
    }
    
    void SetLayerAlpha(int layerIndex, float alpha)
    {
        if (!layerObjects.ContainsKey(layerIndex)) return;
        
        GameObject layerObj = layerObjects[layerIndex];
        
        // 动态获取当前激活的Renderer（包括启用和禁用的）
        Renderer[] renderers = layerObj.GetComponentsInChildren<Renderer>(true);
        
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            
            // 如果alpha为0，直接禁用Renderer
            if (alpha <= 0.01f)
            {
                renderer.enabled = false;
                continue;
            }
            
            // 启用Renderer
            renderer.enabled = true;
            
            // 使用materials获取实例材质（会自动创建副本）
            Material[] materials = renderer.materials;
            
            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;
                
                if (mat.HasProperty("_Color"))
                {
                    // 保存原始的alpha值
                    if (!originalAlphas.ContainsKey(mat))
                    {
                        originalAlphas[mat] = mat.color.a;
                    }
                    
                    // 如果需要半透明，设置材质为Transparent模式
                    if (alpha < 0.99f)
                    {
                        SetMaterialTransparent(mat);
                    }
                    else
                    {
                        SetMaterialOpaque(mat);
                    }
                    
                    // 设置新的alpha值
                    Color color = mat.color;
                    if (alpha >= 0.99f)
                    {
                        color.a = originalAlphas.ContainsKey(mat) ? originalAlphas[mat] : 1.0f;
                    }
                    else
                    {
                        color.a = alpha;
                    }
                    
                    mat.color = color;
                }
            }
            
            // 重新赋值材质数组以确保修改生效
            renderer.materials = materials;
        }
    }
    
    void SetMaterialTransparent(Material mat)
    {
        // 设置为Transparent渲染模式
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }
    
    void SetMaterialOpaque(Material mat)
    {
        // 恢复为Opaque渲染模式
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1;
    }
    
    void ClearBlockEditorHover()
    {
        // 查找BlockEditor并清除hover状态
        BlockEditor blockEditor = FindObjectOfType<BlockEditor>();
        if (blockEditor != null)
        {
            blockEditor.ClearHover();
        }
    }
    
    void OnDisable()
    {
        if (!isInWorkshop) return;
        
        // 恢复所有层级到原始状态
        for (int i = 1; i <= 5; i++)
        {
            if (layerObjects.ContainsKey(i))
            {
                SetLayerAlpha(i, 1.0f);
            }
        }
        
        lastPressedLayerButton = -1;
        originalAlphas.Clear();
    }
}
