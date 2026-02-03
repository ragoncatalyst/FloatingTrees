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
    
    [Header("视觉反馈")]
    [SerializeField] private Color hoverTint = new Color(0.7f, 0.7f, 0.7f, 1f);  // Hover时的灰色
    [SerializeField] private Color clickTint = new Color(1f, 1f, 0.5f, 1f);      // 点击时的闪烁颜色
    [SerializeField] private float clickAnimDuration = 0.2f;                      // 点击动画时长
    
    // 存储所有方块的状态（位置 -> 方块GameObject）
    private Dictionary<Vector3, GameObject> blockDictionary = new Dictionary<Vector3, GameObject>();
    
    // Hover状态
    private GameObject hoveredBlock;
    private Dictionary<Renderer, Color[]> originalColors = new Dictionary<Renderer, Color[]>();
    
    void Start()
    {
        // 如果没有指定摄像机，使用主摄像机
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        // 初始化方块字典
        InitializeBlockDictionary();
        
        Debug.Log($"[BlockEditor] Initialized with {blockDictionary.Count} blocks");
    }
    
    void InitializeBlockDictionary()
    {
        blockDictionary.Clear();
        
        // 如果指定了容器，从容器中查找所有方块
        if (blocksContainer != null)
        {
            // 获取容器内所有带Collider的物体（这才是真正可点击的方块）
            Collider[] allColliders = blocksContainer.GetComponentsInChildren<Collider>(true);
            
            foreach (Collider col in allColliders)
            {
                GameObject block = col.gameObject;
                Vector3 gridPos = GetGridPosition(block.transform.position);
                
                if (!blockDictionary.ContainsKey(gridPos))
                {
                    blockDictionary[gridPos] = block;
                    Debug.Log($"[BlockEditor] Added block at {gridPos}: {block.name} (active: {block.activeSelf})");
                }
                else
                {
                    Debug.LogWarning($"[BlockEditor] Duplicate block at {gridPos}: {block.name} vs {blockDictionary[gridPos].name}");
                }
            }
            Debug.Log($"[BlockEditor] Initialized from container: {blockDictionary.Count} blocks");
        }
        else
        {
            Debug.LogWarning("[BlockEditor] No blocksContainer assigned! Please assign it in the Inspector.");
        }
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
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        // 获取第一个可见的方块
        if (GetFirstVisibleHit(ray, out hit))
        {
            GameObject hitBlock = hit.collider.gameObject;
            Vector3 gridPos = GetGridPosition(hitBlock.transform.position);
            
            // 只有在字典中的方块才能被关闭
            if (blockDictionary.ContainsKey(gridPos) && blockDictionary[gridPos] == hitBlock)
            {
                // 关闭此方块
                DisableBlock(hitBlock);
                Debug.Log($"[BlockEditor] Left Click - Disabled block: {hitBlock.name} at {hit.point}");
            }
            else
            {
                Debug.Log($"[BlockEditor] Left Click - Block not in container, ignoring: {hitBlock.name}");
            }
        }
    }
    
    void HandleRightClick()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        
        // 获取第一个可见的方块
        if (GetFirstVisibleHit(ray, out hit))
        {
            GameObject hitBlock = hit.collider.gameObject;
            
            // 获取碰撞点的法线方向
            Vector3 normal = hit.normal;
            
            // 计算相邻方块的位置（沿法线方向）
            Vector3 currentGridPos = GetGridPosition(hitBlock.transform.position);
            Vector3 adjacentGridPos = currentGridPos + GetGridDirection(normal) * blockSize;
            
            Debug.Log($"[BlockEditor] Right Click - Current block at {currentGridPos}, adjacent position {adjacentGridPos}, normal {normal}");
            
            // 检查相邻位置是否有关闭的方块
            if (blockDictionary.ContainsKey(adjacentGridPos))
            {
                GameObject adjacentBlock = blockDictionary[adjacentGridPos];
                
                // 如果相邻方块处于关闭状态，打开它
                if (adjacentBlock != null && !adjacentBlock.activeSelf)
                {
                    EnableBlock(adjacentBlock);
                    Debug.Log($"[BlockEditor] Right Click - Enabled adjacent block at {adjacentGridPos}");
                }
                else if (adjacentBlock == null)
                {
                    Debug.LogWarning($"[BlockEditor] Right Click - Adjacent block is null at {adjacentGridPos}");
                }
                else
                {
                    Debug.Log($"[BlockEditor] Right Click - Adjacent block is already active at {adjacentGridPos}");
                }
            }
            else
            {
                Debug.Log($"[BlockEditor] Right Click - No block found at adjacent position {adjacentGridPos}");
            }
        }
    }
    
    // 关闭方块
    void DisableBlock(GameObject block)
    {
        if (block != null && block.activeSelf)
        {
            block.SetActive(false);
            Debug.Log($"[BlockEditor] Block disabled: {block.name}");
        }
    }
    
    // 打开方块
    void EnableBlock(GameObject block)
    {
        if (block != null && !block.activeSelf)
        {
            block.SetActive(true);
            Debug.Log($"[BlockEditor] Block enabled: {block.name}");
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
    
    // 检查方块是否可见（检查材质透明度）
    bool IsBlockVisible(GameObject block)
    {
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
            
            // 只有在字典中的方块才能被hover
            if (blockDictionary.ContainsKey(gridPos) && blockDictionary[gridPos] == hitBlock)
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
        Debug.Log($"[BlockEditor] Block dictionary refreshed - {blockDictionary.Count} blocks");
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
        Debug.Log("[BlockEditor] All blocks shown");
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
        Debug.Log("[BlockEditor] All blocks hidden");
    }
}
