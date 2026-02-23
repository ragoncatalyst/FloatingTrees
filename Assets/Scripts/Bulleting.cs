using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 在 Main 场景中：左键发射 Arrow prefab 的副本。
/// - 撞到 Tag = "Terrain" 的物体时立刻停止移动（变为 kinematic）
/// - 发射后对每个已停止在 Terrain 上的子弹，在发射后的 <stuckLifetime> 秒后销毁
/// Usage: 把此脚本挂在场景中任意常驻物体（例如 Main Camera / GameManager），并在 Inspector 指定 Arrow prefab。
/// 如果没有指定 prefab，会生成一个简单的运行时回退体（Capsule）。
/// </summary>
public class Bulleting : MonoBehaviour
{
    [Tooltip("Arrow prefab（实例必须含 Collider + 可选 Rigidbody）。")]
    public GameObject arrowPrefab;

    [Tooltip("发射来源；若为空则使用 Camera.main")] public Transform spawnSource;
    [Tooltip("从来源前方偏移多少单位生成")] public float spawnOffset = 1.0f;
    [Tooltip("发射速度（m/s）")] public float launchSpeed = 25f;

    [Tooltip("发射后，若子弹已停留在标记为 Terrain 的物体上，经过此秒数后销毁（秒）")]
    public float stuckLifetime = 20f;

    [Tooltip("仅在场景名为 " + "Main" + " 时允许发射（勾选可防止非 Main 场景触发）")]
    public bool onlyInMainScene = true;
    [Header("Firing")]
    [Tooltip("发射最小间隔（秒）。脚本会强制最低值为 0.2s；长按时以此间隔持续发射。")]
    public float fireInterval = 0.2f;

    // 内部计时器：下一次许可发射时间
    float nextAllowedFireTime = 0f;

    [Tooltip("弹匣容量（发射次数），默认为 30。")]
    public int magazineSize = 30;

    [Tooltip("当前弹匣剩余子弹（运行时）")]
    public int currentAmmo = 30;

    [Tooltip("换弹时长（秒），按 R 键触发，期间无法发射。")]
    public float reloadDuration = 3f;

    // 是否正在换弹（阻止发射）
    bool isReloading = false;

    void OnValidate()
    {
        // 强制最低发射间隔为 0.2 秒以满足需求
        if (fireInterval < 0.2f) fireInterval = 0.2f;
        if (magazineSize < 1) magazineSize = 1;
        if (reloadDuration < 0f) reloadDuration = 0f;
    }

    [Header("Debug")]
    [Tooltip("开启后在 Scene 视图绘制命中点、映射点和发射向量的 Gizmos，并在控制台打印摄像机本地坐标用于排查符号问题。")]
    public bool debugDrawMapping = false;

    // 上一次映射的调试数据（仅当 debugDrawMapping=true 时有效）
    Vector3 dbg_lastHitWorld = Vector3.zero;
    Vector3 dbg_lastMappedWorld = Vector3.zero;
    Vector3 dbg_lastSpawnWorld = Vector3.zero;
    Vector3 dbg_lastAimDir = Vector3.zero;
    Vector3 dbg_camHitLocal = Vector3.zero;
    Vector3 dbg_camRocketLocal = Vector3.zero;
    Vector3 dbg_relCam = Vector3.zero;
    Vector3 dbg_mappedRelCam = Vector3.zero;
    bool dbg_hasMapping = false;

    void Start()
    {
        currentAmmo = magazineSize;
        nextAllowedFireTime = 0f;
        isReloading = false;
        // 如果场景中再也没有 AimingSphere，EnsureAimingSphereDoesNotCollide
        // 也不会做任何事情，所以可以保留或移除都无妨。
        EnsureAimingSphereDoesNotCollide();
    }

