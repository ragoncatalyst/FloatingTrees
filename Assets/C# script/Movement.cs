using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Movement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float mainThrust = 100f;            // 主推进力
    [SerializeField] private float horizontalMoveForce = 50f;   // 水平移动力
    [SerializeField] private AudioClip mainEngine;
    
    [Header("Particle Effects")]
    [SerializeField] private ParticleSystem mainEngineParticles;
    
    [Header("Explosion Settings")]
    [SerializeField] float explosionForceMultiplier = 50f;  // 爆炸力系数（力 = 速度 × 系数）
    [SerializeField] float debrisMass = 10f;                // 碎片质量
    [SerializeField] float debrisDrag = 0.1f;               // 碎片线性阻力
    [SerializeField] float debrisAngularDrag = 0.3f;        // 碎片角阻力
    
    private Rigidbody parentRigidbody;                    // 父物体的Rigidbody（用于驱动整体运动）
    private Rigidbody[] childRigidbodies;                 // 所有子物体的Rigidbody（用于碰撞检测）
    private Vector3[] initialLocalPositions;              // 初始相对位置
    private Quaternion[] initialLocalRotations;           // 初始相对旋转
    private AudioSource myAudioSource;
    private CamaraFollow cameraFollow;                    // 摄像头脚本（用于获取当前角度）
    
    // 输入状态缓存
    private bool isThrustingThisFrame;
    private bool isMovingForward;
    private bool isMovingBack;
    private bool isMovingLeft;
    private bool isMovingRight;
    
    // 追踪玩家是否操控过火箭
    private bool hasPlayerControlled = false;

    // Start is called before the first frame update
    void Start()
    {
        // 验证并修正参数
        if (mainThrust <= 0)
        {
            mainThrust = 100f;
            Debug.LogWarning("[Movement] mainThrust未设置，使用默认值100");
        }
        
        if (horizontalMoveForce <= 0)
        {
            horizontalMoveForce = 50f;
            Debug.LogWarning("[Movement] horizontalMoveForce未设置，使用默认值50");
        }
        
        Debug.Log($"[Movement] 参数已初始化 - mainThrust:{mainThrust}, horizontalMoveForce:{horizontalMoveForce}");
        
        // 获取父物体的Rigidbody（必须存在）
        parentRigidbody = GetComponent<Rigidbody>();
        if (parentRigidbody == null)
        {
            Debug.LogError("Parent 'Rocket' object must have a Rigidbody component!");
            return;
        }
        
        // 获取所有子物体中有Renderer的方块（排除空的Layer容器）
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
        List<Transform> childTransforms = new List<Transform>();
        
        foreach (Renderer renderer in allRenderers)
        {
            Transform child = renderer.transform;
            if (child != transform && !childTransforms.Contains(child))  // 排除父物体本身，避免重复
            {
                childTransforms.Add(child);
            }
        }
        
        // 记录所有子物体的初始相对位置和旋转
        initialLocalPositions = new Vector3[childTransforms.Count];
        initialLocalRotations = new Quaternion[childTransforms.Count];
        childRigidbodies = new Rigidbody[childTransforms.Count];
        
        for (int i = 0; i < childTransforms.Count; i++)
        {
            initialLocalPositions[i] = childTransforms[i].localPosition;
            initialLocalRotations[i] = childTransforms[i].localRotation;
            childRigidbodies[i] = childTransforms[i].GetComponent<Rigidbody>();
            
            // 如果子物体有Rigidbody（不应该有），移除它
            if (childRigidbodies[i] != null)
            {
                Debug.LogWarning($"Child '{childTransforms[i].name}' has a Rigidbody which is not needed before explosion!");
            }
            
            // 确保每个方块都有Collider
            BoxCollider childCollider = childTransforms[i].GetComponent<BoxCollider>();
            if (childCollider == null)
            {
                childCollider = childTransforms[i].gameObject.AddComponent<BoxCollider>();
                Debug.Log($"<color=green>★ Added BoxCollider to '{childTransforms[i].name}'</color>");
            }
            
            // 添加碰撞转发器，将碰撞事件转发给父物体
            ChildCollisionForwarder forwarder = childTransforms[i].GetComponent<ChildCollisionForwarder>();
            if (forwarder == null)
            {
                forwarder = childTransforms[i].gameObject.AddComponent<ChildCollisionForwarder>();
                forwarder.SetParent(this.gameObject);
                Debug.Log($"<color=green>★ Added ChildCollisionForwarder to '{childTransforms[i].name}'</color>");
            }
        }
        
        myAudioSource = GetComponent<AudioSource>();
        
        // 获取摄像头脚本（用于获取当前角度索引）
        cameraFollow = Camera.main?.GetComponent<CamaraFollow>();
        if (cameraFollow == null)
        {
            Debug.LogWarning("未找到CamaraFollow脚本，WASD移动将使用默认方向");
        }
        
        // 父物体不需要Collider，碰撞由子方块处理
        Collider parentCollider = GetComponent<Collider>();
        if (parentCollider != null)
        {
            Destroy(parentCollider);
            Debug.Log($"<color=yellow>★ Removed parent Collider - using individual block colliders instead</color>");
        }
        
        // 验证Rigidbody配置
        if (parentRigidbody != null)
        {
            Debug.Log($"<color=green>★ Parent Rigidbody: isKinematic={parentRigidbody.isKinematic}, useGravity={parentRigidbody.useGravity}, drag={parentRigidbody.drag}, angularDrag={parentRigidbody.angularDrag}</color>");
        }
        
        if (childTransforms.Count == 0)
        {
            Debug.LogError("No block objects found! Make sure your rocket has visible blocks with Renderers.");
        }
        else
        {
            Debug.Log($"<color=green>★ Found {childTransforms.Count} block objects (5x5x5 structure support)</color>");
        }
    }

    // Update is called once per frame
    void Update()
    {
        ProcessInput();
    }

    // FixedUpdate用于物理计算，保证稳定性
    void FixedUpdate()
    {
        ProcessThrust();
        ProcessHorizontalMovement();
        SynchronizeChildRigidbodies();  // 同步所有子物体，保持粘合状态
    }
    
    // 同步所有方块位置和旋转（保持粘合状态）
    // 只同步实际方块，不修改Layer容器
    void SynchronizeChildRigidbodies()
    {
        // 如果爆炸已发生（childRigidbodies被清空），停止同步
        if (childRigidbodies.Length == 0) return;
        
        // 获取所有方块并确保它们保持初始相对位置和旋转
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>();
        int index = 0;
        
        foreach (Renderer renderer in allRenderers)
        {
            Transform child = renderer.transform;
            if (child != transform && index < initialLocalPositions.Length)
            {
                child.localPosition = initialLocalPositions[index];
                child.localRotation = initialLocalRotations[index];
                index++;
            }
        }
    }

    void ProcessInput()
    {
        // 缓存输入状态
        isThrustingThisFrame = Input.GetKey(KeyCode.Space);
        isMovingForward = Input.GetKey(KeyCode.W);
        isMovingBack = Input.GetKey(KeyCode.S);
        isMovingLeft = Input.GetKey(KeyCode.A);
        isMovingRight = Input.GetKey(KeyCode.D);
        
        // 标记玩家是否操控过火箭
        if (isThrustingThisFrame || isMovingForward || isMovingBack || isMovingLeft || isMovingRight)
        {
            hasPlayerControlled = true;
        }
        
        // 在Update中处理音效和粒子效果（非物理部分）
        if (isThrustingThisFrame)
        {
            // 播放推进音效
            if (myAudioSource != null && mainEngine != null && !myAudioSource.isPlaying)
            {
                myAudioSource.PlayOneShot(mainEngine);
            }
            
            // 播放主引擎粒子效果
            if (mainEngineParticles != null && !mainEngineParticles.isPlaying)
            {
                mainEngineParticles.Play();
            }
        }
        else
        {
            // 释放空格键，停止音效和粒子
            if (myAudioSource != null)
            {
                myAudioSource.Stop();
            }
            if (mainEngineParticles != null)
            {
                mainEngineParticles.Stop();
            }
        }
    }

    void ProcessThrust()
    {
        if (isThrustingThisFrame)
        {
            // 对父物体施加推力（驱动整体运动）
            if (parentRigidbody != null)
            {
                parentRigidbody.AddRelativeForce(Vector3.up * mainThrust);
            }
        }
    }

    void ProcessHorizontalMovement()
    {
        if (parentRigidbody == null)
        {
            Debug.LogError("[Movement] parentRigidbody为null！");
            return;
        }
        
        // 检查是否有WASD输入
        bool hasMovementInput = isMovingForward || isMovingBack || isMovingLeft || isMovingRight;
        
        // 获取摄像头角度索引
        int angleIndex = cameraFollow != null ? cameraFollow.GetCurrentAngleIndex() : 0;
        
        // 根据角度索引和WASD输入计算移动方向（世界坐标系）
        Vector3 moveDirection = Vector3.zero;
        
        switch (angleIndex)
        {
            case 0: // 0°视角
                if (isMovingForward) moveDirection.z += 1f;
                if (isMovingBack) moveDirection.z -= 1f;
                if (isMovingLeft) moveDirection.x -= 1f;
                if (isMovingRight) moveDirection.x += 1f;
                break;
                
            case 1: // 90°视角
                if (isMovingForward) moveDirection.x += 1f;
                if (isMovingBack) moveDirection.x -= 1f;
                if (isMovingLeft) moveDirection.z += 1f;
                if (isMovingRight) moveDirection.z -= 1f;
                break;
                
            case 2: // 180°视角
                if (isMovingForward) moveDirection.z -= 1f;
                if (isMovingBack) moveDirection.z += 1f;
                if (isMovingLeft) moveDirection.x += 1f;
                if (isMovingRight) moveDirection.x -= 1f;
                break;
                
            case 3: // 270°视角
                if (isMovingForward) moveDirection.x -= 1f;
                if (isMovingBack) moveDirection.x += 1f;
                if (isMovingLeft) moveDirection.z -= 1f;
                if (isMovingRight) moveDirection.z += 1f;
                break;
        }
        
        // 施加水平移动力
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Vector3 force = moveDirection.normalized * horizontalMoveForce;
            parentRigidbody.AddForce(force, ForceMode.Force);
            
            // 每秒打印一次调试信息
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"<color=green>[Movement] 施加水平力 - 角度:{angleIndex} | 方向:{moveDirection.normalized} | 力度:{horizontalMoveForce} | 总力:{force}</color>");
            }
        }
        else if (hasMovementInput && Time.frameCount % 60 == 0)
        {
            Debug.LogWarning($"[Movement] 检测到WASD输入但moveDirection为0 - W:{isMovingForward} S:{isMovingBack} A:{isMovingLeft} D:{isMovingRight}");
        }
    }
    
    // 公共方法：供其他类调用，检查玩家是否操控过火箭
    public bool HasPlayerControlled()
    {
        return hasPlayerControlled;
    }
    
    // 公共方法：爆炸时调用，为所有子方块添加Rigidbody并使其动态
    public void DetachChildRigidbodies(float impactSpeed)
    {
        Debug.Log($"<color=red>★★★ DETACHING CHILD RIGIDBODIES - EXPLOSION! Impact Speed: {impactSpeed:F2} m/s ★★★</color>");
        
        // 根据撞击速度计算爆炸力和扭矩
        float calculatedExplosionForce = impactSpeed * explosionForceMultiplier;
        float calculatedTorque = impactSpeed * 10f;  // 扭矩也与速度成正比
        
        Debug.Log($"<color=red>★ Calculated explosion force: {calculatedExplosionForce:F0}, torque: {calculatedTorque:F0}</color>");
        
        // 禁用父物体的Rigidbody（不再控制整体）
        if (parentRigidbody != null)
        {
            parentRigidbody.isKinematic = true;
            parentRigidbody.useGravity = false;
            Debug.Log("★ Parent Rigidbody set to kinematic");
        }
        
        // 为每个可见方块添加物理组件并施加爆炸力（只处理有Renderer的方块）
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            Transform child = renderer.transform;
            if (child == transform) continue;  // 跳过父物体本身
            
            // 添加Rigidbody
            Rigidbody childRb = child.GetComponent<Rigidbody>();
            if (childRb == null)
            {
                childRb = child.gameObject.AddComponent<Rigidbody>();
                Debug.Log($"<color=yellow>★ Added Rigidbody to {child.name}</color>");
            }
            
            // 设置物理属性
            childRb.isKinematic = false;
            childRb.useGravity = true;
            childRb.mass = debrisMass;
            childRb.drag = debrisDrag;
            childRb.angularDrag = debrisAngularDrag;
            childRb.collisionDetectionMode = CollisionDetectionMode.Continuous;  // 防止高速穿透
            childRb.interpolation = RigidbodyInterpolation.Interpolate;  // 平滑运动
            
            Debug.Log($"<color=cyan>★ {child.name} Rigidbody configured: mass={childRb.mass}, useGravity={childRb.useGravity}, isKinematic={childRb.isKinematic}</color>");
            
            // 继承父物体的速度
            if (parentRigidbody != null)
            {
                childRb.velocity = parentRigidbody.velocity;
                childRb.angularVelocity = parentRigidbody.angularVelocity;
                Debug.Log($"<color=cyan>★ {child.name} inherited velocity: {childRb.velocity.magnitude:F2} m/s</color>");
            }
            
            // 添加Collider（如果没有）
            BoxCollider childCollider = child.GetComponent<BoxCollider>();
            if (childCollider == null)
            {
                childCollider = child.gameObject.AddComponent<BoxCollider>();
                Debug.Log($"<color=yellow>★ Added BoxCollider to {child.name}</color>");
            }
            
            // 确保Collider设置正确
            childCollider.isTrigger = false;
            
            // 重置Collider为默认大小（适配MeshRenderer/Renderer）
            childCollider.center = Vector3.zero;
            childCollider.size = Vector3.one;
            
            // 强制唤醒Rigidbody，确保物理计算立即生效
            childRb.WakeUp();
            
            // 设置碰撞检测模式为Continuous，防止高速穿透
            childRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            
            Debug.Log($"<color=green>★ {child.name} Physics Setup Complete - Mass: {childRb.mass}, Collider: {childCollider.size}, isTrigger: {childCollider.isTrigger}</color>");
            
            // 施加爆炸力（基于撞击速度）
            Vector3 explosionDirection = (child.position - transform.position).normalized;
            if (explosionDirection.magnitude < 0.1f)
            {
                // 如果方块在中心，随机一个方向
                explosionDirection = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized;
            }
            
            // 添加随机偏移（±20%）使爆炸更自然
            float randomFactor = UnityEngine.Random.Range(0.8f, 1.2f);
            float explosionForce = calculatedExplosionForce * randomFactor;
            childRb.AddForce(explosionDirection * explosionForce, ForceMode.Impulse);
            
            // 添加随机旋转力（基于速度）
            Vector3 randomTorque = new Vector3(
                UnityEngine.Random.Range(-calculatedTorque, calculatedTorque),
                UnityEngine.Random.Range(-calculatedTorque, calculatedTorque),
                UnityEngine.Random.Range(-calculatedTorque, calculatedTorque)
            );
            childRb.AddTorque(randomTorque, ForceMode.Impulse);
            
            Debug.Log($"<color=red>★ {child.name} EXPLODED! Force: {explosionForce:F0}, Direction: {explosionDirection}</color>");
        }
        
        // 停止同步子物体（不再调用SynchronizeChildRigidbodies）
        childRigidbodies = new Rigidbody[0];
        
        Debug.Log($"<color=red>★★★ EXPLOSION COMPLETE! {renderers.Length} blocks scattered! ★★★</color>");
    }
}
