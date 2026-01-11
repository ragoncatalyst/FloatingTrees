using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Movement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] float mainThrust = 100f;
    [SerializeField] float rotationThrust = 10f;          // 旋转力矩
    [SerializeField] AudioClip mainEngine;
    
    [Header("Particle Effects")]
    [SerializeField] ParticleSystem mainEngineParticles;
    [SerializeField] ParticleSystem leftThrusterParticle;
    [SerializeField] ParticleSystem rightThrusterParticle;
    
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
    
    // 输入状态缓存
    private bool isThrustingThisFrame;
    private bool isRotatingLeftThisFrame;
    private bool isRotatingRightThisFrame;
    
    // 追踪玩家是否操控过火箭
    private bool hasPlayerControlled = false;

    // Start is called before the first frame update
    void Start()
    {
        // 获取父物体的Rigidbody（必须存在）
        parentRigidbody = GetComponent<Rigidbody>();
        if (parentRigidbody == null)
        {
            Debug.LogError("Parent 'Rocket' object must have a Rigidbody component!");
            return;
        }
        
        // 获取所有直接子物体（不获取Rigidbody，因为子物体不需要有）
        Transform[] allChildren = GetComponentsInChildren<Transform>();
        List<Transform> childTransforms = new List<Transform>();
        
        foreach (Transform child in allChildren)
        {
            if (child != transform)  // 排除父物体本身
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
            
            // 移除子物体的所有Collider（避免干扰父物体碰撞）
            Collider[] childColliders = childTransforms[i].GetComponents<Collider>();
            foreach (Collider col in childColliders)
            {
                Destroy(col);
                Debug.Log($"<color=red>★ Removed Collider from child '{childTransforms[i].name}'</color>");
            }
        }
        
        myAudioSource = GetComponent<AudioSource>();
        
        // 确保父物体有一个Collider来产生物理碰撞
        Collider parentCollider = GetComponent<Collider>();
        if (parentCollider == null)
        {
            // 添加一个BoxCollider给父物体（用于物理碰撞）
            BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.isTrigger = false;  // 不是Trigger，产生真实碰撞
            
            // 计算包含所有子物体的边界
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            foreach (Transform child in childTransforms)
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            
            // 设置Collider大小和位置
            boxCollider.center = bounds.center - transform.position;
            boxCollider.size = bounds.size;
            
            Debug.Log($"<color=green>★ Added BoxCollider to parent 'Rocket' for physics collision</color>");
            Debug.Log($"<color=green>  - Center: {boxCollider.center}, Size: {boxCollider.size}</color>");
        }
        else if (parentCollider.isTrigger)
        {
            parentCollider.isTrigger = false;
            Debug.Log($"<color=green>★ Set parent Collider to non-Trigger for physics collision</color>");
        }
        else
        {
            Debug.Log($"<color=green>★ Parent already has non-Trigger Collider: {parentCollider.GetType().Name}</color>");
        }
        
        // 验证Rigidbody配置
        if (parentRigidbody != null)
        {
            Debug.Log($"<color=green>★ Parent Rigidbody: isKinematic={parentRigidbody.isKinematic}, useGravity={parentRigidbody.useGravity}, drag={parentRigidbody.drag}, angularDrag={parentRigidbody.angularDrag}</color>");
        }
        
        if (childTransforms.Count == 0)
        {
            Debug.LogError("No child objects found!");
        }
        else
        {
            Debug.Log($"<color=green>★ Found {childTransforms.Count} child objects</color>");
        }
        if (childRigidbodies.Length == 0)
        {
            Debug.LogError("No child Rigidbodies found! Make sure child cubes have Rigidbody components.");
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
        ProcessRotation();
        SynchronizeChildRigidbodies();  // 同步所有子物体，保持粘合状态
    }
    
    // 同步所有子物体位置和旋转（保持粘合状态）
    // 子物体跟随父物体的transform
    void SynchronizeChildRigidbodies()
    {
        // 如果爆炸已发生（childRigidbodies被清空），停止同步
        if (childRigidbodies.Length == 0) return;
        
        // 确保所有子物体保持初始相对位置和旋转
        for (int i = 0; i < transform.childCount && i < initialLocalPositions.Length; i++)
        {
            Transform child = transform.GetChild(i);
            child.localPosition = initialLocalPositions[i];
            child.localRotation = initialLocalRotations[i];
        }
    }

    void ProcessInput()
    {
        // 缓存输入状态
        isThrustingThisFrame = Input.GetKey(KeyCode.Space);
        isRotatingLeftThisFrame = Input.GetKey(KeyCode.A);
        isRotatingRightThisFrame = Input.GetKey(KeyCode.D);
        
        // 标记玩家是否操控过火箭
        if (isThrustingThisFrame || isRotatingLeftThisFrame || isRotatingRightThisFrame)
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

        // 处理左推进器粒子
        if (isRotatingLeftThisFrame)
        {
            if (leftThrusterParticle != null && !leftThrusterParticle.isPlaying)
            {
                leftThrusterParticle.Play();
            }
        }
        else
        {
            if (leftThrusterParticle != null)
            {
                leftThrusterParticle.Stop();
            }
        }

        // 处理右推进器粒子
        if (isRotatingRightThisFrame)
        {
            if (rightThrusterParticle != null && !rightThrusterParticle.isPlaying)
            {
                rightThrusterParticle.Play();
            }
        }
        else
        {
            if (rightThrusterParticle != null)
            {
                rightThrusterParticle.Stop();
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

    void ProcessRotation()
    {
        if (parentRigidbody == null) return;
        
        // A键控制左转
        if (isRotatingLeftThisFrame)
        {
            parentRigidbody.AddRelativeTorque(Vector3.forward * rotationThrust);
        }

        // D键控制右转
        if (isRotatingRightThisFrame)
        {
            parentRigidbody.AddRelativeTorque(-Vector3.forward * rotationThrust);
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
        
        // 为每个子方块添加物理组件并施加爆炸力
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            
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
        
        Debug.Log("<color=red>★★★ EXPLOSION COMPLETE! ★★★</color>");
    }
}