    void EnsureAimingSphereDoesNotCollide()
    {
        GameObject rocket = GameObject.Find("Rocket");
        if (rocket == null) return;
        Transform aimingTf = rocket.transform.Find("AimingSphere");
        if (aimingTf == null) return;
        Collider aimCol = aimingTf.GetComponent<Collider>();
        if (aimCol == null) return;

            // 强制将 AimingSphere 的 Collider 设为 trigger（避免它影响其他物体的碰撞）
            aimCol.isTrigger = true;

        // 忽略与场景中其它碰撞体的碰撞 —— 但不会影响射线检测
        Collider[] allCols = FindObjectsOfType<Collider>();

        foreach (var c in allCols)
        {
            if (c == aimCol) continue;
            Physics.IgnoreCollision(aimCol, c, true);
        }

        // 设为半透明材质（优先使用 Resources/AimingSphere.mat；若不存在则运行时设置材质为半透明）
        var mr = aimingTf.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = Resources.Load<Material>("AimingSphere");
            if (mat != null)
            {
                mr.sharedMaterial = mat;
            }
            else
            {
                // 运行时回退：确保使用 Standard shader 并设置透明参数
                Material runtime = mr.material;
                var std = Shader.Find("Standard");
                if (runtime == null || runtime.shader == null) runtime = new Material(std);
                runtime.shader = std;
                runtime.SetFloat("_Mode", 3);
                runtime.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                runtime.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                runtime.SetInt("_ZWrite", 0);
                runtime.DisableKeyword("_ALPHATEST_ON");
                runtime.EnableKeyword("_ALPHABLEND_ON");
                runtime.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                var col = runtime.color;
                col.a = 0.25f;
                runtime.color = col;
                mr.material = runtime; // instance
            }

            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        Debug.Log("[Bulleting] AimingSphere: set isTrigger=true, ignored collisions and applied semi-transparent material.");
    }

    void Update()
    {
        if (onlyInMainScene && SceneManager.GetActiveScene().name != "Main") return;
        // disable firing when shop is open
        if (TradingSystem.shopOpen) return;

        // 换弹：按 R（在 Main 场景内），不可在换弹中再次触发
        if (Input.GetKeyDown(KeyCode.R) && !isReloading && SceneManager.GetActiveScene().name == "Main")
        {
            StartCoroutine(ReloadCoroutine());
        }

        // DEBUG: 按 F6 强制触发 Rocket 的爆炸恢复序列（仅用于调试自动复活问题）
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Debug.Log("[Bulleting|DEBUG] 强制触发 RocketStateManager.TriggerExplosionRecoverySequence()");
            RocketStateManager.TriggerExplosionRecoverySequence();
        }

        // 鼠标按下（单次触发）
        if (Input.GetMouseButtonDown(0))
        {
            if (!isReloading && currentAmmo > 0 && Time.time >= nextAllowedFireTime)
            {
                bool fired = SpawnAndFire();
                if (fired)
                {
                    currentAmmo--;
                    // 首发后将下次允许发射时间设置为当前时间 + 间隔
                    nextAllowedFireTime = Time.time + fireInterval;
                    if (currentAmmo <= 0)
                        Debug.Log("[Bulleting] 弹匣已空，按 R 换弹");
                }
                else
                {
                    // 未命中 AimingSphere 半球 — 不消耗弹药也不设置冷却
                    Debug.Log("[Bulleting] 未命中 AimingSphere 半球：未发射");
                }
            }
            else
            {
                if (currentAmmo <= 0 && !isReloading)
                    Debug.Log("[Bulleting] 无弹，按 R 换弹");
            }
        }

