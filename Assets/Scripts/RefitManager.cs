using UnityEngine;
using UnityEngine.SceneManagement;

public class RefitManager : MonoBehaviour
{
    [Header("改装配置")]
    [SerializeField] private string workshopSceneName = "Workshop";
    
    // 改装数据存储
    private class RefitData
    {
        // 这里存储改装相关的数据
        // 例如：方块配置、颜色、材质等
    }
    
    private RefitData currentRefitData;
    
    void Start()
    {
        // 检查是否在Workshop场景
        if (SceneManager.GetActiveScene().name == workshopSceneName)
        {
            Debug.Log("[RefitManager] Workshop场景已加载");
            LoadRefitData();
        }
        else
        {
            Debug.LogWarning("[RefitManager] 当前不在Workshop场景，RefitManager不会生效");
        }
    }
    
    /// <summary>
    /// 加载改装数据
    /// </summary>
    void LoadRefitData()
    {
        // TODO: 从PlayerPrefs或文件加载改装数据
        currentRefitData = new RefitData();
        Debug.Log("[RefitManager] 改装数据已加载");
    }
    
    /// <summary>
    /// 保存改装数据
    /// </summary>
    public void SaveRefitData()
    {
        // TODO: 保存改装数据到PlayerPrefs或文件
        Debug.Log("[RefitManager] 改装数据已保存");
    }
    
    /// <summary>
    /// 应用改装配置
    /// </summary>
    public void ApplyRefit()
    {
        Debug.Log("[RefitManager] 应用改装配置");
        SaveRefitData();
    }
    
    void OnDestroy()
    {
        // 退出场景时自动保存
        SaveRefitData();
    }
}
