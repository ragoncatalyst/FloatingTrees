using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamaraFollow : MonoBehaviour
{
    [Header("目标设置")]
    [SerializeField] private Transform target; // 要跟随的目标（火箭）
    
    [Header("摄像头设置")]
    [SerializeField] private float baseDistance = 20f;              // 基础距离
    [SerializeField] private float minDistance = 10f;               // 最小距离
    [SerializeField] private float maxDistance = 30f;               // 最大距离
    [SerializeField] private float scrollSensitivity = 2f;          // 滚轮灵敏度
    [SerializeField] private float rotationTransitionTime = 0.3f;   // 角度切换过渡时间
    [SerializeField] private float waitListTimeout = 0.2f;          // 等待列表超时时间（秒）
    [SerializeField] private float shoulderOffset = 0f;             // 肩部侧移，正为右肩，负为左肩
    [SerializeField] private float shoulderHeight = 2f;             // 肩侧时相机向上偏移高度
    [SerializeField] private float shoulderTransitionTime = 0.3f;   // 切换肩侧/常态时动画时间

    [Header("肩侧设置")]
    [Tooltip("距离小于等于该值时进入肩侧模式，人机视角锁定。")]
    [SerializeField] private float shoulderDistanceThreshold = 15f;

    [Header("鼠标控制")]
    [SerializeField] private float mouseSensitivity = 5f;          // 右键拖拽灵敏度
    [SerializeField] private float minPitch = 5f;                  // 俯仰最小角度
    [SerializeField] private float maxPitch = 80f;                 // 俯仰最大角度

    [Header("旋转音效")]
    [SerializeField] private AudioClip[] rotationClockwiseSounds;      // 顺时针旋转音效
    [SerializeField] private AudioClip[] rotationCounterClockwiseSounds; // 逆时针旋转音效
    
    // 基准摄像头角度偏移（Euler angles）
    // 现在不再 readonly，以便在鼠标拖拽时调整俯仰
    private Vector3 baseRotation = new Vector3(30f, 30f, 0f);
    
    private float currentYRotation = 0f;  // 当前Y轴旋转角度
    private float currentDistance = 20f;  // 当前距离
    private bool isTransitioning = false; // 是否正在过渡
    private float transitionTimer = 0f;   // 过渡计时器
    private float startYRotation = 0f;    // 过渡起始Y旋转
    private float targetYRotation = 0f;   // 目标Y轴旋转
    private float debugTimer = 0f;        // 调试输出计时器

    // whether currently in shoulder view mode (distance threshold met)
    private bool shoulderMode = false;
    private bool shoulderTransitioning = false;
    private float shoulderBlend = 0f; // 0=normal,1=shoulder

    
    // 等待列表
    private class RotationTask
    {
        public float deltaAngle;
        public float addedTime;
        
        public RotationTask(float deltaAngle, float addedTime)
        {
            this.deltaAngle = deltaAngle;
            this.addedTime = addedTime;
        }
    }
    
    private Queue<RotationTask> rotationWaitList = new Queue<RotationTask>();
    private float lastRotationEndTime = 0f;  // 最后一次旋转结束的时间
    
    // 音频播放器
    private AudioSource audioSource;
    
    /// <summary>
    /// 获取当前角度索引（供Movement使用）
    /// 返回最接近的90度整数倍索引（0=0°, 1=90°, 2=180°, 3=270°）
    /// </summary>
    public int GetCurrentAngleIndex()
    {
        float normalizedAngle = (currentYRotation % 360f + 360f) % 360f;
        // 因为旋转方向反了，索引也需要反过来
        int rawIndex = Mathf.RoundToInt(normalizedAngle / 90f) % 4;
        // 反转索引映射：0->0, 1->3, 2->2, 3->1
        return (4 - rawIndex) % 4;
    }
    
    /// <summary>
    /// 直接设置摄像头Y轴旋转角度（供RocketStateManager恢复位置时使用）
    /// </summary>
    /// <param name="angleIndex">角度索引 (0=0°, 1=90°, 2=180°, 3=270°)</param>
    public void SetCameraAngle(int angleIndex)
    {
        // 将索引直接解释为相对于火箭朝向的偏移次数
        float targetAngle = angleIndex * 90f;

        currentYRotation = targetAngle;
        targetYRotation = targetAngle;
        isTransitioning = false;

        // 立即更新摄像头位置
        UpdateCameraPosition();

        Debug.Log($"[CamaraFollow] 摄像头角度已设置为索引 {angleIndex} ({targetAngle}° 相对)");
    }

    private bool wasLockedBeforeSceneChange = false;

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // 如果加载的不是主场景(Main)，就解锁鼠标并记住之前是否锁定
        if (scene.name != "Main")
        {
            wasLockedBeforeSceneChange = (Cursor.lockState == CursorLockMode.Locked);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // 返回主场景，如果之前是锁定的，则恢复锁定状态
            if (wasLockedBeforeSceneChange && shoulderMode)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            // reset flag so that future switches behave correctly
            wasLockedBeforeSceneChange = false;
        }
    }

    void Start()
    {
        // 获取或添加AudioSource组件
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // 查找目标
        if (target == null)
        {
            GameObject rocket = GameObject.Find("Rocket");
            if (rocket != null)
            {
                target = rocket.transform;
            }
            else
            {
                Movement movement = FindObjectOfType<Movement>();
                if (movement != null)
                {
                    target = movement.transform;
                }
            }
        }
    
        if (target != null)
        {
            // 初始化为0°
            currentYRotation = 0f;
            targetYRotation = 0f;
            currentDistance = baseDistance;
            
            // 初始化摄像头位置
            InitializeCameraPosition();
            
            Debug.Log($"[CamaraFollow] 目标: {target.name}, 位置: {target.position}");
        }
        else
        {
            Debug.LogError("[CamaraFollow] 未找到目标（Rocket）!");
        }
    }
    
    void LateUpdate()
    {
        if (target == null) return;

        // adjust distance first (may trigger shoulder switch)
        HandleDistanceControl();

        // shoulder mode switching based on distance
        bool wantShoulder = currentDistance <= shoulderDistanceThreshold;
        if (wantShoulder && !shoulderMode)
            EnterShoulderMode();
        else if (!wantShoulder && shoulderMode)
            ExitShoulderMode();

        // always allow mouse look if dragging or cursor locked
        HandleMouseLook();
        // wait list only needed in non-shoulder/still mode
        if (!shoulderMode)
            ProcessWaitList();

        // Q/E旋转在任意模式都可用
        HandleAngleSwitch();

        UpdateCameraPosition();

        // if in shoulder mode, make rocket yaw follow camera so bullets fly straight
    }
    
    /// <summary>
    /// 处理等待列表
    /// </summary>
    void ProcessWaitList()
    {
        // 如果不在旋转且等待列表有任务，检查是否可以执行
        if (!isTransitioning && rotationWaitList.Count > 0)
        {
            // 检查最早的任务是否超时
            RotationTask task = rotationWaitList.Peek();
            float taskAge = Time.time - task.addedTime;
            
            if (taskAge <= waitListTimeout)
            {
                // 未超时，执行任务
                rotationWaitList.Dequeue();
                StartTransition(task.deltaAngle);
                Debug.Log($"[CamaraFollow] 从等待列表执行旋转任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
            else
            {
                // 超时，移除任务
                rotationWaitList.Dequeue();
                Debug.Log($"[CamaraFollow] 移除超时任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
        }
        
        // 清理所有超时的任务
        while (rotationWaitList.Count > 0)
        {
            RotationTask task = rotationWaitList.Peek();
            float taskAge = Time.time - task.addedTime;
            
            if (taskAge > waitListTimeout)
            {
                rotationWaitList.Dequeue();
                Debug.Log($"[CamaraFollow] 清理超时任务: {task.deltaAngle:F0}°, 等待时间: {taskAge:F3}秒");
            }
            else
            {
                break; // 队列前面的任务未超时，后面的肯定也没超时
            }
        }
    }
    
    /// <summary>
    /// 处理滚轮控制距离
    /// </summary>
    void HandleDistanceControl()
    {
        // 滚轮调整距离
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            currentDistance -= scroll * scrollSensitivity;
            currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);
        }
    }
    
    /// <summary>
    /// 处理鼠标按住右键时的自由视角拖拽（类似 DREDGE 等船只游戏）
    /// </summary>
    void HandleMouseLook()
    {
        if (TradingSystem.shopOpen) return;            // 商店打开时不响应

        bool shouldRotate = Input.GetMouseButton(1) || Cursor.lockState == CursorLockMode.Locked;
        if (!shouldRotate) return;

        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");


        if (shoulderMode)
        {
            // only rotate the rocket, camera offset remains fixed behind
            if (target != null)
            {
                target.Rotate(0f, mx * mouseSensitivity, 0f, Space.World);
            }
            // adjust pitch freely; allow looking up (!!) and down with a generous clamp
            baseRotation.x -= my * mouseSensitivity;
            baseRotation.x = Mathf.Clamp(baseRotation.x, -80f, 80f); // allow negative for upward
        }
        else
        {
            // non-shoulder: do nothing with mouse
        }
    }

    /// <summary>
    /// 切换肩侧模式时调用
    /// </summary>
    void EnterShoulderMode()
    {
        if (shoulderTransitioning) return; // already animating
        shoulderMode = true;
        shoulderTransitioning = true;
        StartCoroutine(ShoulderBlendCoroutine(true));

        // do not reset pitch; keep whatever angle player was looking at
        // (clamping is handled during mouse drag below and allows upward view)

        // reset camera offset so it sits directly behind rocket
        currentYRotation = -baseRotation.y;
        targetYRotation = currentYRotation;
        isTransitioning = false;
        // do not modify pitch here; allow free range

        UpdateCameraPosition();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        Debug.Log("[CamaraFollow] 进入肩侧模式，锁定在火箭后方");
    }

    void ExitShoulderMode()
    {
        if (shoulderTransitioning) return; // ignore until blend finishes
        shoulderMode = false;
        shoulderTransitioning = true;
        StartCoroutine(ShoulderBlendCoroutine(false));

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[CamaraFollow] 退出肩侧模式");
    }

    /// <summary>
    /// Blend value coroutine for shoulder transition (0→1 entering, 1→0 exiting)
    /// </summary>
    IEnumerator ShoulderBlendCoroutine(bool toShoulder)
    {
        float start = shoulderBlend;
        float end = toShoulder ? 1f : 0f;
        float elapsed = 0f;
        while (elapsed < shoulderTransitionTime)
        {
            elapsed += Time.deltaTime;
            shoulderBlend = Mathf.Lerp(start, end, elapsed / shoulderTransitionTime);
            yield return null;
        }
        shoulderBlend = end;
        shoulderTransitioning = false;
    }

    /// <summary>
    /// 处理Q/E键切换角度
    /// </summary>
    void HandleAngleSwitch()
    {
        // 检测按键持续按下状态
        // if shop open, ignore rotation keys entirely
        if (TradingSystem.shopOpen) return;
        bool qPressed = Input.GetKey(KeyCode.Q);
        bool ePressed = Input.GetKey(KeyCode.E);
        
        // 如果按住Q或E，且不在旋转中，立即开始旋转
        if (!isTransitioning)
        {
            if (qPressed)
            {
                RequestRotation(-90f); // Q键：顺时针旋转90°
            }
            else if (ePressed)
            {
                RequestRotation(90f); // E键：逆时针旋转90°
            }
        }
        // 如果正在旋转但按键首次按下，加入等待列表
        else
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                RequestRotation(-90f);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                RequestRotation(90f);
            }
        }
        
        // 更新过渡状态
        if (isTransitioning)
        {
            transitionTimer += Time.deltaTime;
            if (transitionTimer >= rotationTransitionTime)
            {
                isTransitioning = false;
                currentYRotation = targetYRotation; // 过渡完成，更新当前角度
                lastRotationEndTime = Time.time; // 记录旋转结束时间
                Debug.Log($"[CamaraFollow] 旋转完成，当前角度: {currentYRotation:F1}°");
            }
        }
    }
    
    /// <summary>
    /// 请求旋转（处理等待列表逻辑）
    /// </summary>
    /// <param name="deltaAngle">角度增量（正数逆时针，负数顺时针）</param>
    void RequestRotation(float deltaAngle)
    {
        // 播放旋转音效
        PlayRotationSound(deltaAngle);
        
        if (!isTransitioning)
        {
            // 不在旋转，直接开始
            StartTransition(deltaAngle);
            Debug.Log($"[CamaraFollow] 直接开始旋转: {deltaAngle:F0}°");
        }
        else
        {
            // 正在旋转，加入等待列表
            rotationWaitList.Enqueue(new RotationTask(deltaAngle, Time.time));
            Debug.Log($"[CamaraFollow] 旋转中，任务加入等待列表: {deltaAngle:F0}°, 队列长度: {rotationWaitList.Count}");
        }
    }
    
    /// <summary>
    /// 开始角度过渡
    /// </summary>
    /// <param name="deltaAngle">角度增量（正数顺时针，负数逆时针）</param>
    void StartTransition(float deltaAngle)
    {
        isTransitioning = true;
        transitionTimer = 0f;
        
        // 起始角度为当前角度
        startYRotation = currentYRotation;
        
        // 目标角度 = 当前角度 + 增量
        targetYRotation = currentYRotation + deltaAngle;
        
        Debug.Log($"[CamaraFollow] 旋转: {startYRotation:F1}° → {targetYRotation:F1}° (增量: {deltaAngle:F0}°)");
    }
    
    /// <summary>
    /// 播放旋转音效
    /// </summary>
    /// <param name="deltaAngle">角度增量（负数=顺时针，正数=逆时针）</param>
    public bool IsInShoulderMode()
    {
        return shoulderMode;
    }

    void PlayRotationSound(float deltaAngle)
    {
        if (audioSource == null) return;
        
        AudioClip[] soundArray = null;
        
        // 负数是顺时针（Q键），正数是逆时针（E键）
        if (deltaAngle < 0)
        {
            soundArray = rotationClockwiseSounds;
        }
        else if (deltaAngle > 0)
        {
            soundArray = rotationCounterClockwiseSounds;
        }
        
        // 从数组中随机选择一个音效播放
        if (soundArray != null && soundArray.Length > 0)
        {
            AudioClip randomClip = soundArray[Random.Range(0, soundArray.Length)];
            if (randomClip != null)
            {
                audioSource.PlayOneShot(randomClip);
            }
        }
    }
    
    /// <summary>
    /// 初始化摄像头位置
    /// </summary>
    void InitializeCameraPosition()
    {
        // 火箭位置作为旋转中心
        Vector3 pivotPoint = target.position;
        
        // 计算初始位置（考虑基准角度偏移）
        float currentYAngle = currentYRotation + baseRotation.y;
        float angleInRadians = currentYAngle * Mathf.Deg2Rad;
        
        // 计算水平距离和高度（基于俯角baseRotation.x）
        float pitchRadians = baseRotation.x * Mathf.Deg2Rad;
        float horizontalDist = baseDistance * Mathf.Cos(pitchRadians);
        float height = baseDistance * Mathf.Sin(pitchRadians);

        // 在火箭本地空间中构造偏移，然后转换到世界坐标
        Vector3 localOffset = new Vector3(
            Mathf.Sin(angleInRadians) * horizontalDist,
            height,
            -Mathf.Cos(angleInRadians) * horizontalDist
        );

        // 将相对于火箭前方的局部偏移旋转到世界
        Quaternion rocketYaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3 worldOffset = rocketYaw * localOffset;

        // 应用肩部水平偏移（基于相机当前右向）
        if (shoulderOffset != 0f || (shoulderMode && shoulderHeight != 0f))
        {
            Vector3 camDir = (pivotPoint - (pivotPoint + worldOffset)).normalized;
            Vector3 rightVec = Vector3.Cross(camDir, Vector3.up).normalized;
            worldOffset += rightVec * shoulderOffset;
            if (shoulderMode && shoulderHeight != 0f)
            {
                // compute up direction perpendicular to camDir and rightVec (camera-local up)
                Vector3 upVec = Vector3.Cross(rightVec, camDir).normalized;
                worldOffset += upVec * shoulderHeight;
            }
        }

        transform.position = pivotPoint + worldOffset;

        // 第1步：面朝Rocket
        transform.LookAt(pivotPoint);
    }
    
    /// <summary>
    /// 更新摄像头位置和旋转
    /// </summary>
    void UpdateCameraPosition()
    {
        // 火箭位置作为旋转中心和LookAt目标
        Vector3 pivotPoint = target.position;
        
        // 计算当前Y角度
        float desiredYRotation;
        
        if (isTransitioning)
        {
            float t = transitionTimer / rotationTransitionTime;
            t = Mathf.SmoothStep(0f, 1f, t);
            desiredYRotation = Mathf.Lerp(startYRotation, targetYRotation, t);
        }
        else
        {
            desiredYRotation = currentYRotation;
        }
        
        // 应用基准Y角度偏移
        float currentYAngle = desiredYRotation + baseRotation.y;
        
        // 转换为弧度
        float angleInRadians = currentYAngle * Mathf.Deg2Rad;
        
        // 计算水平距离和高度（基于俯角baseRotation.x）
        float pitchRadians = baseRotation.x * Mathf.Deg2Rad;
        float horizontalDist = currentDistance * Mathf.Cos(pitchRadians);
        float height = currentDistance * Mathf.Sin(pitchRadians);

        Vector3 localOffset = new Vector3(
            Mathf.Sin(angleInRadians) * horizontalDist,
            height,
            -Mathf.Cos(angleInRadians) * horizontalDist
        );
        Quaternion rocketYaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        Vector3 worldOffset = rocketYaw * localOffset;
        if (shoulderOffset != 0f || shoulderBlend>0f)
        {
            Vector3 camDir = (pivotPoint - (pivotPoint + worldOffset)).normalized;
            Vector3 rightVec = Vector3.Cross(camDir, Vector3.up).normalized;
            float blend = shoulderBlend;
            worldOffset += rightVec * shoulderOffset * blend;
            if (shoulderHeight!=0f && blend>0f)
            {
                Vector3 upVec = Vector3.Cross(rightVec, camDir).normalized;
                worldOffset += upVec * shoulderHeight * blend;
            }
        }

        // 设置位置
        transform.position = pivotPoint + worldOffset;

        // 第1步：确保摄像头面向火箭（最高优先级）
        transform.LookAt(pivotPoint);
        
        // 每3秒输出调试信息

        // shoulderBlend debug
        if (Time.frameCount % 180 == 0)
            Debug.Log($"[CamaraFollow] shoulderBlend={shoulderBlend:F2}");
        debugTimer += Time.deltaTime;
        if (debugTimer >= 3f)
        {
            debugTimer = 0f;
            
            // Rocket坐标位置
            Vector3 rocketPos = target.position;
            
            // 摄像头视线方向向量
            Vector3 forward = transform.forward;
            
            // 判断主要面朝方向
            string direction = "";
            float absX = Mathf.Abs(forward.x);
            float absY = Mathf.Abs(forward.y);
            float absZ = Mathf.Abs(forward.z);
            
            if (absX > absY && absX > absZ)
            {
                direction = forward.x > 0 ? "X+" : "X-";
            }
            else if (absZ > absX && absZ > absY)
            {
                direction = forward.z > 0 ? "Z+" : "Z-";
            }
            else
            {
                direction = forward.y > 0 ? "Y+" : "Y-";
            }
            
            Vector3 currentRot = transform.eulerAngles;
            
            Debug.Log($"[CamaraFollow Debug] Rocket坐标: ({rocketPos.x:F2}, {rocketPos.y:F2}, {rocketPos.z:F2}) | " +
                     $"摄像头面朝方向: {direction} | " +
                     $"视线向量: (x={forward.x:F3}, y={forward.y:F3}, z={forward.z:F3}) | " +
                     $"摄像头角度: ({currentRot.x:F1}, {currentRot.y:F1}, {currentRot.z:F1}) | " +
                     $"距离: {currentDistance:F2}");
        }
    }

    // draw simple crosshair when shoulderMode active
    void OnGUI()
    {
        if (!shoulderMode) return;
        float size = 16f;
        Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Color old = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(c.x - 1, c.y - size, 2, size * 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x - size, c.y - 1, size * 2, 2), Texture2D.whiteTexture);
        GUI.color = old;
    }
}

