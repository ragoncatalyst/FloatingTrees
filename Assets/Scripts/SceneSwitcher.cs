using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{
    [Header("场景名称")]
    [SerializeField] private string workshopSceneName = "Workshop";
    [SerializeField] private string mainSceneName = "Main";
    
    void Update()
    {
        // 检测Enter键或Return键
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            SwitchScene();
        }
    }
    
    void SwitchScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        string targetSceneName = "";
        
        if (currentSceneName == workshopSceneName)
        {
            targetSceneName = mainSceneName;
        }
        else if (currentSceneName == mainSceneName)
        {
            targetSceneName = workshopSceneName;
        }
        else
        {
            Debug.LogWarning($"[SceneSwitcher] Current scene '{currentSceneName}' is neither Workshop nor Main.");
            return;
        }
        
        // 使用异步加载和淡入淡出效果
        SceneTransitionManager.LoadSceneAsync(targetSceneName);
    }
}