        // 持续按住时以稳定间隔持续发射（保证恒定 cadence）
        if (Input.GetMouseButton(0))
        {
            if (!isReloading && currentAmmo > 0 && Time.time >= nextAllowedFireTime)
            {
                bool fired = SpawnAndFire();
                if (fired)
                {
                    currentAmmo--;
                    // 保持节奏：基于上次许可时间递增，避免累积抖动
                    nextAllowedFireTime += fireInterval;

                    // 防止在跳帧后 nextAllowedFireTime 远小于 Time.time（造成连发），做一次修正
                    if (nextAllowedFireTime < Time.time)
                        nextAllowedFireTime = Time.time + fireInterval;

                    if (currentAmmo <= 0)
                        Debug.Log("[Bulleting] 弹匣已空，按 R 换弹");
                }
                else
                {
                    // 未命中半球，不执行任何动作（允许用户继续尝试点击/移动鼠标）
                }
            }
        }
    }

    IEnumerator ReloadCoroutine()
    {
        isReloading = true;
        nextAllowedFireTime = Time.time + reloadDuration; // 阻止在换弹期间发射
        Debug.Log($"[Bulleting] 开始换弹：等待 {reloadDuration:F1}s");
        yield return new WaitForSeconds(reloadDuration);
        currentAmmo = magazineSize;
        isReloading = false;
        Debug.Log($"[Bulleting] 换弹完成，弹量 = {currentAmmo}");
    }

    bool SpawnAndFire()
    {
        // 发射源
        Transform source = spawnSource ? spawnSource : (Camera.main ? Camera.main.transform : transform);
        Vector3 spawnPos = source.position + source.forward * spawnOffset;

        // 从屏幕鼠标出发的射线
        Ray camRay = (Camera.main != null)
            ? Camera.main.ScreenPointToRay(Input.mousePosition)
            : new Ray(source.position, source.forward);

        Vector3 aimPoint = Vector3.zero;

        // 为了满足“三个轴的发射方向”，我们使用一个通过火箭、
        // 法线与摄像机方向一致的平面来决定瞄准点。这样的平面与画面平行，
        // 效果类似许多第一人称/第三人称射击类游戏：开火始终朝着摄像机正在看
        // 的方向，只不过由火箭位置发射。
        Vector3 rocketPos = spawnPos;
        GameObject rocket = GameObject.Find("Rocket");
        if (rocket != null) rocketPos = rocket.transform.position;

        bool gotAim = false;
        Plane camPlane = new Plane(Camera.main ? Camera.main.transform.forward : Vector3.forward, rocketPos);
        if (camPlane.Raycast(camRay, out float enter))
        {
            aimPoint = camRay.GetPoint(enter);
            gotAim = true;
        }

        if (!gotAim)
        {
            // 如果摄像机射线与平面平行（理论上很少见），退回到水平XZ平面作为备选。
            Plane horiz = new Plane(Vector3.up, rocketPos);
            if (horiz.Raycast(camRay, out float enter2))
            {
                aimPoint = camRay.GetPoint(enter2);
                gotAim = true;
            }
        }

        if (!gotAim)
        {
            // 连备用平面也没命中说明出了问题，放弃发射
            return false;
        }

        // 调试数据
        if (debugDrawMapping)
        {
            dbg_hasMapping = true;
            dbg_lastHitWorld = aimPoint;
            dbg_lastMappedWorld = aimPoint;
            dbg_lastSpawnWorld = spawnPos;
            dbg_lastAimDir = (aimPoint - spawnPos).normalized;
            Debug.Log($"[Bulleting] aimPoint={aimPoint} (computed on camera plane)");
        }

        // 计算朝向并旋转箭矢
        Vector3 aimDir = (aimPoint - spawnPos);
        if (aimDir.sqrMagnitude < 1e-6f) aimDir = source.forward;
        aimDir.Normalize();
        Quaternion rot = Quaternion.LookRotation(aimDir, Vector3.up);

        // 生成实例（使用已存在的 Resources/Arrow 回退）
        GameObject go = null;
        if (arrowPrefab != null)
        {
            go = Instantiate(arrowPrefab, spawnPos, rot);
        }
        else
        {
            GameObject resPrefab = Resources.Load<GameObject>("Arrow");
            if (resPrefab != null)
            {
                go = Instantiate(resPrefab, spawnPos, rot);
                Debug.Log("[Bulleting] 使用 Resources/Arrow 作为 Arrow prefab");
            }
            else
            {
                Debug.LogWarning("[Bulleting] arrowPrefab 未指派，且 Resources/Arrow 丢失 — 使用运行时回退体 (Capsule)");
                go = CreateFallbackArrow(spawnPos, rot);
            }
        }

        // 忽略与 Rocket 的碰撞
        GameObject rocketObj = GameObject.Find("Rocket");
        if (rocketObj != null)
        {
            Collider[] rocketCols = rocketObj.GetComponentsInChildren<Collider>(true);
            Collider[] myCols = go.GetComponentsInChildren<Collider>(true);
            foreach (var myCol in myCols)
                foreach (var rCol in rocketCols)
                    Physics.IgnoreCollision(myCol, rCol, true);
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();

        // 继承 Rocket 的线速度（若存在），以保留发射时的惯性
        Vector3 inheritedVelocity = Vector3.zero;
        GameObject rocketForVelocity = GameObject.Find("Rocket");
        if (rocketForVelocity != null)
        {
            var rocketRb = rocketForVelocity.GetComponent<Rigidbody>();
            if (rocketRb != null) inheritedVelocity = rocketRb.velocity;
        }

        rb.velocity = inheritedVelocity + aimDir * launchSpeed;
        go.transform.rotation = rot;

        ArrowBullet bullet = go.GetComponent<ArrowBullet>();
        if (bullet == null) bullet = go.AddComponent<ArrowBullet>();
        bullet.Init(stuckLifetime, Time.time);

        return true;
    }

    GameObject CreateFallbackArrow(Vector3 pos, Quaternion rot)
    {
        GameObject a = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        a.name = "Arrow_Fallback";
        a.transform.position = pos;
        a.transform.rotation = rot;
        a.transform.localScale = new Vector3(0.08f, 0.4f, 0.08f);

        // 简单视觉
        var r = a.GetComponent<Renderer>();
        if (r) r.material.color = Color.yellow;

        return a;
    }


    void OnDrawGizmos()
    {
        if (!debugDrawMapping || !dbg_hasMapping) return;

        // hit 点（红），映射点（绿色），发射起点（蓝），发射向量（黄色）
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(dbg_lastHitWorld, 0.08f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(dbg_lastMappedWorld, 0.08f);

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(dbg_lastSpawnWorld, 0.06f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(dbg_lastSpawnWorld, dbg_lastMappedWorld);

        // 连接 hit -> mapped
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawLine(dbg_lastHitWorld, dbg_lastMappedWorld);
    }

    // 每个箭矢的运行时处理类（放在同文件，实例会被 AddComponent 到每个 spawn 的对象上）
    public class ArrowBullet : MonoBehaviour
    {
        Rigidbody rb;
        bool isStuck = false;           // 是否已停在 Terrain 上
        float fireTime = 0f;            // 发射时间（用于计算 20s 到期）
        float stuckLifetime = 20f;      // 从发射开始计时的阈值

        /// <summary>
        /// 在 Bulleting.Spawn 时由宿主调用初始化
        /// </summary>
        public void Init(float stuckLifetime, float fireTime)
        {
            this.stuckLifetime = stuckLifetime;
            this.fireTime = fireTime;
            rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();

            // 确保存在碰撞器（没有的话加一个 SphereCollider）
            if (GetComponent<Collider>() == null) gameObject.AddComponent<SphereCollider>();

            // 额外保护：忽略与场景中名为 Rocket 的物体及其子碰撞体的碰撞（防止任何子弹触发 Rocket 的碰撞处理）
            IgnoreRocketCollisions();
        }

        void IgnoreRocketCollisions()
        {
            GameObject rocketObj = GameObject.Find("Rocket");
            if (rocketObj == null) return;
            Collider[] rocketCols = rocketObj.GetComponentsInChildren<Collider>(true);
            Collider[] myCols = GetComponentsInChildren<Collider>(true);
            foreach (var myCol in myCols)
            {
                if (myCol == null) continue;
                foreach (var rCol in rocketCols)
                {
                    if (rCol == null) continue;
                    Physics.IgnoreCollision(myCol, rCol, true);
                }
            }
        }

        void OnCollisionEnter(Collision other)
        {
            if (isStuck) return;
            if (other.gameObject.CompareTag("Terrain"))
            {
                StopOnTerrain();
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (isStuck) return;
            if (other.gameObject.CompareTag("Terrain"))
            {
                StopOnTerrain();
            }
        }

        void StopOnTerrain()
        {
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; // 立即停止受物理影响
            }
            isStuck = true;
        }

        void Update()
        {
            // 要求：只有“已经停在 Terrain 上的子弹”会在发射后 20s 后消失
            if (isStuck && Time.time - fireTime >= stuckLifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}