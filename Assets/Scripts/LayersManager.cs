using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LayersManager : MonoBehaviour
{
    [Header("场景设置")]
    [SerializeField] private string workshopSceneName = "Workshop";
    
    [Header("层级设置")]
    [SerializeField] private string[] layerNames = { "Layer1", "Layer2", "Layer3", "Layer4", "Layer5" };
    
    private Dictionary<int, GameObject> layerObjects = new Dictionary<int, GameObject>();
    private Dictionary<int, List<Renderer>> layerRenderers = new Dictionary<int, List<Renderer>>();
    private Dictionary<int, Dictionary<Renderer, Material[]>> originalMaterials = new Dictionary<int, Dictionary<Renderer, Material[]>>();
    private Dictionary<int, Dictionary<Renderer, Material[]>> transparentMaterials = new Dictionary<int, Dictionary<Renderer, Material[]>>();
    
    private int lastPressedLayerButton = -1; // -1表示没有按过，1-5表示对应层级
    private bool isInWorkshop = false;
    
    void Start()
    {
        // 检查是否在Workshop场景
        isInWorkshop = SceneManager.GetActiveScene().name == workshopSceneName;
        
        if (!isInWorkshop)
        {
            Debug.Log("[LayersManager] 当前不在Workshop场景，LayersManager不会生效");
            enabled = false;
            return;
        }
        
        Debug.Log("[LayersManager] Workshop场景已加载，初始化层级管理");
        InitializeLayers();
    }
    
    void InitializeLayers()
    {
        // 查找所有层级物体
        for (int i = 0; i < layerNames.Length; i++)
        {
            int layerIndex = i + 1; // 1-5
            GameObject layerObj = transform.Find(layerNames[i])?.gameObject;
            
            if (layerObj != null)
            {
                layerObjects[layerIndex] = layerObj;
                
                // 收集该层所有Renderer
                List<Renderer> renderers = new List<Renderer>();
                Renderer[] allRenderers = layerObj.GetComponentsInChildren<Renderer>();
                renderers.AddRange(allRenderers);
                
                layerRenderers[layerIndex] = renderers;
                
                // 初始化材质字典
                originalMaterials[layerIndex] = new Dictionary<Renderer, Material[]>();
                transparentMaterials[layerIndex] = new Dictionary<Renderer, Material[]>();
                
                // 保存原始材质
                foreach (Renderer renderer in renderers)
                {
                    originalMaterials[layerIndex][renderer] = renderer.sharedMaterials;
                }
                
                Debug.Log($"[LayersManager] 初始化 {layerNames[i]}: {renderers.Count} 个Renderer");
            }
            else
            {
                Debug.LogWarning($"[LayersManager] 未找到层级: {layerNames[i]}");
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
        if (Input.GetKeyDown(KeyCode.BackQuote)) // ~键
        {
            ShowAllLayers();
        }
    }
    
    void HandleLayerPress(int layerIndex)
    {
        if (!layerObjects.ContainsKey(layerIndex))
        {
            Debug.LogWarning($"[LayersManager] 层级{layerIndex}不存在");
            return;
        }
        
        // 判断是否是重复按下同一层级
        if (lastPressedLayerButton == layerIndex)
        {
            // 第二次按下：其他层0%透明（完全隐藏）
            Debug.Log($"[LayersManager] 再次按下Layer{layerIndex}，隐藏其他层级");
            SetLayerTransparency(layerIndex, 1.0f); // 当前层保持不透明
            
            for (int i = 1; i <= 5; i++)
            {
                if (i != layerIndex && layerObjects.ContainsKey(i))
                {
                    SetLayerTransparency(i, 0f); // 其他层完全透明
                }
            }
        }
        else
        {
            // 第一次按下：其他层10%透明（几乎完全透明）
            Debug.Log($"[LayersManager] 按下Layer{layerIndex}，其他层级半透明");
            SetLayerTransparency(layerIndex, 1.0f); // 当前层保持不透明
            
            for (int i = 1; i <= 5; i++)
            {
                if (i != layerIndex && layerObjects.ContainsKey(i))
                {
                    SetLayerTransparency(i, 0.1f); // 其他层10%透明
                }
            }
        }
        
        lastPressedLayerButton = layerIndex;
    }
    
    void ShowAllLayers()
    {
        Debug.Log("[LayersManager] 显示全部层级");
        
        // 恢复所有层级到100%不透明
        for (int i = 1; i <= 5; i++)
        {
            if (layerObjects.ContainsKey(i))
            {
                SetLayerTransparency(i, 1.0f);
            }
        }
        
        lastPressedLayerButton = -1;
    }
    
    void SetLayerTransparency(int layerIndex, float alpha)
    {
        if (!layerRenderers.ContainsKey(layerIndex)) return;
        
        List<Renderer> renderers = layerRenderers[layerIndex];
        
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            
            // 如果透明度为1，恢复原始材质
            if (alpha >= 0.99f)
            {
                if (originalMaterials[layerIndex].ContainsKey(renderer))
                {
                    renderer.materials = originalMaterials[layerIndex][renderer];
                    
                    // 清理透明材质
                    if (transparentMaterials[layerIndex].ContainsKey(renderer))
                    {
                        foreach (Material mat in transparentMaterials[layerIndex][renderer])
                        {
                            if (mat != null) Destroy(mat);
                        }
                        transparentMaterials[layerIndex].Remove(renderer);
                    }
                }
            }
            else
            {
                // 创建或使用透明材质
                if (!transparentMaterials[layerIndex].ContainsKey(renderer))
                {
                    Material[] originalMats = originalMaterials[layerIndex][renderer];
                    Material[] transparentMats = new Material[originalMats.Length];
                    
                    for (int i = 0; i < originalMats.Length; i++)
                    {
                        transparentMats[i] = new Material(originalMats[i]);
                        SetMaterialTransparent(transparentMats[i]);
                    }
                    
                    transparentMaterials[layerIndex][renderer] = transparentMats;
                    renderer.materials = transparentMats;
                }
                
                // 设置透明度
                Material[] materials = transparentMaterials[layerIndex][renderer];
                foreach (Material mat in materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        Color color = mat.color;
                        color.a = alpha;
                        mat.color = color;
                    }
                }
            }
        }
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
    }
    
    void OnDestroy()
    {
        if (!isInWorkshop) return;
        
        Debug.Log("[LayersManager] 退出Workshop场景，清理并保存配置");
        
        // 恢复所有层级到100%不透明
        for (int i = 1; i <= 5; i++)
        {
            if (layerObjects.ContainsKey(i))
            {
                SetLayerTransparency(i, 1.0f);
            }
        }
        
        // 清空LastPressedLayerButton
        lastPressedLayerButton = -1;
        
        // 清理所有透明材质
        foreach (var layerPair in transparentMaterials)
        {
            foreach (var rendererPair in layerPair.Value)
            {
                foreach (Material mat in rendererPair.Value)
                {
                    if (mat != null) Destroy(mat);
                }
            }
        }
        
        // TODO: 保存当前配置
        Debug.Log("[LayersManager] 配置已保存");
    }
}
