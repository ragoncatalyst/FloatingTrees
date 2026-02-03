using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class Resulting : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] TextMeshProUGUI victoryText;         // 胜利文本
    
    [Header("Victory Settings")]
    [SerializeField] float levelLoadDelay = 3f;           // 场景加载延迟
    [SerializeField] float maxLandingSpeed = 5f;          // 最大安全着陆速度（与CollisionHandler同步）
    [SerializeField] float stopConfirmationTime = 0.8f;   // 需要保持静止的时间（秒）
    [SerializeField] float stoppedThreshold = 0.1f;       // 判定为停止的速度阈值
    
    private CollisionHandler collisionHandler;            // 碰撞处理器
    private bool hasResult = false;                       // 是否已经有结果（胜利或失败）
    private bool isLanded = false;                        // 是否处于着陆状态
    private bool wasOverSpeed = false;                    // 上一帧是否超速
    private float lastFrameSpeed = 0f;                    // 上一帧的速度
    private bool hasEverHadVelocity = false;              // 是否曾经有过速度（非零）
    private Movement movementController;                  // 用于检查玩家是否操控过
    private float stoppedTime = 0f;                       // 速度为0持续的时间
    private bool hasDetectedStop = false;                 // 是否已经检测到速度变为0

    void Start()
    {
        collisionHandler = GetComponent<CollisionHandler>();
        movementController = GetComponent<Movement>();
        
        if (collisionHandler == null)
        {
            Debug.LogError("CollisionHandler not found on parent!");
        }
        else
        {
            // 订阅碰撞事件
            collisionHandler.OnCrash += HandleCrash;
        }
        
        // 初始化时隐藏胜利文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(false);
        }
        
        lastFrameSpeed = 0f;
        hasEverHadVelocity = false;
        
        Debug.Log("Resulting system initialized");
    }

    void OnDestroy()
    {
        // 取消订阅事件
        if (collisionHandler != null)
        {
            collisionHandler.OnCrash -= HandleCrash;
        }
    }

    // 处理撞击 - 直接爆炸
    void HandleCrash(string crashReason)
    {
        if (hasResult) return;  // 如果已经有结果，不再处理
        
        Debug.Log($"<color=red>★★★ HandleCrash called - EXPLOSION! ★★★</color>");
        // 任何碰撞都直接爆炸，由CollisionHandler处理爆炸效果
        hasResult = true;
    }

    void FixedUpdate()
    {
        // 如果已经有结果，不再检查
        if (hasResult) return;
        
        // 从CollisionHandler获取当前着陆信息
        if (collisionHandler == null) return;
        
        LandingInfo landingInfo = collisionHandler.CheckLandingStatus();
        
        // 更新着陆状态
        if (landingInfo.isLanded != isLanded)
        {
            isLanded = landingInfo.isLanded;
            if (isLanded)
                Debug.Log("<color=yellow>★ Resulting: Rocket ENTERED landing state</color>");
            else
                Debug.Log("<color=cyan>★ Resulting: Rocket LEFT landing state</color>");
        }
        
        // 无论是否着陆，只要曾经接触过物体，就持续检查速度和结局
        if (landingInfo.contactedObjects.Count > 0)
        {
            CheckLandingStatus();
        }
    }

    void CheckLandingStatus()
    {
        // 从 CollisionHandler 获取着陆信息
        if (collisionHandler == null) return;
        
        LandingInfo landingInfo = collisionHandler.CheckLandingStatus();
        
        // 如果没有接触历史记录，重置所有状态
        if (landingInfo.contactedObjects.Count == 0)
        {
            hasDetectedStop = false;
            stoppedTime = 0f;
            hasEverHadVelocity = false;
            return;
        }
        
        float currentSpeed = landingInfo.currentSpeed;
        
        // 只有在玩家操控过火箭后，才开始追踪速度变化
        if (movementController != null && movementController.HasPlayerControlled())
        {
            // 记录是否曾经有过速度
            if (currentSpeed > stoppedThreshold)
            {
                hasEverHadVelocity = true;
            }
            
            // 检查当前是否停止
            bool isStopped = currentSpeed <= stoppedThreshold;
            
            // 如果当前停止且曾经移动过
            if (isStopped && hasEverHadVelocity)
            {
                // 如果还没开始计时，开始计时
                if (!hasDetectedStop)
                {
                    hasDetectedStop = true;
                    stoppedTime = 0f;
                    Debug.Log($"<color=yellow>★ Speed STOPPED! Now monitoring for {stopConfirmationTime}s...</color>");
                }
                
                // 继续计时
                stoppedTime += Time.fixedDeltaTime;
                Debug.Log($"<color=yellow>★ Stopped for {stoppedTime:F2}s/{stopConfirmationTime}s (contacts: {landingInfo.contactedObjects.Count})</color>");
                
                // 如果速度在0.8秒内一直保持为0，判定为稳定停止
                if (stoppedTime >= stopConfirmationTime)
                {
                    Debug.Log($"<color=green>★★★ STABLE STOP CONFIRMED! Checking victory...</color>");
                    hasDetectedStop = false;  // 重置，防止重复判定
                    stoppedTime = 0f;
                    CheckVictoryConditions(landingInfo);
                }
            }
            else if (!isStopped)
            {
                // 速度不为0，重置计时
                if (hasDetectedStop)
                {
                    Debug.Log($"<color=cyan>Speed increased to {currentSpeed:F2} m/s, resetting...</color>");
                    hasDetectedStop = false;
                    stoppedTime = 0f;
                }
            }
        }
        
        lastFrameSpeed = currentSpeed;
        
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

    void CheckVictoryConditions(LandingInfo landingInfo)
    {
        // 使用从 CollisionHandler 获取的接触物体列表
        List<string> landingPads = new List<string>();
        List<string> launchingPads = new List<string>();
        List<string> otherObjects = new List<string>();
        bool landingPadHasFinish = false;
        
        Debug.Log($"<color=green>★★★ CheckVictoryConditions - contactedObjects: {string.Join(", ", landingInfo.contactedObjects)}</color>");
        
        foreach (string objectName in landingInfo.contactedObjects)
        {
            // 从CollisionHandler获取这个物体的Tag
            string objectTag = collisionHandler.GetContactedObjectTag(objectName);
            Debug.Log($"<color=green>  - {objectName}, Tag: {objectTag}</color>");
            
            if (objectName == "LandingPad")
            {
                landingPads.Add(objectName);
                // 检查是否有Finish标签
                if (objectTag == "Finish")
                {
                    landingPadHasFinish = true;
                    Debug.Log($"<color=green>    -> LandingPad HAS Finish tag!</color>");
                }
                else
                {
                    Debug.Log($"<color=yellow>    -> LandingPad does NOT have Finish tag (Tag: {objectTag})</color>");
                }
            }
            else if (objectName == "LaunchingPad")
            {
                launchingPads.Add(objectName);
            }
            else
            {
                otherObjects.Add(objectName);
            }
        }
        
        Debug.Log($"<color=green>★★★ CheckVictoryConditions - LandingPads={landingPads.Count}(Finish={landingPadHasFinish}), LaunchingPads={launchingPads.Count}, Others={otherObjects.Count}</color>");
        
        // 判定胜利：必须有LandingPad且有Finish标签，且没有LaunchingPad和其他物体
        if (landingPads.Count > 0 && landingPadHasFinish && launchingPads.Count == 0 && otherObjects.Count == 0)
        {
            Debug.Log("<color=green>★★★★★ VICTORY! Landed safely on LandingPad with Finish tag! ★★★★★</color>");
            TriggerVictory();
        }
        else
        {
            // 不符合胜利条件 - 触发爆炸
            Debug.Log($"<color=red>Victory conditions NOT met - EXPLOSION!</color>");
            TriggerExplosion();
        }
    }

    void TriggerVictory()
    {
        hasResult = true;
        Debug.Log("<color=green>★★★ VICTORY! Level Complete! ★★★</color>");
        
        // 显示胜利文本
        if (victoryText != null)
        {
            victoryText.gameObject.SetActive(true);
        }
        
        // 清空CollisionHandler状态
        if (collisionHandler != null)
        {
            collisionHandler.ResetState();
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

    void TriggerExplosion()
    {
        hasResult = true;
        Debug.Log($"<color=red>★★★ EXPLOSION! ★★★</color>");
        
        // 手动触发爆炸效果（当火箭停在错误位置时）
        Movement movementController = GetComponent<Movement>();
        if (movementController != null)
        {
            // 以当前速度触发爆炸
            float currentSpeed = collisionHandler != null ? collisionHandler.GetCurrentSpeed() : 0f;
            movementController.DetachChildRigidbodies(currentSpeed);
        }
        
        // 停止音效
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }
}
