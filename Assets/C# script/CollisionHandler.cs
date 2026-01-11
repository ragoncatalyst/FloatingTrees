using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] float levelLoadDelay = 2f;
    [SerializeField] float maxBankAngle = 50f;       // 最大允许倾斜角度（超过则坠毁）
    
    private Rigidbody rb;
    private bool isCrashed = false;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        // 每帧检测坠毁条件
        if (!isCrashed)
        {
            CheckCrashConditions();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        switch (collision.gameObject.tag)
        {
            case "Friendly":
                Debug.Log("Friendly"); 
                break;
            case "Finish":
                HandleFinish();
                break;
            default:
                TriggerCrash("Collision");
                break;
        }
    }

    void CheckCrashConditions()
    {
        // 检查倾斜角度
        float bankAngle = Vector3.Angle(rb.transform.up, Vector3.up);
        if (bankAngle > maxBankAngle)
        {
            TriggerCrash($"Bank Angle Exceeded: {bankAngle:F1}°");
            return;
        }
    }

    void TriggerCrash(string crashReason)
    {
        isCrashed = true;
        Debug.Log($"Crash detected: {crashReason}");
        StartCrashSequence();
    }

    void StartCrashSequence()
    {
        // 安全检查：只有在存在 AudioSource 时才禁用
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.enabled = false;
        }
        
        Invoke("Respawn", levelLoadDelay);
    }

    void Respawn()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    void HandleFinish()
    {
        Debug.Log("Finish");
        Invoke("NextLevel", levelLoadDelay);
    }

    void NextLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        int nextSceneIndex = currentSceneIndex + 1;
        if (nextSceneIndex == SceneManager.sceneCountInBuildSettings)
        {
            nextSceneIndex = 0;
        }
        SceneManager.LoadScene(nextSceneIndex);
    }
}
