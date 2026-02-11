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
    
    // 只保存摄像头角度（不自动恢复绝对位置，除非MAINPOS）
    private float savedCameraYRotation = 0f;
    private bool hasCameraData = false;

    // Main-scene position persistence (record on *exit* from Main, restore on return)
    private Vector3 savedMainPosition = Vector3.zero;
    private Quaternion savedMainRotation = Quaternion.identity;
    private bool hasMainPosition = false;

    // 当前实时位置（安全时写入RocketStatus.txt为POS: —— 不会自动覆盖 MAINPOS 恢复流程）
    private Vector3 savedCurrentPosition = Vector3.zero;
    private Quaternion savedCurrentRotation = Quaternion.identity;
    private bool hasCurrentPosition = false;

    // 事件状态：火箭是否已经发生过爆炸（影响下一次返回Main的复位策略）
    private bool hasExploded = false;

    // 在正在执行爆炸恢复（黑屏/放置）期间的保护标志，防止重复触发
    private bool isRecoveringFromExplosion = false;

    // 当需要进行程序化写盘（例如 FactoryReset）时，抑制 SaveStateToFile 中的自动 Main-position 捕获
    private bool suppressAutoMainCapture = false;
    
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
        // 订阅场景加载/卸载事件
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
    }
    
    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }
    
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 使用协程异步应用状态，避免卡顿
        StartCoroutine(ApplyRocketStateAsync());
        
        // 如果在任一场景有保存的摄像头角度，则恢复（不恢复Rocket位置）
        if ((scene.name == "Main" || scene.name == "Workshop") && hasCameraData)
        {
            StartCoroutine(ApplyCameraAngleAsync());
        }

            // 如果回到Main场景且存在已记录的Main位置（来自离开时的保存），尝试恢复（精确还原原位）
        if (scene.name == "Main" && hasMainPosition)
        {
            StartCoroutine(ApplyMainPositionAsync());
        }
    }

    // 当场景被卸载时回调（用于在离开Main时保存主场景的Rocket位置）
    void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
    {
        if (scene.name == "Main")
        {
            // 在Main被卸载前，记录Rocket的当前位置（但仅在安全时保存）
            SaveMainScenePositionIfSafe();
        }
    }

    /// <summary>
    /// 在离开Main场景前，若位置安全则保存Main场景的位置/旋转（供稍后返回时恢复）
    /// </summary>
    // 尝试捕获Main位置（只设置内存标志，不触发写盘）
    // 捕获当前Rocket的位置用于写入POS（不会影响MAINPOS逻辑）
    bool TryCaptureCurrentPositionForSave()
    {
        GameObject rocket = GameObject.Find(rocketContainerName);
        if (rocket == null)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                var t = root.transform.Find(rocketContainerName);
                if (t != null)
                {
                    rocket = t.gameObject;
                    break;
                }
            }
        }
        if (rocket == null) return false;

        Vector3 pos = rocket.transform.position;
        Quaternion rot = rocket.transform.rotation;

        // 使用与Main相同的安全检查
        if (!IsMainPositionSafe(pos)) return false;

        // 抬高到地面之上以避免穿地
        RaycastHit hit;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out hit, 5f))
        {
            pos.y = Mathf.Max(pos.y, hit.point.y + 0.35f);
        }

        savedCurrentPosition = pos;
        savedCurrentRotation = rot;
        hasCurrentPosition = true;
        return true;
    }

    bool TryCaptureMainPosition(bool force = false)
    {
        // 保留原有Main捕获逻辑（用于MAINPOS）
        GameObject rocket = GameObject.Find(rocketContainerName);
        // 回退：在场景中查找（包含可能被禁用的对象）
        if (rocket == null)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
            {
                var t = root.transform.Find(rocketContainerName);
                if (t != null)
                {
                    rocket = t.gameObject;
                    break;
                }
            }
        }

        if (rocket == null)
        {
            Debug.LogWarning("[RocketStateManager] TryCaptureMainPosition: Rocket not found in scene");
            return false;
        }

        Vector3 pos = rocket.transform.position;
        Quaternion rot = rocket.transform.rotation;

        // 强制模式下：直接记录当前变换（用户要求“从原位开始”），不做自动抬高/安全替换
        if (force)
        {
            savedMainPosition = pos;
            savedMainRotation = rot;
            hasMainPosition = true;
            Debug.Log($"[RocketStateManager] TryCaptureMainPosition(force): captured exact transform => {savedMainPosition}");
            return true;
        }

        // 非强制模式下保留原有的安全性调整逻辑
        // 若位置高度非常接近地面，向上微调（避免放进地面）
        RaycastHit hit;
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out hit, 5f))
        {
            float groundY = hit.point.y;
            if (pos.y < groundY + 0.4f)
            {
                pos.y = groundY + 0.4f; // 抬高到地面之上
                Debug.Log($"[RocketStateManager] TryCaptureMainPosition: adjusted Y to ground ({groundY:F2}) => {pos.y:F2}");
            }
        }

        if (!IsMainPositionSafe(pos))
        {
            Debug.LogWarning($"[RocketStateManager] TryCaptureMainPosition: position unsafe (y={pos.y:F2})");
            return false;
        }

        savedMainPosition = pos;
        savedMainRotation = rot;
        hasMainPosition = true;
        return true;
    }

    void SaveMainScenePositionIfSafe()
    {
        // 强制在离开 Main 时捕获并持久化当前变换（响应需求：不要回到“安全位置”，而是回到原位）
        bool captured = TryCaptureMainPosition(force: true);
        if (!captured)
        {
            Debug.LogWarning("[RocketStateManager] SaveMainScenePositionIfSafe: failed to capture MAIN position (rocket missing)");
            return;
        }

        Debug.Log($"[RocketStateManager] Saved MAIN position (exact) for later restore: {savedMainPosition}");
        SaveStateToFile();
    }

    /// <summary>
    /// 简单的安全检查：高度合理且不与LaunchingPad重叠
    /// </summary>
    bool IsMainPositionSafe(Vector3 pos)
    {
        // 基本高度检查
        if (pos.y < 0.2f || pos.y > 500f) return false;

        // 向下射线确认地面高度一致性
        RaycastHit groundHit;
        if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out groundHit, 10f))
        {
            float dy = Mathf.Abs(pos.y - groundHit.point.y);
            if (dy > 1.5f) // 与地面相差过大
                return false;
        }

        // 检查是否与名为或标记为LaunchingPad的碰撞体重叠或距离太近
        Collider[] cols = Physics.OverlapSphere(pos, 0.8f);
        foreach (var c in cols)
        {
            if (c == null || c.gameObject == null) continue;
            string n = c.gameObject.name.ToLower();
            if (n.Contains("launchingpad") || c.gameObject.CompareTag("LaunchingPad"))
            {
                return false;
            }
        }

        // 最后检查周围是否有大量遮挡物（避免卡在建筑下）
        int hitCount = 0;
        Vector3[] checks = { Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        foreach (var dir in checks)
        {
            if (Physics.Raycast(pos, dir, 1.0f)) hitCount++;
        }
        if (hitCount >= 4) return false;

        return true;
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
        
        // 不保存Rocket的绝对位置 — 仅保存摄像头角度（若存在）
        CamaraFollow cam = FindObjectOfType<CamaraFollow>();
        if (cam != null)
        {
            savedCameraYRotation = cam.GetCurrentAngleIndex() * 90f;
            hasCameraData = true;
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
        
        // 如果没有保存的方块状态：
        // - 如果存在已记录的Main位置（用户离开Main时曾保存），不要将所有方块打开；
        // - 只有在既没有方块状态也没有Main位置时，才初始化为单个方块
        if (blockStates.Count == 0)
        {
            if (hasMainPosition)
            {
                Debug.Log("[RocketStateManager] 仅存在Main位置数据，跳过初始化所有方块");
                // 继续（不要调用 InitializeToSingleBlock）
            }
            else
            {
                Debug.Log("[RocketStateManager] 配置文件为空，初始化为单个方块");
                InitializeToSingleBlock(rocket);
                yield break;
            }
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

    // 本类局部的光照刷新（与 SceneTransitionManager 中的实现一致）
    void UpdateLighting()
    {
        // 强制更新光照和天空盒
        DynamicGI.UpdateEnvironment();
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
    }

    // 将 Rocket 放置到目标位置并尝试修复任何穿模/重叠问题
    // - 会短暂将父 Rigidbody 设置为 isKinematic 以安全移动
    // - 使用 Physics.ComputePenetration 尝试移出重叠体
    // - 若无法通过穿透修正清除重叠，会向上逐步抬高直到可用或达到上限
    IEnumerator PlaceRocketAtAndResolve(GameObject rocket, Vector3 targetPos, Quaternion targetRot)
    {
        if (rocket == null) yield break;

        const int maxPenetrationIters = 6;
        const float upwardNudgeStep = 0.2f;
        const float maxUpwardNudge = 3.0f;
        const float smallEpsilon = 0.01f;

        Rigidbody parentRb = rocket.GetComponent<Rigidbody>();
        bool hadKinematic = false;
        if (parentRb != null)
        {
            hadKinematic = parentRb.isKinematic;
            parentRb.isKinematic = true; // 先停用物理以安全移动
            parentRb.velocity = Vector3.zero;
            parentRb.angularVelocity = Vector3.zero;
        }

        // 确保所有子碰撞体启用以便检测
        Collider[] myColliders = rocket.GetComponentsInChildren<Collider>(true);

        // 直接移动到目标位置/朝向（基础放置）
        rocket.transform.position = targetPos;
        rocket.transform.rotation = targetRot;
        Physics.SyncTransforms();
        yield return null; // 让物理系统同步一次

        // 尝试通过 ComputePenetration 修正穿透
        bool separated = false;
        for (int iter = 0; iter < maxPenetrationIters; iter++)
        {
            Vector3 totalSeparation = Vector3.zero;
            int overlapCount = 0;

            // 更新Collider bounds并检测与外部碰撞体的穿透
            foreach (var myCol in myColliders)
            {
                if (myCol == null || myCol.isTrigger) continue;

                // 使用简单的 Overlap 检测潜在的重叠对象
                Collider[] overlaps = Physics.OverlapBox(myCol.bounds.center, myCol.bounds.extents, myCol.transform.rotation,
                                                        ~0, QueryTriggerInteraction.Ignore);

                foreach (var oc in overlaps)
                {
                    if (oc == null) continue;
                    // 忽略自身的碰撞体
                    bool isSelf = false;
                    foreach (var mc in myColliders) if (mc == oc) { isSelf = true; break; }
                    if (isSelf) continue;

                    // 计算穿透向量
                    Vector3 dir; float dist;
                    bool ok = Physics.ComputePenetration(myCol, myCol.transform.position, myCol.transform.rotation,
                                                         oc, oc.transform.position, oc.transform.rotation,
                                                         out dir, out dist);
                    if (ok && dist > 0f)
                    {
                        totalSeparation += dir * (dist + smallEpsilon);
                        overlapCount++;
                    }
                }
            }

            if (overlapCount == 0)
            {
                separated = true;
                break;
            }

            // 将火箭沿合成向量移动（尝试解开穿透）
            if (totalSeparation.sqrMagnitude > 0f)
            {
                rocket.transform.position += totalSeparation / Mathf.Max(1, overlapCount);
                Physics.SyncTransforms();
                yield return null;
                continue;
            }
            else
            {
                break;
            }
        }

        // 如果仍有重叠，则尝试向上小步抬高直到不重叠
        if (!separated)
        {
            float nudge = upwardNudgeStep;
            bool cleared = false;
            while (nudge <= maxUpwardNudge)
            {
                rocket.transform.position = targetPos + Vector3.up * nudge;
                Physics.SyncTransforms();
                yield return null;

                bool anyOverlap = false;
                foreach (var mc in myColliders)
                {
                    if (mc == null || mc.isTrigger) continue;
                    Collider[] overlaps = Physics.OverlapBox(mc.bounds.center, mc.bounds.extents, mc.transform.rotation,
                                                            ~0, QueryTriggerInteraction.Ignore);
                    foreach (var oc in overlaps)
                    {
                        if (oc == null) continue;
                        bool isSelf = false;
                        foreach (var mm in myColliders) if (mm == oc) { isSelf = true; break; }
                        if (!isSelf) { anyOverlap = true; break; }
                    }
                    if (anyOverlap) break;
                }

                if (!anyOverlap)
                {
                    cleared = true;
                    break;
                }

                nudge += upwardNudgeStep;
            }

            if (!cleared && !separated)
            {
                // 无法完全清除重叠：将火箭再抬高一个默认高度并记录警告
                rocket.transform.position = targetPos + Vector3.up * (maxUpwardNudge + 0.5f);
                Physics.SyncTransforms();
                Debug.LogWarning("[RocketStateManager] PlaceRocketAtAndResolve: could not fully resolve overlaps — applied upward fallback");
                yield return null;
            }
        }

        // 最终清理：清零速度并恢复物理状态
        if (parentRb != null)
        {
            parentRb.velocity = Vector3.zero;
            parentRb.angularVelocity = Vector3.zero;
            parentRb.isKinematic = hadKinematic; // 恢复原始 kinematic 状态
            parentRb.WakeUp();
        }

        // 清理子刚体速度（如果存在）
        var childRbs = rocket.GetComponentsInChildren<Rigidbody>(true);
        foreach (var cr in childRbs)
        {
            cr.velocity = Vector3.zero;
            cr.angularVelocity = Vector3.zero;
        }

        yield return null;
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
        
        // 清除任何已记录的Main位置与当前POS（避免恢复到过时/错误的位置)
        hasMainPosition = false;
        hasCurrentPosition = false;
        
        // 保存这个默认状态（仅方块/摄像头/旋转信息）
        SaveRocketState();
    }
    
    // 保存状态到文件（格式：层级+方块号+启用状态）
    void SaveStateToFile()
    {
        try
        {
            // 如果当前在Main场景，且未被抑制，则强制捕获Main位置（按用户要求记录原位）
            if (!suppressAutoMainCapture && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main")
            {
                TryCaptureMainPosition(force: true);
            }

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

            // 在写盘前尝试捕获当前安全位置（用于POS:）
            TryCaptureCurrentPositionForSave();

            // 写入摄像头角度（如果存在）
            if (hasCameraData)
            {
                lines.Add($"CAM:{savedCameraYRotation:F1}");
            }

            // 写入Main场景位置/旋转（仅当用户离开Main时已安全记录）
            if (hasMainPosition)
            {
                Vector3 p = savedMainPosition;
                Vector3 r = savedMainRotation.eulerAngles;
                lines.Add($"MAINPOS:{p.x:F3},{p.y:F3},{p.z:F3},{r.x:F3},{r.y:F3},{r.z:F3}");
            }

            // 写入当前安全位置（POS: 可供外部调用/调试使用；不会替代 MAINPOS 恢复逻辑）
            if (hasCurrentPosition)
            {
                Vector3 cp = savedCurrentPosition;
                Vector3 cr = savedCurrentRotation.eulerAngles;
                lines.Add($"POS:{cp.x:F3},{cp.y:F3},{cp.z:F3},{cr.x:F3},{cr.y:F3},{cr.z:F3}");
            }

            // 写入爆炸标记（如果发生过）
            if (hasExploded)
            {
                lines.Add($"EXPLODED:1");
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
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    // 摄像头行
                    if (line.StartsWith("CAM:"))
                    {
                        float.TryParse(line.Substring(4), out savedCameraYRotation);
                        hasCameraData = true;
                        hasValidContent = true;
                        continue;
                    }

                    // Main-scene位置行
                    if (line.StartsWith("MAINPOS:"))
                    {
                        string[] parts = line.Substring(8).Split(',');
                        if (parts.Length == 6)
                        {
                            float px, py, pz, rx, ry, rz;
                            if (float.TryParse(parts[0], out px) && float.TryParse(parts[1], out py) && float.TryParse(parts[2], out pz)
                                && float.TryParse(parts[3], out rx) && float.TryParse(parts[4], out ry) && float.TryParse(parts[5], out rz))
                            {
                                savedMainPosition = new Vector3(px, py, pz);
                                savedMainRotation = Quaternion.Euler(rx, ry, rz);
                                hasMainPosition = true;
                                hasValidContent = true;
                            }
                        }
                        continue;
                    }

                    // POS: 当前安全位置行（供外部调用/调试）
                    if (line.StartsWith("POS:"))
                    {
                        string[] parts = line.Substring(4).Split(',');
                        if (parts.Length == 6)
                        {
                            float px, py, pz, rx, ry, rz;
                            if (float.TryParse(parts[0], out px) && float.TryParse(parts[1], out py) && float.TryParse(parts[2], out pz)
                                && float.TryParse(parts[3], out rx) && float.TryParse(parts[4], out ry) && float.TryParse(parts[5], out rz))
                            {
                                savedCurrentPosition = new Vector3(px, py, pz);
                                savedCurrentRotation = Quaternion.Euler(rx, ry, rz);
                                hasCurrentPosition = true;
                                hasValidContent = true;
                            }
                        }
                        continue;
                    }

                    // EXPLODED 标记（指示火箭在上一次会话中发生了爆炸）
                    if (line.StartsWith("EXPLODED:"))
                    {
                        string v = line.Substring(9).Trim();
                        hasExploded = (v == "1");
                        hasValidContent = true;
                        continue;
                    }

                    // 方块行（格式4字符）
                    if (line.Length != 4) continue;
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

    // 在外部（例如 Movement）调用：标记Rocket已爆炸（影响下一次返回Main的恢复策略）
    public static void MarkExploded()
    {
        if (instance != null)
        {
            instance.hasExploded = true;
            // 清除已有的 MAINPOS，下一次返回时应使用安全点而非原位
            instance.hasMainPosition = false;
            Debug.Log("[RocketStateManager] Marked as exploded (will fallback to nearest safe point on next Main load)");
        }
    }

    // 由外部调用以标记爆炸并精确记录爆炸发生时的变换（确保恢复搜索有可靠的起点）
    public static void MarkExplodedAt(Vector3 pos, Quaternion rot)
    {
        if (instance != null)
        {
            instance.hasExploded = true;
            instance.savedCurrentPosition = pos;
            instance.savedCurrentRotation = rot;
            instance.hasCurrentPosition = true;
            // 清除 MAINPOS：下一次返回应采用安全点策略
            instance.hasMainPosition = false;
            instance.SaveStateToFile();
            Debug.Log($"[RocketStateManager] Marked exploded at position {pos} and persisted POS");
        }
    }

    // 触发：爆炸后等待3秒 -> 黑屏1秒 -> 放置到安全点 -> 解除黑屏（公开API，外部可调用）
    public static void TriggerExplosionRecoverySequence()
    {
        if (instance == null) return;
        if (instance.isRecoveringFromExplosion) return;
        instance.StartCoroutine(instance.ExplosionRecoveryScreenAndPlacement());
    }

    // 爆炸恢复序列（内部协程）
    IEnumerator ExplosionRecoveryScreenAndPlacement()
    {
        if (isRecoveringFromExplosion) yield break;
        isRecoveringFromExplosion = true;

        // 等待 3 秒（玩家可见的延迟）
        yield return new WaitForSeconds(3f);

        // 创建黑屏 Canvas 并淡入
        GameObject fadeObj = new GameObject("RS_ExplosionFade");
        Canvas c = fadeObj.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 99999;
        fadeObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        fadeObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        GameObject imgObj = new GameObject("FadeImage");
        imgObj.transform.SetParent(fadeObj.transform, false);
        var img = imgObj.AddComponent<UnityEngine.UI.Image>();
        img.color = Color.black;
        var rt = img.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        CanvasGroup cg = imgObj.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        float fadeDur = 0.2f;
        for (float t = 0f; t < fadeDur; t += Time.unscaledDeltaTime)
        {
            cg.alpha = Mathf.Clamp01(t / fadeDur);
            yield return null;
        }
        cg.alpha = 1f;

        // 在黑屏期间将 Rocket 放到安全点（复用 ApplyMainPositionAsync 的逻辑）
        yield return StartCoroutine(ApplyMainPositionAsync());

        // 保持黑屏 1 秒（不受 timeScale 影响）
        yield return new WaitForSecondsRealtime(1f);

        // 淡出黑屏
        for (float t = 0f; t < fadeDur; t += Time.unscaledDeltaTime)
        {
            cg.alpha = 1f - Mathf.Clamp01(t / fadeDur);
            yield return null;
        }
        cg.alpha = 0f;

        GameObject.Destroy(fadeObj);
        isRecoveringFromExplosion = false;
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
                Debug.Log("[RocketStateManager] 已重置为单个方块并保存（位置数据已清空）");
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
            // 简单清空（保留用于快速调试的行为）
            instance.blockStates.Clear();
            instance.hasCameraData = false;
            instance.hasMainPosition = false;
            instance.hasCurrentPosition = false;
            instance.hasExploded = false;
            
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
            
            GameObject rocket = GameObject.Find(instance.rocketContainerName);
            if (rocket != null)
            {
                instance.InitializeToSingleBlock(rocket);
                Debug.Log("[RocketStateManager] 已清空配置文件并重新初始化为单个方块");
            }
        }
    }

    // 公共方法：执行出厂重置——删除/重写所有保存文件，恢复 RocketPosition.txt 中的默认位姿/摄像头，并初始化为单个方块
    public static void FactoryResetToDefaults()
    {
        if (instance == null) return;

        Debug.Log("[RocketStateManager] Factory reset: restoring defaults and wiping user config...");

        // 抑制自动捕获，避免在写盘时被当前场景的对象覆盖
        instance.suppressAutoMainCapture = true;

        // 清空内存状态
        instance.blockStates.Clear();
        instance.hasCameraData = false;
        instance.hasMainPosition = false;
        instance.hasCurrentPosition = false;
        instance.hasExploded = false;

        // 读取默认位姿（来自 RocketPosition.txt）
        string posPath = Path.Combine(Application.dataPath, "RocketPosition.txt");
        Vector3 defaultPos = Vector3.zero;
        Quaternion defaultRot = Quaternion.identity;
        float defaultCam = 0f;

        try
        {
            if (File.Exists(posPath))
            {
                string[] lines = File.ReadAllLines(posPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("POS:"))
                    {
                        var parts = line.Substring(4).Split(',');
                        if (parts.Length >= 3)
                        {
                            float x, y, z;
                            if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y) && float.TryParse(parts[2], out z))
                            {
                                defaultPos = new Vector3(x, y, z);
                            }
                        }
                    }
                    else if (line.StartsWith("ROT:"))
                    {
                        var parts = line.Substring(4).Split(',');
                        if (parts.Length >= 3)
                        {
                            float rx, ry, rz;
                            if (float.TryParse(parts[0], out rx) && float.TryParse(parts[1], out ry) && float.TryParse(parts[2], out rz))
                            {
                                defaultRot = Quaternion.Euler(rx, ry, rz);
                            }
                        }
                    }
                    else if (line.StartsWith("CAM:"))
                    {
                        float.TryParse(line.Substring(4), out defaultCam);
                        instance.hasCameraData = true;
                        instance.savedCameraYRotation = defaultCam;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RocketStateManager] Failed to read RocketPosition.txt: {e.Message}");
        }

        // 应用到场景中的 Rocket（如果存在）并初始化为单个方块
        GameObject rocket = GameObject.Find(instance.rocketContainerName);
        if (rocket != null)
        {
            rocket.transform.position = defaultPos;
            rocket.transform.rotation = defaultRot;
            instance.InitializeToSingleBlock(rocket);
        }

        // 删除/重写所有相关保存文件（彻底出厂化）
        string[] filesToWipe = new string[] {
            instance.saveFilePath, // RocketStatus.txt
            Path.Combine(Application.dataPath, "RocketStatusMain.txt")
        };

        foreach (var f in filesToWipe)
        {
            try
            {
                if (File.Exists(f)) File.WriteAllText(f, "");
                Debug.Log($"[RocketStateManager] Wiped: {f}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RocketStateManager] Failed to wipe {f}: {e.Message}");
            }
        }

        // 将默认 MAINPOS 和 CAM 写回到主保存文件，保持一致性
        instance.savedMainPosition = defaultPos;
        instance.savedMainRotation = defaultRot;
        instance.hasMainPosition = true;
        instance.hasCameraData = instance.hasCameraData;

        instance.SaveStateToFile();

        // 完成，恢复自动捕获行为
        instance.suppressAutoMainCapture = false;

        Debug.Log("[RocketStateManager] Factory reset complete.");
    }
    
    // ========== 位置和摄像头方向保存/恢复 ==========
    
    // NOTE: Rocket absolute position is no longer persisted across scenes by design.
    // Camera angle (Y) and block edit state remain persisted.
    
    /// <summary>
    /// 异步应用保存的Rocket位置和摄像头角度
    /// </summary>

    
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
    /// 将摄像头角度设置为之前保存的角度（协程）
    /// </summary>
    IEnumerator RestoreCameraAngle(CamaraFollow cameraFollow)
    {
        // 等待一帧以确保CamaraFollow初始化完成
        yield return null;

        int targetIndex = Mathf.RoundToInt(savedCameraYRotation / 90f) % 4;
        cameraFollow.SetCameraAngle(targetIndex);

        Debug.Log($"[RocketStateManager] RestoreCameraAngle: set camera to index {targetIndex} ({savedCameraYRotation}°)");
    }
    
    /// <summary>
    /// 异步恢复Main场景保存的位置（仅当之前离开Main时已安全记录）
    /// </summary>
    IEnumerator ApplyMainPositionAsync()
    {
        yield return new WaitForSeconds(0.1f);

        GameObject rocket = GameObject.Find(rocketContainerName);
        if (rocket == null)
        {
            Debug.LogWarning("[RocketStateManager] ApplyMainPositionAsync: 未找到Rocket");
            yield break;
        }

        // 如果火箭在上一次会话中发生过爆炸，则优先放到最近的带有 'Friendly' tag 的物体上方；若无则进行环形搜索再回退到LaunchingPad
        if (hasExploded)
        {
            Vector3 origin = hasCurrentPosition ? savedCurrentPosition : (hasMainPosition ? savedMainPosition : rocket.transform.position);
            Quaternion targetRot = hasCurrentPosition ? savedCurrentRotation : (hasMainPosition ? savedMainRotation : Quaternion.identity);

            Debug.Log("[RocketStateManager] Detected previous explosion — attempting to place above nearest 'Friendly' object first");

            // 1) 优先查找带 Friendly tag 的对象
            GameObject[] friendlies = GameObject.FindGameObjectsWithTag("Friendly");
            GameObject best = null;
            float bestDist = float.MaxValue;
            foreach (var f in friendlies)
            {
                float d = Vector3.SqrMagnitude((f.transform.position) - origin);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = f;
                }
            }

            if (best != null)
            {
                Vector3 place = best.transform.position + Vector3.up * 0.6f;
                yield return StartCoroutine(PlaceRocketAtAndResolve(rocket, place, targetRot));

                // 恢复可玩性（移除子刚体、恢复父刚体、重置输入等）
                var mv = rocket.GetComponent<Movement>();
                if (mv != null) mv.RecoverFromExplosion();

                savedMainPosition = rocket.transform.position;
                savedMainRotation = rocket.transform.rotation;
                hasMainPosition = true;

                Debug.Log($"[RocketStateManager] Explosion-fallback: placed Rocket above nearest Friendly '{best.name}' at {savedMainPosition}");

                // 清理 exploded 标记并持久化新的 MAINPOS
                hasExploded = false;
                SaveStateToFile();
                yield break;
            }

            Debug.Log("[RocketStateManager] No Nearby 'Friendly' found — falling back to radial safe-point search");

            // 2) 环形搜索（旧行为）
            bool found = false;
            float[] radii = {0.5f, 1f, 2f, 5f, 10f};
            for (int ri = 0; ri < radii.Length && !found; ri++)
            {
                float r = radii[ri];
                int steps = Mathf.Clamp(12 * (ri + 1), 12, 72);
                for (int a = 0; a < steps; a++)
                {
                    float ang = (a / (float)steps) * Mathf.PI * 2f;
                    Vector3 candidate = origin + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;

                    // 抬高至地面
                    RaycastHit gh;
                    if (Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out gh, 10f))
                    {
                        candidate.y = gh.point.y + 0.4f;
                    }

                    if (IsMainPositionSafe(candidate))
                    {
                        yield return StartCoroutine(PlaceRocketAtAndResolve(rocket, candidate, targetRot));

                        var mv2 = rocket.GetComponent<Movement>();
                        if (mv2 != null) mv2.RecoverFromExplosion();

                        Debug.Log($"[RocketStateManager] Explosion-fallback: placed Rocket at nearest safe point: {rocket.transform.position}");
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                // 最后手段：尝试放到LaunchingPad上方
                GameObject pad = GameObject.FindWithTag("LaunchingPad");
                if (pad != null)
                {
                    Vector3 p = pad.transform.position + Vector3.up * 0.6f;
                    yield return StartCoroutine(PlaceRocketAtAndResolve(rocket, p, Quaternion.identity));

                    var mv3 = rocket.GetComponent<Movement>();
                    if (mv3 != null) mv3.RecoverFromExplosion();

                    Debug.LogWarning($"[RocketStateManager] Explosion-fallback: no nearby safe spot found — placed on LaunchingPad at {rocket.transform.position}");

                    // 持久化这个位置为 MAINPOS
                    savedMainPosition = rocket.transform.position;
                    savedMainRotation = rocket.transform.rotation;
                    hasMainPosition = true;
                    hasExploded = false;
                    SaveStateToFile();
                    yield break;
                }
                else
                {
                    Debug.LogWarning("[RocketStateManager] Explosion-fallback: no safe spot or LaunchingPad found — leaving Rocket at current transform");
                }
            }

            // 若成功通过环形搜索放置，则持久化 MAINPOS 并清除 exploded 标记
            if (found)
            {
                hasExploded = false;
                hasMainPosition = true;
                savedMainPosition = rocket.transform.position;
                savedMainRotation = rocket.transform.rotation;
                SaveStateToFile();
            }

            yield break;
        }

        // 如果没有 MAINPOS，但有最近的 POS，也直接原位恢复（但在原位恢复时先做穿透修正）
        if (!hasMainPosition)
        {
            if (hasCurrentPosition)
            {
                Debug.Log("[RocketStateManager] MAINPOS 缺失 — 直接使用最近的 POS 原位恢复（含穿透修正）");
                yield return StartCoroutine(PlaceRocketAtAndResolve(rocket, savedCurrentPosition, savedCurrentRotation));

                var mv4 = rocket.GetComponent<Movement>();
                if (mv4 != null) mv4.RecoverFromExplosion();

                Debug.Log($"[RocketStateManager] 已恢复Main场景位置（来自 POS）: {rocket.transform.position}");
                yield break;
            }
            else
            {
                yield break;
            }
        }

        // 直接将Rocket放回保存的原位（遵从用户要求：不进行安全位置回退或附近搜索），但先做穿透修正
        yield return StartCoroutine(PlaceRocketAtAndResolve(rocket, savedMainPosition, savedMainRotation));

        var mv5 = rocket.GetComponent<Movement>();
        if (mv5 != null) mv5.RecoverFromExplosion();

        Debug.Log($"[RocketStateManager] 已精确恢复Main场景原位: {rocket.transform.position}");
    }

    /// <summary>
    /// <summary>
    /// 从文件加载位置
    /// </summary>

    
    /// <summary>
    /// 清空位置文件
    /// </summary>

}
