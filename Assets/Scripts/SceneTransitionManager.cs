using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景过渡管理器 - 提供流畅的场景切换体验
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    private static SceneTransitionManager instance;
    
    [Header("过渡设置")]
    [SerializeField] private float fadeDuration = 0.3f;  // 淡入淡出时长
    
    private CanvasGroup fadeCanvasGroup;
    private Canvas fadeCanvas;
    private bool isTransitioning = false;
    
    /// <summary>
    /// 检查是否正在进行场景切换
    /// </summary>
    public static bool IsTransitioning
    {
        get { return instance != null && instance.isTransitioning; }
    }
    
    void Awake()
    {
        // 单例模式
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
            CreateFadeCanvas();
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    /// <summary>
    /// 创建淡入淡出画布
    /// </summary>
    void CreateFadeCanvas()
    {
        // 创建Canvas
        GameObject canvasObj = new GameObject("FadeCanvas");
        canvasObj.transform.SetParent(transform);
        
        fadeCanvas = canvasObj.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 9999; // 最顶层
        
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // 创建黑色遮罩
        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform);
        
        Image fadeImage = imageObj.AddComponent<Image>();
        fadeImage.color = Color.black;
        
        // 设置为全屏
        RectTransform rectTransform = imageObj.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        
        // 添加CanvasGroup控制透明度
        fadeCanvasGroup = imageObj.AddComponent<CanvasGroup>();
        fadeCanvasGroup.alpha = 0f; // 初始透明
        fadeCanvasGroup.blocksRaycasts = false;
    }
    
    /// <summary>
    /// 异步加载场景（带淡入淡出效果）
    /// </summary>
    public static void LoadSceneAsync(string sceneName)
    {
        if (instance == null)
        {
            Debug.LogError("SceneTransitionManager实例不存在！请确保场景中有SceneTransitionManager。");
            return;
        }
        
        if (instance.isTransitioning)
        {
            Debug.LogWarning("场景切换正在进行中，请稍候再试...");
            return;
        }
        
        instance.StartCoroutine(instance.LoadSceneCoroutine(sceneName));
    }
    
    IEnumerator LoadSceneCoroutine(string sceneName)
    {
        isTransitioning = true;
        
        // 1. 保存当前状态
        RocketStateManager.Save();
        
        // 2. 淡出（变黑）
        yield return StartCoroutine(FadeOut());
        
        // 3. 异步加载场景
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        asyncLoad.allowSceneActivation = false;
        
        // 等待场景加载到90%
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }
        
        // 激活场景
        asyncLoad.allowSceneActivation = true;
        
        // 等待场景完全加载
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        
        // 4. 短暂延迟，确保所有对象初始化完成
        yield return new WaitForSeconds(0.1f);
        
        // 5. 淡入（显示场景）
        yield return StartCoroutine(FadeIn());
        
        isTransitioning = false;
    }
    
    /// <summary>
    /// 淡出效果
    /// </summary>
    IEnumerator FadeOut()
    {
        fadeCanvasGroup.blocksRaycasts = true;
        float elapsed = 0f;
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }
        
        fadeCanvasGroup.alpha = 1f;
    }
    
    /// <summary>
    /// 淡入效果
    /// </summary>
    IEnumerator FadeIn()
    {
        float elapsed = 0f;
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            fadeCanvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }
        
        fadeCanvasGroup.alpha = 0f;
        fadeCanvasGroup.blocksRaycasts = false;
    }
}
