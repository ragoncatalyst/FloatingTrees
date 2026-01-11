using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] float maxLandingSpeed = 5f;          // 最大安全着陆速度
    [SerializeField] float explosionForce = 500f;         // 爆炸力强度
    [SerializeField] float explosionRadius = 10f;         // 爆炸半径
    
    private Rigidbody parentRigidbody;                    // 父物体的Rigidbody
    private Rigidbody[] childRigidbodies;                 // 所有子物体的rigidbody（爆炸时创建）
    private List<string> lastContactedObjects = new List<string>();  // 最后接触的物体名称列表
    private Dictionary<string, string> contactedObjectTags = new Dictionary<string, string>();  // 接触物体的Tag映射
    private List<Collision> activeCollisions = new List<Collision>();  // 当前活动的碰撞
    private float lastFrameSpeed = 0f;                    // 上一帧的速度
    private bool isLanded = false;                        // 是否处于着陆状态
    
    // 事件/回调
    public delegate void OnCrashHandler(string crashReason);
    public event OnCrashHandler OnCrash;
    
    public delegate void OnLandedHandler();
    public event OnLandedHandler OnLanded;
    
    public delegate void OnTookOffHandler();
    public event OnTookOffHandler OnTookOff;

    void Start()
    {
        // 获取父物体的Rigidbody
        parentRigidbody = GetComponent<Rigidbody>();
        if (parentRigidbody == null)
        {
            Debug.LogError("CollisionHandler: Parent object must have a Rigidbody!");
        }
        
        // 初始化子物体Rigidbody数组（爆炸时才会创建）
        childRigidbodies = new Rigidbody[0];
        
        Debug.Log($"CollisionHandler initialized on parent object");
    }

    void Update()
    {
        // 每帧更新速度信息（用于判定）
        UpdateSpeed();
    }

    // 获取当前速度（从父物体）
    public float GetCurrentSpeed()
    {
        if (parentRigidbody != null)
        {
            return parentRigidbody.velocity.magnitude;
        }
        return 0f;
    }

    // 更新上一帧的速度
    void UpdateSpeed()
    {
        lastFrameSpeed = GetCurrentSpeed();
    }

    // 处理碰撞进入（直接在父物体上调用）
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log($"<color=red>★★★ OnCollisionEnter CALLED! Object: {(collision?.gameObject?.name ?? "NULL")}</color>");
        
        if (collision == null || collision.gameObject == null) return;
        
        // 添加到活动碰撞列表
        if (!activeCollisions.Contains(collision))
        {
            activeCollisions.Add(collision);
        }
        
        float impactSpeed = collision.relativeVelocity.magnitude;
        Debug.Log($"<color=orange>★ COLLISION ENTER: {collision.gameObject.name} (Tag: {collision.gameObject.tag}), Speed: {impactSpeed:F2} m/s</color>");
        
        // 记录接触的物体
        string objectName = collision.gameObject.name;
        string objectTag = collision.gameObject.tag;
        if (!lastContactedObjects.Contains(objectName))
        {
            lastContactedObjects.Add(objectName);
            contactedObjectTags[objectName] = objectTag;  // 记录Tag
            Debug.Log($"<color=orange>Added contact: {objectName} (Tag: {objectTag}), total contacts: {activeCollisions.Count}</color>");
        }
        
        // 检查超速撞击
        if (impactSpeed > maxLandingSpeed)
        {
            Debug.Log($"<color=red>★ CRASH: Impact speed too high! {impactSpeed:F1} m/s > {maxLandingSpeed} m/s</color>");
            
            // 改变所有子方块为dynamic状态（解除粘合）
            Movement movementController = GetComponent<Movement>();
            if (movementController != null)
            {
                movementController.DetachChildRigidbodies();
            }
            
            // 施加爆炸力
            ApplyExplosionForce(collision.GetContact(0).point);
            
            // 触发碰撞事件
            string crashReason = DetermineCrashType(collision.gameObject.name, collision.gameObject.tag);
            OnCrash?.Invoke(crashReason);
            
            return;
        }
        
        // 没有超速，进入着陆状态
        if (!isLanded)
        {
            isLanded = true;
            OnLanded?.Invoke();
            Debug.Log("<color=yellow>★ ENTERED LANDING STATE</color>");
        }
    }

    // 处理碰撞保持
    void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.gameObject == null) return;
        
        // 持续更新接触物体列表（累积所有接触过的物体）
        string objectName = collision.gameObject.name;
        string objectTag = collision.gameObject.tag;
        if (!lastContactedObjects.Contains(objectName))
        {
            lastContactedObjects.Add(objectName);
            contactedObjectTags[objectName] = objectTag;  // 记录Tag
            Debug.Log($"<color=yellow>★ Added to lastContactedObjects: {objectName} (Tag: {objectTag}), total: {lastContactedObjects.Count}</color>");
        }
        
        // 定期输出碰撞保持状态
        if (Time.frameCount % 30 == 0)  // 每30帧输出一次
        {
            Debug.Log($"<color=yellow>★ OnCollisionStay: {objectName}, lastContactedObjects: {lastContactedObjects.Count}</color>");
        }
    }

    // 处理碰撞退出
    void OnCollisionExit(Collision collision)
    {
        if (collision == null) return;
        
        Debug.Log($"<color=cyan>★ COLLISION EXIT: {collision.gameObject.name}</color>");
        
        // 从活动碰撞列表中移除
        activeCollisions.Remove(collision);
        Debug.Log($"<color=cyan>★ Active collisions remaining: {activeCollisions.Count}</color>");
        
        // 如果没有任何活动碰撞，离开着陆状态
        if (activeCollisions.Count == 0)
        {
            if (isLanded)
            {
                isLanded = false;
                OnTookOff?.Invoke();
                Debug.Log("<color=cyan>★ LEFT LANDING STATE - No active collisions</color>");
            }
        }
    }
    
    // 每帧检查：如果火箭完全离地且速度足够，清空接触历史（开始新的飞行段）
    void FixedUpdate()
    {
        // 如果没有任何碰撞，且速度超过阈值，清空历史记录（表示已经起飞）
        if (activeCollisions.Count == 0 && lastContactedObjects.Count > 0)
        {
            float currentSpeed = GetCurrentSpeed();
            const float takeoffSpeedThreshold = 1f;  // 速度超过1m/s认为已起飞
            
            if (currentSpeed > takeoffSpeedThreshold)
            {
                Debug.Log($"<color=magenta>★ CLEARED contact history (speed: {currentSpeed:F2} m/s, starting new flight segment)</color>");
                lastContactedObjects.Clear();
                contactedObjectTags.Clear();
            }
        }
    }
    
    // 对所有子物体施加爆炸力
    void ApplyExplosionForce(Vector3 explosionCenter)
    {
        if (childRigidbodies == null || childRigidbodies.Length == 0) return;
        
        foreach (Rigidbody childRb in childRigidbodies)
        {
            if (childRb != null)
            {
                childRb.AddExplosionForce(explosionForce, explosionCenter, explosionRadius);
                Debug.Log($"Applied explosion force to {childRb.gameObject.name}");
            }
        }
    }

    // 判定撞击类型
    string DetermineCrashType(string objectName, string objectTag)
    {
        if (objectName == "LaunchingPad")
            return "03";
        else if (objectName == "LandingPad")
            return "04";
        else if (objectTag == "Terrain")
            return "05";
        else
            return "00";
    }

    // 检查着陆条件
    public LandingInfo CheckLandingStatus()
    {
        LandingInfo info = new LandingInfo();
        
        // 是否有活动碰撞
        info.isLanded = (activeCollisions.Count > 0);
        info.currentSpeed = GetCurrentSpeed();
        info.lastFrameSpeed = lastFrameSpeed;
        // 返回所有接触过的物体（历史记录，用于判定结局类型）
        info.contactedObjects = new List<string>(lastContactedObjects);
        
        return info;
    }

    // 重置状态
    public void ResetState()
    {
        lastContactedObjects.Clear();
        contactedObjectTags.Clear();
        lastFrameSpeed = 0f;
        isLanded = false;
    }

    // 获取接触的物体列表
    public List<string> GetContactedObjects()
    {
        return new List<string>(lastContactedObjects);
    }

    // 获取接触物体的Tag
    public string GetContactedObjectTag(string objectName)
    {
        if (contactedObjectTags.ContainsKey(objectName))
        {
            return contactedObjectTags[objectName];
        }
        return "";
    }

    // 是否接触指定物体
    public bool IsContactingObject(string objectName)
    {
        return lastContactedObjects.Contains(objectName);
    }
}

// 着陆信息结构
public struct LandingInfo
{
    public bool isLanded;
    public float currentSpeed;
    public float lastFrameSpeed;
    public List<string> contactedObjects;
}
