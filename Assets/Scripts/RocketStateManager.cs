using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 火箭状态管理器 - 跨场景同步火箭方块的启用/禁用状态
/// 性能优化：减少不必要的日志输出，只在关键操作时记录
/// 使用协程分帧处理，避免一次性处理所有方块造成卡顿
/// </summary>
public class RocketStateManager : MonoBehaviour
{
    private static RocketStateManager instance;
    
    [Header("设置")]
    [SerializeField] private string rocketContainerName = "Rocket";
    
    private string saveFilePath;
    // 存储格式：层级 -> 方块号 -> 是否启用
    private Dictionary<int, Dictionary<int, bool>> blockStates = new Dictionary<int, Dictionary<int, bool>>();
    
    void Awake()
    {
        // 设置保存文件路径到Assets文件夹
        saveFilePath = Path.Combine(Application.dataPath, "RocketStatus.txt");
        Debug.Log($"[RocketStateManager] Save file path: {saveFilePath}");
        
        // 单例模式 - 确保只有一个实例，并且在场景切换时不被销毁
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 启动时加载保存的状态
            LoadStateFromFile();
            
            Debug.Log("[RocketStateManager] Initialized and set to DontDestroyOnLoad");
            
            // 使用协程异步应用状态
            StartCoroutine(ApplyRocketStateAsync());
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void OnEnable()
    {
        // 订阅场景加载事件
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 使用协程异步应用状态，避免卡顿
        StartCoroutine(ApplyRocketStateAsync());
    }
    
    void UpdateLighting()
    {
        // 强制更新光照和天空盒
        DynamicGI.UpdateEnvironment();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
    }
    
    // 保存当前Rocket的状态
    public void SaveRocketState()
    {
        blockStates.Clear();
        
        // 查找Rocket容器
        GameObject rocket = GameObject.Find(rocketContainerName);
        if (rocket == null)
        {
            Debug.LogWarning($"[RocketStateManager] Rocket container '{rocketContainerName}' not found!");
            return;
        }
        
        int totalSaved = 0;
        int disabledSaved = 0;
        
        // 遍历所有Layer（Layer1-Layer5）
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            blockStates[layer] = new Dictionary<int, bool>();
            
            // 遍历Layer下的所有直接子物体Cube
            foreach (Transform child in layerTransform)
            {
                // 尝试从名字中提取方块号（例如"Cube22" -> 22）
                Match match = Regex.Match(child.name, @"Cube(\d+)");
                if (match.Success)
                {
                    int blockNum = int.Parse(match.Groups[1].Value);
                    bool isEnabled = child.gameObject.activeSelf;
                    blockStates[layer][blockNum] = isEnabled;
                    totalSaved++;
                    if (!isEnabled)
                        disabledSaved++;
                }
            }
        }
        
        Debug.Log($"[RocketStateManager] Saved {totalSaved} blocks total, {disabledSaved} disabled");
        
        // 保存到文件
        SaveStateToFile();
    }
    
    /// <summary>
    /// 异步应用保存的状态（协程版本，分帧处理避免卡顿）
    /// </summary>
    IEnumerator ApplyRocketStateAsync()
    {
        // 等待一帧，确保场景对象完全初始化
        yield return null;
        
        // 查找Rocket容器
        GameObject rocket = GameObject.Find(rocketContainerName);
        if (rocket == null)
        {
            Debug.LogWarning($"[RocketStateManager] Rocket container '{rocketContainerName}' not found in scene!");
            yield break;
        }
        
        int enabledCount = 0;
        int disabledCount = 0;
        int totalProcessed = 0;
        
        // 如果没有保存的状态，启用所有方块（默认状态）
        if (blockStates.Count == 0)
        {
            EnableAllBlocks(rocket);
            yield break;
        }
        
        // 遍历场景中所有实际存在的Layer和Cube，每处理一层就yield一次
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            // 遍历该Layer下的所有子物体
            foreach (Transform child in layerTransform)
            {
                // 尝试从名字中提取方块号
                Match match = Regex.Match(child.name, @"Cube(\d+)");
                if (!match.Success) continue;
                
                int blockNum = int.Parse(match.Groups[1].Value);
                
                // 检查保存的状态中是否有这个方块的记录
                bool shouldBeEnabled = true; // 默认启用
                
                if (blockStates.ContainsKey(layer) && blockStates[layer].ContainsKey(blockNum))
                {
                    shouldBeEnabled = blockStates[layer][blockNum];
                }
                
                // 应用状态
                child.gameObject.SetActive(shouldBeEnabled);
                
                // 显式控制Renderer（确保视觉同步）
                Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    r.enabled = shouldBeEnabled;
                }
                
                totalProcessed++;
                if (shouldBeEnabled)
                    enabledCount++;
                else
                    disabledCount++;
            }
            
