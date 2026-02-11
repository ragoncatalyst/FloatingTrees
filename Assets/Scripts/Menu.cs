using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 主菜单管理器 - 管理主界面的所有UI交互
/// </summary>
public class Menu : MonoBehaviour
{
    [Header("UI按钮")]
    [SerializeField] private Button startButton;        // 开始游戏按钮
    [SerializeField] private Button optionsButton;      // 选项按钮
    [SerializeField] private Button advancementsButton; // 成就按钮
    [SerializeField] private Button exitButton;         // 退出按钮
    // (initializeButton removed — test-only)
    
    [Header("面板")]
    [SerializeField] private GameObject optionsPanel;       // 选项面板
    [SerializeField] private GameObject advancementsPanel;  // 成就面板
    
    [Header("场景设置")]
    [SerializeField] private string mainSceneName = "Main"; // 主场景名称
    
    void Start()
    {
        // 绑定按钮事件
        if (startButton != null)
            startButton.onClick.AddListener(OnStartGameClicked);
        
        if (optionsButton != null)
            optionsButton.onClick.AddListener(OnOptionsClicked);
        
        if (advancementsButton != null)
            advancementsButton.onClick.AddListener(OnAdvancementsClicked);
        
        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitClicked);

        // initializeButton removed (was test-only)
        
        // 确保面板初始状态为关闭
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        
        if (advancementsPanel != null)
            advancementsPanel.SetActive(false);
    }
    
    /// <summary>
    /// 开始游戏按钮点击事件 - 淡出并切换到主场景
    /// </summary>
    void OnStartGameClicked()
    {
        Debug.Log("开始游戏");
        
        // 禁用按钮防止重复点击
        if (startButton != null)
            startButton.interactable = false;
        
        // 使用场景过渡管理器加载场景（带淡入淡出效果）
        SceneTransitionManager.LoadSceneAsync(mainSceneName);
    }
    
    /// <summary>
    /// 选项按钮点击事件 - 打开/关闭选项面板
    /// </summary>
    void OnOptionsClicked()
    {
        Debug.Log("打开/关闭选项面板");
        
        if (optionsPanel != null)
        {
            // 切换面板显示状态
            bool isActive = optionsPanel.activeSelf;
            optionsPanel.SetActive(!isActive);
            
            // 如果打开选项面板，关闭成就面板
            if (!isActive && advancementsPanel != null)
                advancementsPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("选项面板未分配！请在Inspector中设置Options Panel。");
        }
    }
    
    /// <summary>
    /// 成就按钮点击事件 - 打开/关闭成就面板
    /// </summary>
    void OnAdvancementsClicked()
    {
        Debug.Log("打开/关闭成就面板");
        
        if (advancementsPanel != null)
        {
            // 切换面板显示状态
            bool isActive = advancementsPanel.activeSelf;
            advancementsPanel.SetActive(!isActive);
            
            // 如果打开成就面板，关闭选项面板
            if (!isActive && optionsPanel != null)
                optionsPanel.SetActive(false);
        }
        else
        {
            Debug.LogWarning("成就面板未分配！请在Inspector中设置Advancements Panel。");
        }
    }
    
    /// <summary>
    /// 退出按钮点击事件 - 退出游戏
    /// </summary>
    void OnExitClicked()
    {
        Debug.Log("退出游戏");
        
#if UNITY_EDITOR
        // 在编辑器中停止运行
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // 在构建版本中退出应用程序
        Application.Quit();
#endif
    }
    
    /// <summary>
    /// 关闭选项面板（被面板上的关闭按钮调用）
    /// </summary>
    public void CloseOptionsPanel()
    {
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(false);
            Debug.Log("关闭选项面板");
        }
    }
    
    /// <summary>
    /// 关闭成就面板（被面板上的关闭按钮调用）
    /// </summary>
    public void CloseAdvancementsPanel()
    {
        if (advancementsPanel != null)
        {
            advancementsPanel.SetActive(false);
            Debug.Log("关闭成就面板");
        }
    }
    
    /// <summary>
    /// 关闭所有面板（通用方法）
    /// </summary>
    public void CloseAllPanels()
    {
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        
        if (advancementsPanel != null)
            advancementsPanel.SetActive(false);
        
        Debug.Log("关闭所有面板");
    }

    // OnInitializeClicked removed (test-only)
    
    void OnDestroy()
    {
        // 清理按钮监听器
        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartGameClicked);
        
        if (optionsButton != null)
            optionsButton.onClick.RemoveListener(OnOptionsClicked);
        
        if (advancementsButton != null)
            advancementsButton.onClick.RemoveListener(OnAdvancementsClicked);
        
        if (exitButton != null)
            exitButton.onClick.RemoveListener(OnExitClicked);

        // initializeButton removed (was test-only)
    }
}
