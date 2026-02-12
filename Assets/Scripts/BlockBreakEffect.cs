using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BlockBreakEffect
/// - 在运行时生成方形网格的小方块碎片（类似 Minecraft 的方块破裂效果）
/// - 简单、零配置：调用 Spawn 即可
/// </summary>
public class BlockBreakEffect : MonoBehaviour
{
    // 池节点（避免场景污染）
    private static Transform effectsRoot;

    // Spawn 碎片并朝四周爆散
    // - blockTransform: 用于从中获取材质/尺寸等信息（可为 null）
    // - resolution: 每轴分多少份（3 => 27 个碎片，4 => 64）
    // - fragmentSize: 每个碎片的缩放（相对单位）
    // - force: 爆散力度
    // - lifetime: 碎片存活时间
    public static void Spawn(Vector3 worldCenter, Transform blockTransform = null, int resolution = 3, float fragmentSize = 0.25f, float force = 3f, float lifetime = 1.6f)
    {
        if (effectsRoot == null)
        {
            GameObject go = new GameObject("_BlockBreakEffects");
            DontDestroyOnLoad(go);
            effectsRoot = go.transform;
        }

        Material mat = null;
        Color tint = Color.white;

        if (blockTransform != null)
        {
            var r = blockTransform.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                mat = r.sharedMaterial;
                // 尝试取主要颜色（若有）作为碎片颜色
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                    tint = r.sharedMaterial.GetColor("_Color");
            }
        }

        // 中心偏移，使碎片围绕方块中心分布
        float half = (resolution * fragmentSize) * 0.5f;

        // 父容器用于自动清理
        GameObject container = new GameObject("break_shards");
        container.transform.position = worldCenter;
        container.transform.SetParent(effectsRoot, true);

        // 预计算
        float gap = fragmentSize; // 紧密排列

        // 生成小方块碎片
        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int z = 0; z < resolution; z++)
                {
                    // 计算局部中心
                    Vector3 localPos = new Vector3(
                        (x + 0.5f) * gap - half + gap * 0.5f,
                        (y + 0.5f) * gap - half + gap * 0.5f,
                        (z + 0.5f) * gap - half + gap * 0.5f
                    );

                    Vector3 spawnPos = worldCenter + localPos;

                    // 使用原生立方体以获得碰撞体与渲染
                    GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shard.transform.position = spawnPos;
                    shard.transform.localScale = Vector3.one * fragmentSize;
                    shard.transform.SetParent(container.transform, true);

                    // 材质复制（避免修改原材质）
                    var rend = shard.GetComponent<Renderer>();
                    if (mat != null)
                    {
                        try
                        {
                            Material inst = new Material(mat);
                            inst.color = tint;
                            rend.material = inst;
                        }
                        catch
                        {
                            rend.material.color = tint;
                        }
                    }
                    else
                    {
                        rend.material.color = tint;
                    }

                    // Rigidbody
                    Rigidbody rb = shard.AddComponent<Rigidbody>();
                    // 增强重力感：将质量翻倍使重力作用看起来更强（gravity force = mass * g）
                    rb.mass = 0.2f * 2f; // 之前是 0.2f
                    rb.drag = 0.6f;
                    rb.angularDrag = 0.9f;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                    // 给碎片一个随机推力，使其爆散 — 播放速度提升至 2x（将 mag ×=2）
                    Vector3 dir = (localPos + Random.insideUnitSphere * 0.3f).normalized;
                    float mag = (force * 0.5f) * (0.5f + Random.value) * 2f; // 相对于上一次调整，速度翻倍
                    rb.AddForce(dir * mag, ForceMode.Impulse);

                    // 随机旋转（基于新的 mag）
                    rb.AddTorque(Random.insideUnitSphere * mag * 0.2f, ForceMode.Impulse);

                    // 立即开始淡出（在接下来的 fadeDuration 秒内逐渐透明并销毁）
                    float fadeDuration = 2f; // 用户要求为 2 秒
                    shard.AddComponent<FadeAndKill>().BeginFade(fadeDuration);
                }
            }
        }

        // 销毁容器（延迟，稍比碎片晚） — 延迟与碎片淡出时间一致
        Object.Destroy(container, 2f + 0.15f);
    }


    // 附加组件：对单个碎片做透明淡出并在完成后销毁
    class FadeAndKill : MonoBehaviour
    {
        Material[] materials;
        float duration = 2f;

        public void BeginFade(float fadeDuration)
        {
            duration = fadeDuration;

            var rends = GetComponentsInChildren<Renderer>(true);
            List<Material> mats = new List<Material>();
            foreach (var r in rends)
            {
                // 确保使用实例化材质以便安全修改颜色
                for (int i = 0; i < r.materials.Length; i++)
                {
                    r.materials[i] = new Material(r.materials[i]);
                    mats.Add(r.materials[i]);
                }
            }
            materials = mats.ToArray();

            StartCoroutine(FadeCoroutine());
        }

        IEnumerator FadeCoroutine()
        {
            float t = 0f;
            // 记录原始颜色
            Color[] original = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].HasProperty("_Color"))
                    original[i] = materials[i].GetColor("_Color");
                else
                    original[i] = materials[i].color;
            }

            while (t < duration)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(1f - (t / duration));
                for (int i = 0; i < materials.Length; i++)
                {
                    Color c = original[i];
                    c.a = a;
                    if (materials[i].HasProperty("_Color"))
                        materials[i].SetColor("_Color", c);
                    else
                        materials[i].color = c;
                }
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