            // 每处理完一层，让出一帧给其他系统
            yield return null;
        }
        
        // 更新光照
        UpdateLighting();
        
        Debug.Log($"[RocketStateManager] Applied: {totalProcessed} total, {enabledCount} enabled, {disabledCount} disabled");
    }
    
    // 保留同步版本用于手动调用
    void ApplyRocketState()
    {
        StartCoroutine(ApplyRocketStateAsync());
    }
    
    // 启用所有方块（默认状态）
    void EnableAllBlocks(GameObject rocket)
    {
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            foreach (Transform child in layerTransform)
            {
                if (child.name.Contains("Cube"))
                {
                    child.gameObject.SetActive(true);
                    Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        r.enabled = true;
                    }
                }
            }
        }
        Debug.Log($"[RocketStateManager] All blocks enabled (default state)");
    }
    
    // 保存状态到文件（格式：层级+方块号+启用状态）
    void SaveStateToFile()
    {
        try
        {
            // 确保目录存在
            string directory = Path.GetDirectoryName(saveFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            List<string> lines = new List<string>();
            
            // 按层级和方块号排序输出
            for (int layer = 1; layer <= 5; layer++)
            {
                if (!blockStates.ContainsKey(layer)) continue;
                
                var sortedBlocks = new List<int>(blockStates[layer].Keys);
                sortedBlocks.Sort();
                
                foreach (int blockNum in sortedBlocks)
                {
                    bool isEnabled = blockStates[layer][blockNum];
                    // 格式：1位layer + 2位blockNum + 1位enabled（0或1）
                    string line = $"{layer}{blockNum:D2}{(isEnabled ? "1" : "0")}";
                    lines.Add(line);
                }
            }
            
            File.WriteAllLines(saveFilePath, lines);
            Debug.Log($"[RocketStateManager] State saved successfully to: {saveFilePath}");
            Debug.Log($"[RocketStateManager] Total lines: {lines.Count}");
            
            // 刷新Unity的Asset数据库
            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }
        catch
        {
            // 保存失败，静默处理
        }
    }
    
    // 从文件加载状态
    void LoadStateFromFile()
    {
        try
        {
            blockStates.Clear();
            
            if (File.Exists(saveFilePath))
            {
                string[] lines = File.ReadAllLines(saveFilePath);
                
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Length != 4) continue;
                    
                    // 解析格式：1位layer + 2位blockNum + 1位enabled
                    int layer = int.Parse(line.Substring(0, 1));
                    int blockNum = int.Parse(line.Substring(1, 2));
                    bool isEnabled = line.Substring(3, 1) == "1";
                    
                    if (!blockStates.ContainsKey(layer))
                    {
                        blockStates[layer] = new Dictionary<int, bool>();
                    }
                    
                    blockStates[layer][blockNum] = isEnabled;
                }
                
                Debug.Log($"[RocketStateManager] State loaded from file: {lines.Length} entries");
            }
            else
            {
                Debug.Log($"[RocketStateManager] No saved state file found, starting fresh");
            }
        }
        catch
        {
            blockStates.Clear();
        }
    }
    
    // 公共方法：手动保存状态
    public static void Save()
    {
        if (instance != null)
        {
            instance.SaveRocketState();
        }
    }
    
    // 公共方法：手动应用状态
    public static void Apply()
    {
        if (instance != null)
        {
            instance.ApplyRocketState();
        }
    }
}
