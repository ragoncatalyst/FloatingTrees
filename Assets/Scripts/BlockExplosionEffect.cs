using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minecraft-style explosion visual: many tiny cubic shards + smoke particle burst + brief light flash.
/// Usage: BlockExplosionEffect.SpawnExplosion(position, options...)
/// </summary>
public class BlockExplosionEffect : MonoBehaviour
{
    public static void SpawnExplosion(Vector3 center, int shardCount = 40, float spread = 3.0f, float shardSize = 0.12f, float force = 6f, float lifetime = 2f)
    {
        GameObject root = new GameObject("explosion_effect");
        root.transform.position = center;
        DontDestroyOnLoad(root);

        // Spawn shards
        for (int i = 0; i < shardCount; i++)
        {
            Vector3 rand = new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 1f), Random.Range(-1f, 1f));
            Vector3 pos = center + rand * (0.2f + Random.value * 0.4f);

            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.transform.localScale = Vector3.one * shardSize;
            shard.transform.position = pos;
            shard.transform.SetParent(root.transform, true);

            var rend = shard.GetComponent<Renderer>();
            // neutral gray-ish color
            rend.material = new Material(Shader.Find("Standard"));
            rend.material.color = new Color(0.8f, 0.7f, 0.6f);

            Rigidbody rb = shard.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.drag = 0.6f;
            rb.angularDrag = 0.9f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 dir = (shard.transform.position - center).normalized + Random.insideUnitSphere * 0.25f;
            float mag = force * (0.6f + Random.value * 0.8f);
            rb.AddForce(dir * mag, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * mag * 0.1f, ForceMode.Impulse);

            // fade & destroy
            shard.AddComponent<AutoFadeDestroy>().Begin(lifetime);
        }

        // Smoke particle burst (programmatic ParticleSystem)
        var psGO = new GameObject("explosion_smoke");
        psGO.transform.SetParent(root.transform, false);
        psGO.transform.position = center;
        var ps = psGO.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.duration = 1.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)40) });

        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new GradientColorKey[] { new GradientColorKey(new Color(0.2f,0.18f,0.16f),0f), new GradientColorKey(new Color(0.05f,0.05f,0.05f),1f) },
                  new GradientAlphaKey[] { new GradientAlphaKey(0.9f,0f), new GradientAlphaKey(0f,1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
        Object.Destroy(psGO, 3f);

        // Brief light flash
        var lightGO = new GameObject("explosion_light");
        lightGO.transform.SetParent(root.transform, false);
        lightGO.transform.position = center;
        var l = lightGO.AddComponent<Light>();
        l.color = new Color(1f, 0.85f, 0.6f);
        l.intensity = 6f;
        l.range = 6f;

        // fade light
        root.AddComponent<MonoBehaviourHelper>().StartCoroutine(FadeLightAndCleanup(l, root, lifetime));
    }

    static IEnumerator FadeLightAndCleanup(Light l, GameObject root, float lifetime)
    {
        float t = 0f;
        float dur = 0.5f;
        while (t < dur)
        {
            t += Time.deltaTime;
            l.intensity = Mathf.Lerp(6f, 0f, t / dur);
            yield return null;
        }
        Object.Destroy(l.gameObject);

        // cleanup after lifetime
        yield return new WaitForSeconds(lifetime);
        Object.Destroy(root);
    }

    // helper component: fades material alpha and destroys
    class AutoFadeDestroy : MonoBehaviour
    {
        Material[] mats;
        float life = 1.5f;
        float elapsed = 0f;
        public void Begin(float lifetime)
        {
            life = lifetime;
            var rends = GetComponentsInChildren<Renderer>(true);
            List<Material> list = new List<Material>();
            foreach (var r in rends)
            {
                for (int i = 0; i < r.materials.Length; i++)
                {
                    r.materials[i] = new Material(r.materials[i]);
                    list.Add(r.materials[i]);
                }
            }
            mats = list.ToArray();
            StartCoroutine(LifeCoroutine());
        }
        IEnumerator LifeCoroutine()
        {
            while (elapsed < life)
            {
                elapsed += Time.deltaTime;
                float a = Mathf.Clamp01(1f - (elapsed / life));
                foreach (var m in mats)
                {
                    if (m.HasProperty("_Color"))
                    {
                        Color c = m.GetColor("_Color");
                        c.a = a;
                        m.SetColor("_Color", c);
                    }
                    else
                    {
                        Color c = m.color;
                        c.a = a;
                        m.color = c;
                    }
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    // tiny MonoBehaviour helper to run coroutines from static context
    class MonoBehaviourHelper : MonoBehaviour { }
}