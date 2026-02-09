using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlockEditor : MonoBehaviour
{
    [Header("编辑设置")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float rayDistance = 100f;
    [SerializeField] private LayerMask blockLayer = ~0;          // 方块所在的层
    [SerializeField] private float blockSize = 1f;               // 方块大小（用于计算相邻位置）
    
    [Header("方块管理")]
    [SerializeField] private Transform blocksContainer;          // 所有方块的父物体
    
    [Header("编辑音效")]
    [SerializeField] private AudioClip[] editSounds;             // 编辑音效（4个音频随机播放）
    [SerializeField] private AudioClip[] denySounds;             // 拒绝操作音效（破坏最后方块/范围外放置时播放）
    
    [Header("视觉反馈")]
    [SerializeField] private Color hoverTint = new Color(0.7f, 0.7f, 0.7f, 1f);  // Hover时的灰色
    [SerializeField] private Color clickTint = new Color(1f, 1f, 0.5f, 1f);      // 点击时的闪烁颜色
    [SerializeField] private float clickAnimDuration = 0.2f;                      // 点击动画时长
    [SerializeField] private float shakeAmount = 0.1f;                            // 摇晃幅度
    [SerializeField] private float shakeDuration = 0.3f;                          // 摇晃时长
    
    // 存储所有方块的状态（Layer名/Cube名 -> 方块GameObject）
    private Dictionary<string, GameObject> blockDictionary = new Dictionary<string, GameObject>();
    
    // Hover状态
    private GameObject hoveredBlock;
    private Dictionary<Renderer, Color[]> originalColors = new Dictionary<Renderer, Color[]>();
    
    // 音频播放器
    private AudioSource audioSource;
    
    // 摇晃状态
    private bool isShaking = false;
    private GameObject shakingBlock = null;
    private Dictionary<GameObject, Vector3> blockOriginalPositions = new Dictionary<GameObject, Vector3>();
    
    void Start()
    {
        // 如果没有指定摄像机，使用主摄像机
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        // 如果没有指定容器，尝试查找Rocket
        if (blocksContainer == null)
        {
            GameObject rocket = GameObject.Find("Rocket");
            if (rocket != null)
            {
                blocksContainer = rocket.transform;
            }
        }
        
        // 获取或添加AudioSource组件
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 初始化方块字典
        InitializeBlockDictionary();
        
        // Debug.Log($"[BlockEditor] Initialized with {blockDictionary.Count} blocks");
    }
    
    void OnEnable()
    {
        // 订阅场景加载事件，确保切换场景后重新初始化
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 场景加载后重新查找容器并初始化
        if (blocksContainer == null)
        {
            GameObject rocket = GameObject.Find("Rocket");
            if (rocket != null)
            {
                blocksContainer = rocket.transform;
            }
        }
        
        // 延迟初始化，确保RocketStateManager先应用状态
        Invoke("InitializeBlockDictionary", 0.3f);
    }
    
    void InitializeBlockDictionary()
    {
        blockDictionary.Clear();
        
        // 如果指定了容器，从容器中查找所有方块
        if (blocksContainer != null)
        {
            // 遍历每个Layer
            for (int layer = 1; layer <= 5; layer++)
            {
                Transform layerTransform = blocksContainer.Find($"Layer{layer}");
                if (layerTransform == null) continue;
                
                // 获取该Layer下所有Cube
                foreach (Transform child in layerTransform)
                {
                    if (child.name.Contains("Cube"))
                    {
                        string key = $"Layer{layer}/{child.name}";
                        blockDictionary[key] = child.gameObject;
                    }
                }
            }
            
            // Debug.Log($"[BlockEditor] Initialized {blockDictionary.Count} blocks");
        }
        // else
        // {
        //     Debug.LogWarning("[BlockEditor] No blocksContainer assigned!");
        // }
    }
    
    void Update()
    {
        // 检测hover
        HandleHover();
        
        // 检测鼠标左键点击 - 关闭方块
        if (Input.GetMouseButtonDown(0))
        {
            HandleLeftClick();
        }
        
        // 检测鼠标右键点击 - 打开相邻方块
        if (Input.GetMouseButtonDown(1))
        {
            HandleRightClick();
        }
    }
    
    void HandleLeftClick()
    {
        // 如果正在摇晃，忽略点击
        if (isShaking)
        {
            return;
        }
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        // 获取第一个可见的方块
        if (GetFirstVisibleHit(ray, out hit))
        {
            GameObject hitBlock = hit.collider.gameObject;
            
            // 检查是否是方块（在Layer下）
            Transform layer = hitBlock.transform.parent;
            if (layer != null && layer.name.StartsWith("Layer"))
            {
                // 检查当前有多少个方块是开启的
                int enabledBlockCount = CountEnabledBlocks();
                
                // 如果只剩1个方块，不允许关闭，播放摇晃动画和拒绝音效
                if (enabledBlockCount <= 1)
                {
                    // Debug.Log($"[BlockEditor] Cannot disable last block! Shaking {layer.name}/{hitBlock.name}");
                    PlayDenySound();
                    StartCoroutine(ShakeBlock(hitBlock, ray.direction));
                    return;
                }
                
                // 直接禁用这个方块
                hitBlock.SetActive(false);
                
                // 显式禁用Renderer确保视觉同步
                Renderer[] renderers = hitBlock.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    r.enabled = false;
                }
                
                // Debug.Log($"[BlockEditor] Disabled {layer.name}/{hitBlock.name}");
                
                // 播放编辑音效
                PlayEditSound();
                
                // 清除hover状态
                if (hoveredBlock == hitBlock)
                {
                    hoveredBlock = null;
                }
            }
        }
    }
    
    void HandleRightClick()
    {
        // 如果正在摇晃，忽略点击
        if (isShaking)
        {
            return;
        }
        
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        // 获取第一个可见的方块
        if (GetFirstVisibleHit(ray, out hit))
        {
            GameObject hitBlock = hit.collider.gameObject;
            
            // 检查是否是方块
            Transform layer = hitBlock.transform.parent;
            if (layer != null && layer.name.StartsWith("Layer"))
            {
                // 获取碰撞点的法线方向
                Vector3 normal = hit.normal;
                Vector3 gridDirection = GetGridDirection(normal);
                
                // 计算目标位置（相邻方块应该在的位置）
                Vector3 currentPos = hitBlock.transform.position;
                Vector3 targetWorldPos = currentPos + gridDirection * blockSize;
                
                // Debug.Log($"[BlockEditor] Right click: current={currentPos}, direction={gridDirection}, target={targetWorldPos}");
                
                // 在所有Layer中查找最接近目标位置的方块
                GameObject closestBlock = null;
                float minDistance = float.MaxValue;
                
                for (int layerIndex = 1; layerIndex <= 5; layerIndex++)
                {
                    Transform layerTransform = blocksContainer.Find($"Layer{layerIndex}");
                    if (layerTransform == null) continue;
                    
                    foreach (Transform child in layerTransform)
                    {
                        if (child.name.Contains("Cube"))
                        {
                            float distance = Vector3.Distance(child.position, targetWorldPos);
                            
                            // 如果距离小于0.1（容差范围），认为是目标方块
                            if (distance < 0.1f && distance < minDistance)
                            {
                                minDistance = distance;
                                closestBlock = child.gameObject;
                            }
                        }
                    }
                }
                
                // 如果找到了方块且当前是禁用状态，则启用它
                if (closestBlock != null)
                {
                    if (!closestBlock.activeSelf)
                    {
                        closestBlock.SetActive(true);
                        
                        // 显式启用Renderer
                        Renderer[] renderers = closestBlock.GetComponentsInChildren<Renderer>(true);
                        foreach (Renderer r in renderers)
                        {
                            r.enabled = true;
                        }
                        
                        // Debug.Log($"[BlockEditor] Enabled {closestBlock.transform.parent.name}/{closestBlock.name} at distance {minDistance}");                        
                        // 播放编辑音效
                        PlayEditSound();                    }
                    // else
                    // {
                    //     Debug.Log($"[BlockEditor] Block {closestBlock.name} already enabled");
                    // }
                }
                else
                {
                    // Debug.LogWarning($"[BlockEditor] No block found near target position {targetWorldPos} - out of 5x5x5 range");
                    PlayDenySound();
                    StartCoroutine(ShakeBlock(hitBlock, ray.direction));
                }
            }
        }
    }
    

    
    // 将世界坐标转换为网格坐标
    Vector3 GetGridPosition(Vector3 worldPosition)
    {
        return new Vector3(
            Mathf.Round(worldPosition.x / blockSize) * blockSize,
            Mathf.Round(worldPosition.y / blockSize) * blockSize,
            Mathf.Round(worldPosition.z / blockSize) * blockSize
        );
    }
    
    // 将法线方向转换为网格方向
    Vector3 GetGridDirection(Vector3 normal)
    {
        // 找到最接近的坐标轴方向
        float absX = Mathf.Abs(normal.x);
        float absY = Mathf.Abs(normal.y);
        float absZ = Mathf.Abs(normal.z);
        
        if (absX > absY && absX > absZ)
        {
            return new Vector3(Mathf.Sign(normal.x), 0, 0);
        }
        else if (absY > absX && absY > absZ)
        {
            return new Vector3(0, Mathf.Sign(normal.y), 0);
        }
        else
        {
            return new Vector3(0, 0, Mathf.Sign(normal.z));
        }
    }
    
    // 获取第一个可见的碰撞对象（跳过不可见/透明的方块）
    bool GetFirstVisibleHit(Ray ray, out RaycastHit firstVisibleHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, rayDistance, blockLayer);
        
        // 按距离排序
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        
        // 找到第一个可见的碰撞
        foreach (RaycastHit hit in hits)
        {
            if (IsBlockVisible(hit.collider.gameObject))
            {
                firstVisibleHit = hit;
                return true;
            }
        }
        
        firstVisibleHit = default(RaycastHit);
        return false;
    }
    
    // 检查方块是否可见（检查GameObject激活状态和Renderer）
    bool IsBlockVisible(GameObject block)
    {
        // 先检查GameObject是否激活
        if (!block.activeSelf)
        {
            return false;
        }
        
        Renderer renderer = block.GetComponent<Renderer>();
        if (renderer == null || !renderer.enabled)
        {
            return false;
        }
        
        // 检查材质的alpha值，如果接近1则视为可见
        Material mat = renderer.material;
        if (mat.HasProperty("_Color"))
        {
            Color color = mat.color;
            return color.a > 0.9f; // alpha > 0.9 视为可见（完全不透明）
        }
        
        // 如果没有_Color属性，默认视为可见
        return true;
    }
    
    // 处理Hover效果
    void HandleHover()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        if (GetFirstVisibleHit(ray, out hit))
        {
            GameObject hitBlock = hit.collider.gameObject;
            Vector3 gridPos = GetGridPosition(hitBlock.transform.position);
            
            // 检查是否在容器内（通过Layer检查）
            Transform layer = hitBlock.transform.parent;
            if (layer != null && layer.name.StartsWith("Layer"))
            {
                // 如果是新的方块
                if (hitBlock != hoveredBlock)
                {
                    // 恢复之前的方块
                    if (hoveredBlock != null)
                    {
                        RestoreBlockColor(hoveredBlock);
                    }
                    
                    // 设置新的hover方块
                    hoveredBlock = hitBlock;
                    ApplyHoverTint(hoveredBlock);
                }
            }
            else
            {
                // 不在容器内的方块，清除hover
                if (hoveredBlock != null)
                {
                    RestoreBlockColor(hoveredBlock);
                    hoveredBlock = null;
                }
            }
        }
        else
        {
            // 鼠标没有指向任何方块
            if (hoveredBlock != null)
            {
                RestoreBlockColor(hoveredBlock);
                hoveredBlock = null;
            }
        }
    }
    
    // 应用hover变灰效果
    void ApplyHoverTint(GameObject block)
    {
        Renderer renderer = block.GetComponent<Renderer>();
        if (renderer == null) return;
        
        // 保存原始颜色
        if (!originalColors.ContainsKey(renderer))
        {
            Color[] colors = new Color[renderer.materials.Length];
            for (int i = 0; i < renderer.materials.Length; i++)
            {
                if (renderer.materials[i].HasProperty("_Color"))
                {
                    colors[i] = renderer.materials[i].color;
                }
            }
            originalColors[renderer] = colors;
        }
        
        // 应用灰色tint
        for (int i = 0; i < renderer.materials.Length; i++)
        {
            if (renderer.materials[i].HasProperty("_Color"))
            {
                Color original = originalColors[renderer][i];
                renderer.materials[i].color = new Color(
                    original.r * hoverTint.r,
                    original.g * hoverTint.g,
                    original.b * hoverTint.b,
                    original.a * hoverTint.a
                );
            }
        }
    }
    
    // 恢复方块原始颜色
    void RestoreBlockColor(GameObject block)
    {
        Renderer renderer = block.GetComponent<Renderer>();
        if (renderer == null || !originalColors.ContainsKey(renderer)) return;
        
        Color[] colors = originalColors[renderer];
        for (int i = 0; i < renderer.materials.Length && i < colors.Length; i++)
        {
            if (renderer.materials[i].HasProperty("_Color"))
            {
                renderer.materials[i].color = colors[i];
            }
        }
    }
    
    // 公共方法：清除hover状态（供LayersManager调用）
    public void ClearHover()
    {
        if (hoveredBlock != null)
        {
            RestoreBlockColor(hoveredBlock);
            hoveredBlock = null;
        }
    }
    
    // 播放随机编辑音效
    void PlayEditSound()
    {
        if (editSounds == null || editSounds.Length == 0 || audioSource == null)
        {
            return;
        }
        
        // 从数组中随机选择一个音效
        AudioClip randomClip = editSounds[Random.Range(0, editSounds.Length)];
        
        if (randomClip != null)
        {
            audioSource.PlayOneShot(randomClip);
        }
    }
    
    // 播放随机拒绝音效
    void PlayDenySound()
    {
        if (denySounds == null || denySounds.Length == 0 || audioSource == null)
        {
            return;
        }
        
        // 从数组中随机选择一个拒绝音效
        AudioClip randomClip = denySounds[Random.Range(0, denySounds.Length)];
        
        if (randomClip != null)
        {
            audioSource.PlayOneShot(randomClip);
        }
    }
    
    // 统计当前有多少个方块是开启的
    int CountEnabledBlocks()
    {
        int count = 0;
        
        if (blocksContainer != null)
        {
            for (int layer = 1; layer <= 5; layer++)
            {
                Transform layerTransform = blocksContainer.Find($"Layer{layer}");
                if (layerTransform == null) continue;
                
                foreach (Transform child in layerTransform)
                {
                    if (child.name.Contains("Cube") && child.gameObject.activeSelf)
                    {
                        count++;
                    }
                }
            }
        }
        
        return count;
    }
    
    // 摇晃方块动画（表示无法破坏）
    IEnumerator ShakeBlock(GameObject block, Vector3 rayDirection)
    {
        if (block == null) yield break;
        
        // 如果已经在摇晃这个方块，忽略
        if (isShaking && shakingBlock == block)
        {
            yield break;
        }
        
        // 设置摇晃状态，防止重复触发
        isShaking = true;
        shakingBlock = block;
        
        // 保存或获取原始位置
        Vector3 originalPosition;
        if (!blockOriginalPositions.ContainsKey(block))
        {
            originalPosition = block.transform.localPosition;
            blockOriginalPositions[block] = originalPosition;
        }
        else
        {
            originalPosition = blockOriginalPositions[block];
        }
        
        // 计算垂直于射线的摇晃方向（使用世界Up向量叉乘射线方向）
        Vector3 shakeDirection = Vector3.Cross(rayDirection.normalized, Vector3.up).normalized;
        if (shakeDirection.magnitude < 0.1f)
        {
            // 如果射线方向与up平行，使用right向量
            shakeDirection = Vector3.Cross(rayDirection.normalized, Vector3.right).normalized;
        }
        
        // 将世界方向转换为本地方向
        shakeDirection = block.transform.parent.InverseTransformDirection(shakeDirection);
        
        int shakeCount = 4; // 摇晃次数
        float singleShakeDuration = shakeDuration / (shakeCount * 2);
        
        for (int i = 0; i < shakeCount; i++)
        {
            // 向左摇
            float leftProgress = 0f;
            while (leftProgress < 1f)
            {
                leftProgress += Time.deltaTime / singleShakeDuration;
                float offset = Mathf.Sin(leftProgress * Mathf.PI) * shakeAmount;
                block.transform.localPosition = originalPosition - shakeDirection * offset;
                yield return null;
            }
            
            // 向右摇
            float rightProgress = 0f;
            while (rightProgress < 1f)
            {
                rightProgress += Time.deltaTime / singleShakeDuration;
                float offset = Mathf.Sin(rightProgress * Mathf.PI) * shakeAmount;
                block.transform.localPosition = originalPosition + shakeDirection * offset;
                yield return null;
            }
        }
        
        // 恢复原始位置
        block.transform.localPosition = originalPosition;
        
        // 清除摇晃状态
        isShaking = false;
        shakingBlock = null;
    }
    
    // 播放点击动画
    IEnumerator PlayClickAnimation(GameObject block)
    {
        Renderer renderer = block.GetComponent<Renderer>();
        if (renderer == null) yield break;
        
        // 保存当前颜色
        Color[] currentColors = new Color[renderer.materials.Length];
        for (int i = 0; i < renderer.materials.Length; i++)
        {
            if (renderer.materials[i].HasProperty("_Color"))
            {
                currentColors[i] = renderer.materials[i].color;
            }
        }
        
        // 动画：渐变到点击颜色再回来
        float elapsed = 0f;
        while (elapsed < clickAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / clickAnimDuration;
            
            // 使用Sin曲线制作闪烁效果
            float intensity = Mathf.Sin(t * Mathf.PI);
            
            for (int i = 0; i < renderer.materials.Length; i++)
            {
                if (renderer.materials[i].HasProperty("_Color"))
                {
                    renderer.materials[i].color = Color.Lerp(currentColors[i], clickTint, intensity);
                }
            }
            
            yield return null;
        }
        
        // 恢复原始颜色
        for (int i = 0; i < renderer.materials.Length && i < currentColors.Length; i++)
        {
            if (renderer.materials[i].HasProperty("_Color"))
            {
                renderer.materials[i].color = currentColors[i];
            }
        }
    }
    
    // 公共方法：重新初始化方块字典（在运行时添加/删除方块后调用）
    public void RefreshBlockDictionary()
    {
        blockDictionary.Clear();
        InitializeBlockDictionary();
        // Debug.Log($"[BlockEditor] Block dictionary refreshed - {blockDictionary.Count} blocks");
    }
    
    // 公共方法：显示所有方块
    public void ShowAllBlocks()
    {
        foreach (var block in blockDictionary.Values)
        {
            if (block != null)
            {
                block.SetActive(true);
            }
        }
        // Debug.Log("[BlockEditor] All blocks shown");
    }
    
    // 公共方法：隐藏所有方块
    public void HideAllBlocks()
    {
        foreach (var block in blockDictionary.Values)
        {
            if (block != null)
            {
                block.SetActive(false);
            }
        }
        // Debug.Log("[BlockEditor] All blocks hidden");
    }
}
