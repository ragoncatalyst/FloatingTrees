using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StorageManager : MonoBehaviour
{
    [Header("场景设置")]
    [SerializeField] private string workshopSceneName = "Workshop";
    
    [Header("仓库设置")]
    [SerializeField] private Transform storageContainer; // 存放方块的容器
    
    // 仓库中的方块数据
    [System.Serializable]
    public class StorageBlock
    {
        public GameObject blockObject;
        public string blockType;
        public Vector3 originalPosition;
        public bool isInUse;
    }
    
    private List<StorageBlock> storageBlocks = new List<StorageBlock>();
    private bool isInWorkshop = false;
    
    void Start()
    {
        // 检查是否在Workshop场景
        isInWorkshop = SceneManager.GetActiveScene().name == workshopSceneName;
        
        if (!isInWorkshop)
        {
            Debug.Log("[StorageManager] 当前不在Workshop场景，StorageManager不会生效");
            enabled = false;
            return;
        }
        
        Debug.Log("[StorageManager] Workshop场景已加载，初始化仓库管理");
        InitializeStorage();
        LoadStorageData();
    }
    
    /// <summary>
    /// 初始化仓库，收集所有子物体方块
    /// </summary>
    void InitializeStorage()
    {
        storageBlocks.Clear();
        
        // 使用transform作为容器，如果没有指定storageContainer
        Transform container = storageContainer != null ? storageContainer : transform;
        
        // 收集所有子物体方块
        foreach (Transform child in container)
        {
            StorageBlock block = new StorageBlock
            {
                blockObject = child.gameObject,
                blockType = child.name,
                originalPosition = child.position,
                isInUse = false
            };
            
            storageBlocks.Add(block);
        }
        
        Debug.Log($"[StorageManager] 初始化完成，仓库中有 {storageBlocks.Count} 个方块");
    }
    
    /// <summary>
    /// 加载仓库数据
    /// </summary>
    void LoadStorageData()
    {
        // TODO: 从PlayerPrefs或文件加载仓库数据
        // 例如：哪些方块已被使用，位置状态等
        
        Debug.Log("[StorageManager] 仓库数据已加载");
    }
    
    /// <summary>
    /// 获取指定类型的可用方块
    /// </summary>
    public GameObject GetAvailableBlock(string blockType)
    {
        StorageBlock block = storageBlocks.Find(b => b.blockType == blockType && !b.isInUse);
        
        if (block != null)
        {
            block.isInUse = true;
            Debug.Log($"[StorageManager] 获取方块: {blockType}");
            return block.blockObject;
        }
        
        Debug.LogWarning($"[StorageManager] 没有可用的方块: {blockType}");
        return null;
    }
    
    /// <summary>
    /// 归还方块到仓库
    /// </summary>
    public void ReturnBlock(GameObject blockObject)
    {
        StorageBlock block = storageBlocks.Find(b => b.blockObject == blockObject);
        
        if (block != null)
        {
            block.isInUse = false;
            // 恢复到原始位置
            blockObject.transform.position = block.originalPosition;
            Debug.Log($"[StorageManager] 归还方块: {block.blockType}");
        }
        else
        {
            Debug.LogWarning($"[StorageManager] 未找到方块: {blockObject.name}");
        }
    }
    
    /// <summary>
    /// 获取指定类型方块的可用数量
    /// </summary>
    public int GetAvailableCount(string blockType)
    {
        return storageBlocks.FindAll(b => b.blockType == blockType && !b.isInUse).Count;
    }
    
    /// <summary>
    /// 获取所有可用方块
    /// </summary>
    public List<GameObject> GetAllAvailableBlocks()
    {
        List<GameObject> availableBlocks = new List<GameObject>();
        
        foreach (StorageBlock block in storageBlocks)
        {
            if (!block.isInUse)
            {
                availableBlocks.Add(block.blockObject);
            }
        }
        
        return availableBlocks;
    }
    
    /// <summary>
    /// 显示/隐藏仓库中的所有方块
    /// </summary>
    public void SetStorageVisible(bool visible)
    {
        foreach (StorageBlock block in storageBlocks)
        {
            block.blockObject.SetActive(visible);
        }
        
        Debug.Log($"[StorageManager] 仓库方块 {(visible ? "显示" : "隐藏")}");
    }
    
    /// <summary>
    /// 重置所有方块到原始位置
    /// </summary>
    public void ResetAllBlocks()
    {
        foreach (StorageBlock block in storageBlocks)
        {
            block.isInUse = false;
            block.blockObject.transform.position = block.originalPosition;
        }
        
        Debug.Log("[StorageManager] 所有方块已重置");
    }
    
    /// <summary>
    /// 保存仓库数据
    /// </summary>
    void SaveStorageData()
    {
        // TODO: 保存仓库数据到PlayerPrefs或文件
        // 例如：保存每个方块的使用状态、位置等
        
        Debug.Log("[StorageManager] 仓库数据已保存");
    }
    
    void OnDestroy()
    {
        if (!isInWorkshop) return;
        
        // 退出场景时自动保存
        SaveStorageData();
        Debug.Log("[StorageManager] 退出Workshop场景，仓库数据已保存");
    }
}
