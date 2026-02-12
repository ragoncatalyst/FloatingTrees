using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StatusCompare : MonoBehaviour
{
    [Header("设置")]
    [SerializeField] private string rocketName = "Rocket";
    [SerializeField] private string workshopSceneName = "Workshop";
    [SerializeField] private string mainSceneName = "Main";
    
    private string workshopStatusPath;
    private string mainStatusPath;
    
    void Awake()
    {
        workshopStatusPath = Path.Combine(Application.dataPath, "RocketStatusWorkshop.txt");
        mainStatusPath = Path.Combine(Application.dataPath, "RocketStatusMain.txt");
        
        DontDestroyOnLoad(gameObject);
    }
    
    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 延迟一点，确保RocketStateManager已经应用了状态
        Invoke("CompareAndRecord", 0.5f);
    }
    
    void CompareAndRecord()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        
        // 记录当前场景的实际Rocket状态
        RecordCurrentRocketState(currentScene);
        
        // 如果在Main场景，对比两个文件
        if (currentScene == mainSceneName)
        {
            CompareStatusFiles();
        }
    }
    
    void RecordCurrentRocketState(string sceneName)
    {
        GameObject rocket = GameObject.Find(rocketName);
        if (rocket == null)
        {
            Debug.LogWarning($"[StatusCompare] Rocket not found in {sceneName}!");
            return;
        }
        
        List<string> lines = new List<string>();
        Dictionary<int, List<string>> disabledByLayer = new Dictionary<int, List<string>>();
        
        // 遍历所有Layer
        for (int layer = 1; layer <= 5; layer++)
        {
            Transform layerTransform = rocket.transform.Find($"Layer{layer}");
            if (layerTransform == null) continue;
            
            disabledByLayer[layer] = new List<string>();
            
            // 遍历Layer下所有Cube
            foreach (Transform child in layerTransform)
            {
                Match match = Regex.Match(child.name, @"Cube(\d+)");
                if (match.Success)
                {
                    int blockNum = int.Parse(match.Groups[1].Value);
                    bool isEnabled = child.gameObject.activeSelf;
                    
                    // 格式：层级(1位) + 方块号(2位) + 状态(1位)
                    string line = $"{layer}{blockNum:D2}{(isEnabled ? "1" : "0")}";
                    lines.Add(line);
                    
                    if (!isEnabled)
                    {
                        disabledByLayer[layer].Add($"Cube{blockNum:D2}");
                    }
                }
            }
        }
        
        // 排序
        lines.Sort();
        
        // 根据场景名保存到不同文件
        string filePath = (sceneName == workshopSceneName) ? workshopStatusPath : mainStatusPath;
        
        try
        {
            File.WriteAllLines(filePath, lines);
            
            // 输出摘要
            Debug.Log($"[StatusCompare] === {sceneName} Rocket Status ===");
            Debug.Log($"[StatusCompare] Total blocks: {lines.Count}");
            foreach (var kvp in disabledByLayer)
            {
                if (kvp.Value.Count > 0)
                {
                    Debug.Log($"[StatusCompare] Layer{kvp.Key} disabled ({kvp.Value.Count}): {string.Join(", ", kvp.Value)}");
                }
            }
            Debug.Log($"[StatusCompare] Saved to: {Path.GetFileName(filePath)}");
            
            #if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StatusCompare] Failed to save: {e.Message}");
        }
    }
    
    void CompareStatusFiles()
    {
        if (!File.Exists(workshopStatusPath) || !File.Exists(mainStatusPath))
        {
            Debug.LogWarning($"[StatusCompare] Cannot compare - files missing!");
            return;
        }
        
        Debug.Log($"[StatusCompare] ========== COMPARING STATUS FILES ==========");
        
        // 读取两个文件
        Dictionary<string, string> workshopData = LoadStatusFile(workshopStatusPath);
        Dictionary<string, string> mainData = LoadStatusFile(mainStatusPath);
        
        // 找出差异
        List<string> differences = new List<string>();
        int matchCount = 0;
        int mismatchCount = 0;
        
        foreach (var kvp in workshopData)
        {
            string key = kvp.Key; // "Layer1/Cube05"
            string workshopState = kvp.Value; // "0" or "1"
            
            if (mainData.ContainsKey(key))
            {
                string mainState = mainData[key];
                
                if (workshopState != mainState)
                {
                    mismatchCount++;
                    string workshopStr = workshopState == "1" ? "ENABLED" : "DISABLED";
                    string mainStr = mainState == "1" ? "ENABLED" : "DISABLED";
                    differences.Add($"{key}: Workshop={workshopStr}, Main={mainStr}");
                }
                else
                {
                    matchCount++;
                }
            }
            else
            {
                differences.Add($"{key}: EXISTS in Workshop but NOT FOUND in Main!");
            }
        }
        
        // 检查Main中有但Workshop中没有的
        foreach (var kvp in mainData)
        {
            if (!workshopData.ContainsKey(kvp.Key))
            {
                differences.Add($"{kvp.Key}: EXISTS in Main but NOT FOUND in Workshop!");
            }
        }
        
        // 输出结果
        Debug.Log($"[StatusCompare] Matching blocks: {matchCount}");
        Debug.Log($"[StatusCompare] Mismatched blocks: {mismatchCount}");
        
        if (differences.Count > 0)
        {
            Debug.LogWarning($"[StatusCompare] ===== DIFFERENCES FOUND ({differences.Count}) =====");
            foreach (string diff in differences)
            {
                Debug.LogWarning($"[StatusCompare] {diff}");
            }
        }
        else
        {
            Debug.Log($"[StatusCompare] ✓ All blocks match perfectly!");
        }
        
        Debug.Log($"[StatusCompare] ==========================================");
    }
    
    Dictionary<string, string> LoadStatusFile(string filePath)
    {
        Dictionary<string, string> data = new Dictionary<string, string>();
        
        string[] lines = File.ReadAllLines(filePath);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length != 4) continue;
            
            // 格式：层级(1位) + 方块号(2位) + 状态(1位)
            int layer = int.Parse(line.Substring(0, 1));
            int blockNum = int.Parse(line.Substring(1, 2));
            string state = line.Substring(3, 1);
            
            string key = $"Layer{layer}/Cube{blockNum:D2}";
            data[key] = state;
        }
        
        return data;
    }
}
