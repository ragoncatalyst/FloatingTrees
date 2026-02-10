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
    private string positionFilePath;
    // 存储格式：层级 -> 方块号 -> 是否启用
    private Dictionary<int, Dictionary<int, bool>> blockStates = new Dictionary<int, Dictionary<int, bool>>();
    
    // 存储Rocket的位置、旋转和摄像头角度（仅在Main场景使用）
    private Vector3 savedRocketPosition = Vector3.zero;
    private Quaternion savedRocketRotation = Quaternion.identity;
    private float savedCameraYRotation = 0f;
    private bool hasPositionData = false;
    
    void Awake()
    {
        // 设置保存文件路径到Assets文件夹
        saveFilePath = Path.Combine(Application.dataPath, "RocketStatus.txt");
        positionFilePath = Path.Combine(Application.dataPath, "RocketPosition.txt");
        Debug.Log($"[RocketStateManager] Save file path: {saveFilePath}");
        Debug.Log($"[RocketStateManager] Position file path: {positionFilePath}");
        
        // 单例模式 - 确保只有一个实例，并且在场景切换时不被销毁
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 启动时加载保存的状态
            LoadStateFromFile();
            LoadPositionFromFile();
            
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
        
        // 如果是Main场景且有保存的位置数据，恢复位置
        if (scene.name == "Main" && hasPositionData)
        {
            StartCoroutine(ApplyRocketPositionAsync());
        }
        
        // 如果是Workshop场景且有保存的摄像头数据，恢复摄像头角度
        if (scene.name == "Workshop" && hasPositionData)
        {
            StartCoroutine(ApplyCameraAngleAsync());
        }
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
        
        // 保存位置和旋转（仅在Main场景）
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentScene == "Main")
        {
            SaveRocketPosition(rocket);
        }
        
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
        
        // 如果没有保存的状态，默认为单个方块
        if (blockStates.Count == 0)
        {
            Debug.Log("[RocketStateManager] 配置文件为空，初始化为单个方块");
            InitializeToSingleBlock(rocket);
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
                // 如果blockStates中有数据，但某个方块不在记录中，说明该方块应该被禁用
                bool shouldBeEnabled = false; // 默认禁用（重要！）
                
                if (blockStates.ContainsKey(layer) && blockStates[layer].ContainsKey(blockNum))
                {
                    shouldBeEnabled = blockStates[layer][blockNum];
                }
                else
                {
                    // 如果没有记录，说明这个方块应该是禁用的
                    shouldBeEnabled = false;
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
    
    // 初始化为单个方块（Layer1的Cube13）
    void InitializeToSingleBlock(GameObject rocket)
    {
        int disabledCount = 0;
        
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            foreach (Transform child in layerTransform)
            {
                if (child.name.Contains("Cube"))
                {
                    // 只有Layer1的Cube13保持开启
                    bool shouldEnable = (layer == 1 && child.name == "Cube13");
                    
                    child.gameObject.SetActive(shouldEnable);
                    Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        r.enabled = shouldEnable;
                    }
                    
                    if (!shouldEnable)
                        disabledCount++;
                }
            }
        }
        
        Debug.Log($"[RocketStateManager] 初始化为单个方块（Layer1/Cube13），禁用了{disabledCount}个方块");
        
        // 保存这个默认状态
        SaveRocketState();
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
                
                // 检查文件是否有有效内容
                bool hasValidContent = false;
                
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
                    hasValidContent = true;
                }
                
                if (hasValidContent)
                {
                    Debug.Log($"[RocketStateManager] State loaded from file: {lines.Length} entries, {blockStates.Count} layers");
                }
                else
                {
                    Debug.Log($"[RocketStateManager] File exists but is empty or invalid, will initialize to single block");
                    blockStates.Clear(); // 确保清空
                }
            }
            else
            {
                Debug.Log($"[RocketStateManager] No saved state file found, will initialize to single block");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RocketStateManager] Failed to load state from file: {e.Message}");
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
    
    // 公共方法：重置为单个方块并保存
    public static void ResetToSingleBlock()
    {
        if (instance != null)
        {
            GameObject rocket = GameObject.Find(instance.rocketContainerName);
            if (rocket != null)
            {
                instance.InitializeToSingleBlock(rocket);
                Debug.Log("[RocketStateManager] 已重置为单个方块并保存");
            }
            else
            {
                Debug.LogWarning("[RocketStateManager] 未找到Rocket，无法重置");
            }
        }
    }
    
    // 公共方法：清空配置文件并重新加载（会初始化为单个方块）
    public static void ClearAndReload()
    {
        if (instance != null)
        {
            // 清空内存中的状态
            instance.blockStates.Clear();
            
            // 清空文件
            try
            {
                if (File.Exists(instance.saveFilePath))
                {
                    File.WriteAllText(instance.saveFilePath, "");
                    Debug.Log($"[RocketStateManager] 配置文件已清空: {instance.saveFilePath}");
                    
                    #if UNITY_EDITOR
                    UnityEditor.AssetDatabase.Refresh();
                    #endif
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RocketStateManager] 清空文件失败: {e.Message}");
            }
            
            // 重新加载并应用（会初始化为单个方块因为文件为空）
            GameObject rocket = GameObject.Find(instance.rocketContainerName);
            if (rocket != null)
            {
                instance.InitializeToSingleBlock(rocket);
                Debug.Log("[RocketStateManager] 已清空配置文件并重新初始化为单个方块");
            }
        }
    }
    
    // ========== 位置和摄像头方向保存/恢复 ==========
    
    /// <summary>
    /// 保存Rocket的位置、旋转和摄像头角度
    /// </summary>
    void SaveRocketPosition(GameObject rocket)
    {
        if (rocket == null) return;
        
        savedRocketPosition = rocket.transform.position;
        savedRocketRotation = rocket.transform.rotation;
        
        // 获取摄像头角度
        CamaraFollow cameraFollow = FindObjectOfType<CamaraFollow>();
        if (cameraFollow != null)
        {
            savedCameraYRotation = cameraFollow.GetCurrentAngleIndex() * 90f;
        }
        else
        {
            savedCameraYRotation = 0f;
        }
        
        hasPositionData = true;
        
        // 保存到文件
        SavePositionToFile();
        
        Debug.Log($"[RocketStateManager] 保存位置: {savedRocketPosition}, 旋转: {savedRocketRotation.eulerAngles}, 摄像头: {savedCameraYRotation}°");
    }
    
    /// <summary>
    /// 异步应用保存的Rocket位置和摄像头角度
    /// </summary>
    IEnumerator ApplyRocketPositionAsync()
    {
        yield return new WaitForSeconds(0.2f); // 等待场景完全初始化
        
        GameObject rocket = GameObject.Find(rocketContainerName);
        if (rocket == null)
        {
            Debug.LogWarning("[RocketStateManager] 无法恢复位置：未找到Rocket");
            yield break;
        }
        
        // 恢复位置和旋转
        rocket.transform.position = savedRocketPosition;
        rocket.transform.rotation = savedRocketRotation;
        
        // 恢复摄像头角度
        CamaraFollow cameraFollow = FindObjectOfType<CamaraFollow>();
        if (cameraFollow != null)
        {
            // 通过反射或公共方法设置摄像头角度
            // 这里需要在CamaraFollow中添加一个公共方法来设置角度
            StartCoroutine(RestoreCameraAngle(cameraFollow));
        }
        
        Debug.Log($"[RocketStateManager] 恢复位置: {savedRocketPosition}, 旋转: {savedRocketRotation.eulerAngles}, 摄像头: {savedCameraYRotation}°");
    }
    
    /// <summary>
    /// 异步应用摄像头角度（仅摄像头，用于Workshop场景）
    /// </summary>
    IEnumerator ApplyCameraAngleAsync()
    {
        yield return new WaitForSeconds(0.2f); // 等待场景完全初始化
        
        // 恢复摄像头角度
        CamaraFollow cameraFollow = FindObjectOfType<CamaraFollow>();
        if (cameraFollow != null)
        {
            StartCoroutine(RestoreCameraAngle(cameraFollow));
        }
        else
        {
            Debug.LogWarning("[RocketStateManager] Workshop场景中未找到CamaraFollow");
        }
    }
    
    /// <summary>
    /// 恢复摄像头角度
    /// </summary>
    IEnumerator RestoreCameraAngle(CamaraFollow cameraFollow)
    {
        yield return null; // 等待一帧
        
        // 计算需要旋转到的角度索引 (0-3)
        int targetIndex = Mathf.RoundToInt(savedCameraYRotation / 90f) % 4;
        
        // 直接设置摄像头角度
        cameraFollow.SetCameraAngle(targetIndex);
        
        Debug.Log($"[RocketStateManager] 摄像头已恢复到角度索引 {targetIndex} ({savedCameraYRotation}°)");
    }
    
    /// <summary>
    /// 保存位置到文件
    /// </summary>
    void SavePositionToFile()
    {
        try
        {
            List<string> lines = new List<string>();
            lines.Add($"POS:{savedRocketPosition.x:F3},{savedRocketPosition.y:F3},{savedRocketPosition.z:F3}");
            lines.Add($"ROT:{savedRocketRotation.eulerAngles.x:F3},{savedRocketRotation.eulerAngles.y:F3},{savedRocketRotation.eulerAngles.z:F3}");
            lines.Add($"CAM:{savedCameraYRotation:F1}");
            
            File.WriteAllLines(positionFilePath, lines);
            
            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RocketStateManager] 保存位置失败: {e.Message}");
        }
    }
    
    /// <summary>
    /// 从文件加载位置
    /// </summary>
    void LoadPositionFromFile()
    {
        try
        {
            if (!File.Exists(positionFilePath))
            {
                Debug.Log("[RocketStateManager] 位置文件不存在");
                hasPositionData = false;
                return;
            }
            
            string[] lines = File.ReadAllLines(positionFilePath);
            
            foreach (string line in lines)
            {
                if (line.StartsWith("POS:"))
                {
                    string[] parts = line.Substring(4).Split(',');
                    if (parts.Length == 3)
                    {
                        savedRocketPosition = new Vector3(
                            float.Parse(parts[0]),
                            float.Parse(parts[1]),
                            float.Parse(parts[2])
                        );
                    }
                }
                else if (line.StartsWith("ROT:"))
                {
                    string[] parts = line.Substring(4).Split(',');
                    if (parts.Length == 3)
                    {
                        savedRocketRotation = Quaternion.Euler(
                            float.Parse(parts[0]),
                            float.Parse(parts[1]),
                            float.Parse(parts[2])
                        );
                    }
                }
                else if (line.StartsWith("CAM:"))
                {
                    savedCameraYRotation = float.Parse(line.Substring(4));
                }
            }
            
            hasPositionData = true;
            Debug.Log($"[RocketStateManager] 加载位置成功: {savedRocketPosition}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RocketStateManager] 加载位置失败: {e.Message}");
            hasPositionData = false;
        }
    }
}