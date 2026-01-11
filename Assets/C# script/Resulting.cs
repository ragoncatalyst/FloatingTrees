using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class Resulting : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TextMeshProUGUI victoryText;         // 胜利文本
    [SerializeField] TextMeshProUGUI defeatText;          // 失败文本
    
    [Header("Victory/Defeat Settings")]
    [SerializeField] float levelLoadDelay = 3f;           // 场景加载延迟
    [SerializeField] float maxLandingSpeed = 5f;          // 最大安全着陆速度
    [SerializeField] float stoppedThreshold = 0.1f;       // 判定为停止的速度阈值
    
    private Rigidbody rb;
    private bool hasResult = false;                       // 是否已经有结果（胜利或失败）
    private bool isLanded = false;                        // 是否处于着陆状态
    private HashSet<Collision> activeCollisions = new HashSet<Collision>();  // 当前接触的碰撞
    private bool wasOverSpeed = false;                    // 上一帧是否超速
    private float lastFrameSpeed = 0f;                    // 上一帧的速度
    private bool hasEverHadVelocity = false;              // 是否曾经有过速度（非零）
    private Movement movementController;                  // 用于检查玩家是否操控过
    private float stoppedTime = 0f;                       // 速度为0持续的时间
    private bool hasDetectedStop = false;                 // 是否已经检测到速度变为0
    private const float stopConfirmationTime = 0.8f;      // 需要保持静止的时间（秒）

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        movementController = GetComponent<Movement>();
        
        // 初始化时隐藏所有文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(false);
        }
        if (defeatText != null)
        {
            defeatText.gameObject.SetActive(false);
        }
        
        lastFrameSpeed = 0f;
        hasEverHadVelocity = false;
        
        Debug.Log("Resulting system initialized");
    }

    void FixedUpdate()
    {
        // 如果已经有结果，不再检查
        if (hasResult) return;
        
        // 如果处于着陆状态，持续检查
        if (isLanded)
        {
            CheckLandingStatus();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        // 如果已经有结果，不再处理碰撞
        if (hasResult) return;
        
        // 检查碰撞速度
        float impactSpeed = collision.relativeVelocity.magnitude;
        
        Debug.Log($"Collision with {collision.gameObject.name} (Tag: {collision.gameObject.tag}), Impact speed: {impactSpeed:F2} m/s");
        
        if (impactSpeed > maxLandingSpeed)
        {
            // 超速碰撞，直接失败
            Debug.Log($"<color=red>FAILED: Impact speed too high! {impactSpeed:F1} m/s > {maxLandingSpeed} m/s</color>");
            TriggerDefeat($"Impact speed too high: {impactSpeed:F1} m/s (Max: {maxLandingSpeed} m/s)");
            return;
        }
        
        Debug.Log($"<color=green>Safe landing speed: {impactSpeed:F2} m/s <= {maxLandingSpeed} m/s</color>");
        
        // 没有超速，添加到接触列表
        activeCollisions.Add(collision);
        
        // 进入着陆状态
        if (!isLanded)
        {
            isLanded = true;
            Debug.Log("<color=yellow>Entered landing state - started monitoring</color>");
        }
    }

    void OnCollisionStay(Collision collision)
    {
        // 确保碰撞在列表中
        if (!hasResult && !activeCollisions.Contains(collision))
        {
            activeCollisions.Add(collision);
        }
    }

    void OnCollisionExit(Collision collision)
    {
        // 从接触列表中移除
        activeCollisions.Remove(collision);
        
        Debug.Log($"Left contact with {collision.gameObject.name}, remaining contacts: {activeCollisions.Count}");
        
        // 如果没有任何接触了，离开着陆状态
        if (activeCollisions.Count == 0)
        {
            isLanded = false;
            hasDetectedStop = false;  // 重置停止检测
            stoppedTime = 0f;          // 重置停止计时
            Debug.Log("<color=cyan>Left landing state - took off again</color>");
        }
    }

    void CheckLandingStatus()
    {
        // 检查是否还有接触物体
        if (activeCollisions.Count == 0)
        {
            isLanded = false;
            lastFrameSpeed = rb.velocity.magnitude;
            hasDetectedStop = false;
            stoppedTime = 0f;
            return;
        }
        
        // 检查当前速度
        float currentSpeed = rb.velocity.magnitude;
        
        // 只有在玩家操控过火箭后，才开始追踪速度变化
        if (movementController != null && movementController.HasPlayerControlled())
        {
            // 记录是否曾经有过速度
            if (currentSpeed > stoppedThreshold)
            {
                hasEverHadVelocity = true;
            }
            
            // 检查是否从有速度降到无速度
            bool isStopped = currentSpeed <= stoppedThreshold;
            bool wasMoving = lastFrameSpeed > stoppedThreshold;
            
            // 首次检测到速度变为0
            if (isStopped && wasMoving && hasEverHadVelocity && !hasDetectedStop)
            {
                hasDetectedStop = true;
                stoppedTime = 0f;
                Debug.Log($"<color=yellow>Rocket speed dropped to 0! Now monitoring for {stopConfirmationTime}s to confirm stable stop...</color>");
            }
            
            // 如果已经检测到速度为0，继续计时
            if (hasDetectedStop && isStopped)
            {
                stoppedTime += Time.fixedDeltaTime;
                
                // 如果速度在0.8秒内一直保持为0，判定为稳定停止
                if (stoppedTime >= stopConfirmationTime)
                {
                    Debug.Log($"<color=yellow>Rocket confirmed stable! Stopped for {stoppedTime:F2}s, checking victory conditions...</color>");
                    hasDetectedStop = false;  // 重置，防止重复判定
                    stoppedTime = 0f;
                    CheckVictoryConditions();
                }
            }
            else if (hasDetectedStop && !isStopped)
            {
                // 速度不为0了，重置计时
                Debug.Log($"<color=cyan>Rocket started moving again during confirmation period! Speed: {currentSpeed:F2} m/s</color>");
                hasDetectedStop = false;
                stoppedTime = 0f;
            }
        }
        
        // 检测速度变化（避免每帧输出）
        bool isOverSpeed = currentSpeed > maxLandingSpeed;
        if (isOverSpeed != wasOverSpeed)
        {
            if (isOverSpeed)
            {
                Debug.Log($"<color=orange>WARNING: Speed increased to {currentSpeed:F2} m/s (above safe limit {maxLandingSpeed} m/s)</color>");
            }
            else
            {
                Debug.Log($"<color=green>Speed decreased to safe range: {currentSpeed:F2} m/s</color>");
            }
            wasOverSpeed = isOverSpeed;
        }
        
        // 更新上一帧的速度
        lastFrameSpeed = currentSpeed;
    }

    void CheckVictoryConditions()
    {
        bool hasFinishTag = false;
        bool hasOtherTag = false;
        List<string> contactedObjects = new List<string>();
        
        // 遍历所有接触的碰撞体
        foreach (Collision collision in activeCollisions)
        {
            if (collision == null || collision.gameObject == null) continue;
            
            string tag = collision.gameObject.tag;
            contactedObjects.Add($"{collision.gameObject.name}({tag})");
            
            if (tag == "Finish")
            {
                hasFinishTag = true;
            }
            else if (tag != "Friendly")
            {
                // 不是Finish也不是Friendly，算作其他物体
                hasOtherTag = true;
            }
        }
        
        Debug.Log($"Checking victory: Contacting {activeCollisions.Count} objects: {string.Join(", ", contactedObjects)}");
        Debug.Log($"Has Finish: {hasFinishTag}, Has Other: {hasOtherTag}");
        
        // 判定结果
        if (hasFinishTag && !hasOtherTag)
        {
            // 只接触Finish tag的物体，胜利
            Debug.Log("<color=green>VICTORY! Stopped only on Finish platform!</color>");
            TriggerVictory();
        }
        else if (hasOtherTag)
        {
            // 接触了其他物体，失败
            Debug.Log($"<color=red>FAILED: Stopped on wrong surface! Contacting: {string.Join(", ", contactedObjects)}</color>");
            TriggerDefeat("Stopped on wrong surface (not only on Finish platform)");
        }
        else
        {
            // 只有Friendly tag，继续等待
            Debug.Log("<color=yellow>Only touching Friendly objects, waiting...</color>");
        }
    }

    void TriggerVictory()
    {
        hasResult = true;
        Debug.Log("Victory! Level Complete!");
        
        // 显示胜利文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(true);
        }
        
        // 停止音效
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
        
        // 延迟加载下一关
        Invoke("LoadNextLevel", levelLoadDelay);
    }

    void TriggerDefeat(string reason)
    {
        hasResult = true;
        Debug.Log($"Defeat: {reason}");
        
        // 显示失败文本
        if (defeatText != null)
        {
            defeatText.gameObject.SetActive(true);
        }
        
        // 停止音效
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
        
        // 延迟重新加载当前场景
        Invoke("ReloadLevel", levelLoadDelay);
    }

    void LoadNextLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        int nextSceneIndex = currentSceneIndex + 1;
        
        // 如果是最后一关，回到第一关
        if (nextSceneIndex >= SceneManager.sceneCountInBuildSettings)
        {
            nextSceneIndex = 0;
        }
        
        SceneManager.LoadScene(nextSceneIndex);
    }

    void ReloadLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }
}
