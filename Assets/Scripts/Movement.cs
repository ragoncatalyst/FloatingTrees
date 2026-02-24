using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Movement : MonoBehaviour
{
    // ========================================
    // 性能优化说明：
    // 1. 移除了每帧同步方块位置的SynchronizeChildRigidbodies调用
    // 2. 方块作为Rocket的子对象，会自动通过Unity的Transform父子关系保持相对位置
    // 3. 使用字典存储Transform引用，避免索引错乱问题
    // 4. 只在爆炸时才解除父子关系并应用物理
    // ========================================
    
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

    [Header("Audio")]
    [Tooltip("可选：爆炸音效列表，运行时会在这些音效中随机选一个播放。留空则不播放声音。")]
    [SerializeField] private AudioClip[] explosionClips;
    [Range(0f,1f)]
    [SerializeField] private float explosionVolume = 1f;

    private Rigidbody parentRigidbody;                    // 父物体的Rigidbody（用于驱动整体运动）
    private Rigidbody[] childRigidbodies;                 // 所有子物体的Rigidbody（用于碰撞检测）
    
    // 使用字典存储每个方块的初始位置，key是方块的Transform引用
    private Dictionary<Transform, Vector3> initialLocalPositions = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Quaternion> initialLocalRotations = new Dictionary<Transform, Quaternion>();
    
    private AudioSource myAudioSource;
    private CamaraFollow cameraFollow;                    // 摄像头脚本（用于获取当前角度）
    
    // 运行时状态守护：防止重复触发爆炸/恢复
    private bool isExplodedRuntime = false;

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
        // 重要：使用true参数包括被禁用的GameObject
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        List<Transform> childTransforms = new List<Transform>();
        
        foreach (Renderer renderer in allRenderers)
        {
            Transform child = renderer.transform;
            if (child != transform && !childTransforms.Contains(child))  // 排除父物体本身，避免重复
            {
                childTransforms.Add(child);
                
                // 只记录初始位置和旋转，不每帧强制同步
                // 方块作为子对象会自动跟随父物体运动
                initialLocalPositions[child] = child.localPosition;
                initialLocalRotations[child] = child.localRotation;
            }
        }
        
        // 初始化子物体Rigidbody数组（爆炸时才会创建）
        childRigidbodies = new Rigidbody[0];
        
        // 为所有方块添加Collider和碰撞转发器
        foreach (Transform child in childTransforms)
        {
            // Skip the AimingSphere explicitly — it should keep its own SphereCollider / trigger behavior
            if (child.gameObject.name.Equals("AimingSphere"))
                continue;

            // 确保每个方块都有Collider
            BoxCollider childCollider = child.GetComponent<BoxCollider>();
            if (childCollider == null)
            {
                childCollider = child.gameObject.AddComponent<BoxCollider>();
            }

            // 添加碰撞转发器，将碰撞事件转发给父物体
            ChildCollisionForwarder forwarder = child.GetComponent<ChildCollisionForwarder>();
            if (forwarder == null)
            {
                forwarder = child.gameObject.AddComponent<ChildCollisionForwarder>();
                forwarder.SetParent(this.gameObject);
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
        }
        
        if (childTransforms.Count == 0)
        {
            Debug.LogError("[Movement] No block objects found!");
        }
        else
        {
            Debug.Log($"[Movement] Initialized {childTransforms.Count} blocks");
        }
    }

    // Ensure all child blocks have Colliders + collision forwarders (can be called at runtime by other systems)
    public void EnsureCollidersExist()
    {
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in allRenderers)
        {
            Transform child = renderer.transform;
            if (child == transform) continue;

            // Skip AimingSphere entirely — do not add/modify colliders or forwarders for it
            if (child.gameObject.name.Equals("AimingSphere"))
                continue;

            // 添加或修复 BoxCollider
            BoxCollider childCollider = child.GetComponent<BoxCollider>();
            if (childCollider == null)
            {
                childCollider = child.gameObject.AddComponent<BoxCollider>();
                childCollider.isTrigger = false;
            }
            else
            {
                childCollider.isTrigger = false;
                childCollider.enabled = true;
            }

            // 添加或修复碰撞转发器
            ChildCollisionForwarder forwarder = child.GetComponent<ChildCollisionForwarder>();
            if (forwarder == null)
            {
                forwarder = child.gameObject.AddComponent<ChildCollisionForwarder>();
                forwarder.SetParent(this.gameObject);
            }
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
        // 注意：不需要每帧同步子物体位置，Unity的Transform父子关系会自动保持相对位置
        // 只有在爆炸时才需要解除父子关系并应用物理
    }
    
    // 注意：移除了SynchronizeChildRigidbodies方法
    // 方块作为Rocket的子对象，会自动通过Unity的Transform父子关系保持相对位置
    // 不需要每帧手动同步，这样可以大幅提升性能

    void ProcessInput()
    {
        // 若商店正在打开，则不响应任何输入
        if (TradingSystem.shopOpen) return;

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
            // 对父物体施加推力（驱动整体运动）- 始终向世界坐标系的上方
            if (parentRigidbody != null)
            {
                parentRigidbody.AddForce(Vector3.up * mainThrust);
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
        
        Vector3 moveDirection = Vector3.zero;
        // movement is always oriented relative to the camera's horizontal plane
        Vector3 camFwd = Camera.main ? Camera.main.transform.forward : Vector3.forward;
        camFwd.y = 0f;
        camFwd.Normalize();
        Vector3 camRight = Vector3.Cross(Vector3.up, camFwd);

        if (isMovingForward) moveDirection += camFwd;
        if (isMovingBack)    moveDirection -= camFwd;
        if (isMovingLeft)    moveDirection -= camRight;
        if (isMovingRight)   moveDirection += camRight;

        // debug text no longer needs angleIndex
        
        // 施加水平移动力
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Vector3 force = moveDirection.normalized * horizontalMoveForce;
            parentRigidbody.AddForce(force, ForceMode.Force);
            
            // 每秒打印一次调试信息
            if (Time.frameCount % 60 == 0)
            {
                string mode = (cameraFollow != null && cameraFollow.IsInShoulderMode()) ? "shoulder" : "normal";
                Debug.Log($"<color=green>[Movement] 施加水平力 - 模式:{mode} | 方向:{moveDirection.normalized} | 力度:{horizontalMoveForce} | 总力:{force}</color>");
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
        // Guard: only allow the explosion sequence to run once until RecoverFromExplosion resets state
        if (isExplodedRuntime)
        {
            Debug.Log("[Movement] DetachChildRigidbodies: already exploded — ignoring duplicate trigger");
            return;
        }
        isExplodedRuntime = true;

        Debug.Log($"<color=red>★★★ DETACHING CHILD RIGIDBODIES - EXPLOSION! Impact Speed: {impactSpeed:F2} m/s ★★★</color>");

        // Spawn visual explosion effect (Minecraft-like)
        // reduce visual shard count to avoid large per-frame allocations on explosion
        BlockExplosionEffect.SpawnExplosion(transform.position, shardCount: 18, spread: 4f, shardSize: 0.10f, force: 8f, lifetime: 2.0f);

        // 保障性调用：确保所有子碰撞体存在（避免后续重复创建代码）
        EnsureCollidersExist();

        // 立即记录爆炸发生的精确变换（确保 RocketStateManager 有可靠的爆炸源位置信息）
        try
        {
            RocketStateManager.MarkExplodedAt(transform.position, transform.rotation);
            Debug.Log("[Movement] Marked explosion origin on RocketStateManager");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Movement] Failed to mark explosion origin: {e.Message}");
        }

        // 播放爆炸音效（在多个导入的音效中随机选择一个）
        if (explosionClips != null && explosionClips.Length > 0)
        {
            AudioClip clip = explosionClips[UnityEngine.Random.Range(0, explosionClips.Length)];
            if (clip != null)
            {
                if (myAudioSource != null)
                {
                    myAudioSource.PlayOneShot(clip, explosionVolume);
                }
                else
                {
                    // 回退到静态播放以保证能听到声音（3D 空间位置在火箭处）
                    AudioSource.PlayClipAtPoint(clip, transform.position, explosionVolume);
                }

                Debug.Log($"[Movement] Played explosion clip: {clip.name}");
            }
        }

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
            
            // Collider 已由 EnsureCollidersExist() 保障——仅校准参数
            BoxCollider childCollider = child.GetComponent<BoxCollider>();
            if (childCollider != null)
            {
                // 不要改动 AimingSphere 的 isTrigger
                if (!child.gameObject.name.Equals("AimingSphere"))
                    childCollider.isTrigger = false;
                childCollider.center = Vector3.zero;
                childCollider.size = Vector3.one;
            }
            
            // 强制唤醒Rigidbody，确保物理计算立即生效
            childRb.WakeUp();
            
            // 设置碰撞检测模式为Continuous，防止高速穿透
            childRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            
            Debug.Log($"<color=green>★ {child.name} Physics Setup Complete - Mass: {childRb.mass}, Collider: {childCollider.size}, isTrigger: {childCollider.isTrigger}</color>");
            
            // 施加爆炸力（基于撞击速度）
            Vector3 explosionDirection = (child.position - transform.position).normalized;
            
            // 如果方块离中心太近（比如0.5米），随机一个方向
            // 这对单个方块特别重要
            if ((child.position - transform.position).magnitude < 0.5f)
            {
                explosionDirection = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(0.2f, 0.6f),  // 轻微向上偏向
                    UnityEngine.Random.Range(-1f, 1f)
                ).normalized;
                Debug.Log($"<color=magenta>★ {child.name} 离中心太近，使用随机爆炸方向: {explosionDirection}</color>");
            }
            
            // 添加随机偏移（±20%）使爆炸更自然
            float randomFactor = UnityEngine.Random.Range(0.8f, 1.2f);
            float explosionForce = calculatedExplosionForce * randomFactor;
            
            // 确保有最小爆炸力（即使速度很小），但不要太大
            float minExplosionForce = 100f;  // 降低最小爆炸力到100
            if (explosionForce < minExplosionForce)
            {
                explosionForce = minExplosionForce;
                Debug.Log($"<color=yellow>★ {child.name} 爆炸力太小，使用最小值: {minExplosionForce}</color>");
            }
            
            childRb.AddForce(explosionDirection * explosionForce, ForceMode.Impulse);
            
            // 添加轻微的向上力（降低到10%）
            Vector3 upwardForce = Vector3.up * (explosionForce * 0.1f);
            childRb.AddForce(upwardForce, ForceMode.Impulse);
            
            // 添加随机旋转力（基于速度）
            float minTorque = 20f;  // 降低最小扭矩到20
            float actualTorque = Mathf.Max(calculatedTorque, minTorque);
            Vector3 randomTorque = new Vector3(
                UnityEngine.Random.Range(-actualTorque, actualTorque),
                UnityEngine.Random.Range(-actualTorque, actualTorque),
                UnityEngine.Random.Range(-actualTorque, actualTorque)
            );
            childRb.AddTorque(randomTorque, ForceMode.Impulse);
            
            Debug.Log($"<color=red>★ {child.name} EXPLODED! Force: {explosionForce:F0}, Direction: {explosionDirection}, Upward: {upwardForce.magnitude:F0}, Torque: {actualTorque:F0}</color>");
        }
        
        // 停止同步子物体（不再调用SynchronizeChildRigidbodies）
        childRigidbodies = new Rigidbody[0];
        
        Debug.Log($"<color=red>★★★ EXPLOSION COMPLETE! {renderers.Length} blocks scattered! ★★★</color>");

        // 通知 RocketStateManager：发生了爆炸（以便下一次返回 Main 时使用安全点复位）
        try
        {
            RocketStateManager.MarkExploded();
            RocketStateManager.Save();
            RocketStateManager.TriggerExplosionRecoverySequence();
            Debug.Log("[Movement] Notified RocketStateManager of explosion, persisted state and started recovery sequence");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Movement] Failed to notify RocketStateManager about explosion: {e.Message}");
        }
    }

    // 恢复爆炸后的可玩性：移除子刚体、重置子变换、恢复父刚体与输入状态、重建碰撞转发器
    public void RecoverFromExplosion()
    {
        Debug.Log("[Movement] RecoverFromExplosion: restoring playable state...");

        // 停止粒子与音频
        if (mainEngineParticles != null) mainEngineParticles.Stop();
        if (myAudioSource != null) myAudioSource.Stop();

        // 删除运行时添加的子刚体（保留父刚体）
        var childRbs = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in childRbs)
        {
            if (rb == parentRigidbody) continue;
            Destroy(rb);
        }

        // 恢复子物体初始局部变换并确保Collider/Forwarder存在
        foreach (var kvp in initialLocalPositions)
        {
            Transform child = kvp.Key;
            if (child == null) continue;
            child.localPosition = initialLocalPositions[child];
            if (initialLocalRotations.ContainsKey(child))
                child.localRotation = initialLocalRotations[child];

            // 确保子物体有碰撞体与转发器（使用统一 helper）
            EnsureCollidersExist();
            ChildCollisionForwarder f = child.GetComponent<ChildCollisionForwarder>();
            if (f == null) f = child.gameObject.AddComponent<ChildCollisionForwarder>();
            f.SetParent(this.gameObject);
        }

        // 恢复父刚体
        if (parentRigidbody != null)
        {
            parentRigidbody.isKinematic = false;
            parentRigidbody.useGravity = true;
            parentRigidbody.velocity = Vector3.zero;
            parentRigidbody.angularVelocity = Vector3.zero;
            parentRigidbody.WakeUp();
        }

        // 重置内部状态
        childRigidbodies = new Rigidbody[0];
        hasPlayerControlled = false;
        this.enabled = true;

        // 确保摄像头指向火箭并刷新一次角度
        CamaraFollow cam = Camera.main?.GetComponent<CamaraFollow>();
        if (cam != null) cam.SetCameraAngle(cam.GetCurrentAngleIndex());

        Debug.Log("[Movement] RecoverFromExplosion: playable state restored");

        // Allow future explosions again
        isExplodedRuntime = false;
    }
}

