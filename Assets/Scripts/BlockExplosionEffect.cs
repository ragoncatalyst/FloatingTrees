using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minecraft-style explosion visual: many tiny cubic shards + smoke particle burst + brief light flash.
/// Usage: BlockExplosionEffect.SpawnExplosion(position, options...)
/// </summary>
public class BlockExplosionEffect : MonoBehaviour
{
    // shared material for shards to avoid per-instance material allocations
    private static Material sharedShardMaterial;

    public static void SpawnExplosion(Vector3 center, int shardCount = 40, float spread = 3.0f, float shardSize = 0.12f, float force = 6f, float lifetime = 2f)
    {
        GameObject root = new GameObject("explosion_effect");
        root.transform.position = center;
        DontDestroyOnLoad(root);

        // Safety clamp to avoid huge allocations on low-end devices or large explosions
        shardCount = Mathf.Clamp(shardCount, 0, 20); // limit shards to 20 by default

        // Shared material for shards to avoid per-shard material instancing cost
        if (sharedShardMaterial == null)
        {
            sharedShardMaterial = new Material(Shader.Find("Standard"));
            sharedShardMaterial.color = new Color(0.8f, 0.7f, 0.6f);
        }

        // Spawn shards (reduced and optimized)
        for (int i = 0; i < shardCount; i++)
        {
            Vector3 rand = new Vector3(Random.Range(-1f, 1f), Random.Range(0f, 1f), Random.Range(-1f, 1f));
            Vector3 pos = center + rand * (0.2f + Random.value * 0.4f);

            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.transform.localScale = Vector3.one * shardSize;
            shard.transform.position = pos;
            shard.transform.SetParent(root.transform, true);

            var rend = shard.GetComponent<Renderer>();
            rend.sharedMaterial = sharedShardMaterial; // reuse shared material to reduce GC/instancing

            Rigidbody rb = shard.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.drag = 0.6f;
            rb.angularDrag = 0.9f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 dir = (shard.transform.position - center).normalized + Random.insideUnitSphere * 0.25f;
            float mag = force * (0.6f + Random.value * 0.8f);
            rb.AddForce(dir * mag, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * mag * 0.1f, ForceMode.Impulse);

            // fade & destroy (non-instancing fade)
            shard.AddComponent<AutoFadeDestroy>().Begin(lifetime);
        }

        // Try to use project "Explosion Particles" prefab (Resources) if available to preserve intended visuals
        GameObject explosionPrefab = Resources.Load<GameObject>("Explosion Particles");
        if (explosionPrefab != null)
        {
            GameObject prefabInst = Object.Instantiate(explosionPrefab, center, Quaternion.identity, root.transform);
            // Play all particle systems on the prefab instance then destroy
            var parts = prefabInst.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var p in parts)
            {
                p.Play();
            }
            Object.Destroy(prefabInst, 3f);
        }
        else
        {
            // Fallback: programmatic smoke particle burst
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
        }

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
        Renderer[] rends;
        float life = 1.5f;
        float elapsed = 0f;
        MaterialPropertyBlock mpb;

        public void Begin(float lifetime)
        {
            life = lifetime;
            rends = GetComponentsInChildren<Renderer>(true);
            mpb = new MaterialPropertyBlock();
            StartCoroutine(LifeCoroutine());
        }

        IEnumerator LifeCoroutine()
        {
            while (elapsed < life)
            {
                elapsed += Time.deltaTime;
                float a = Mathf.Clamp01(1f - (elapsed / life));
                foreach (var r in rends)
                {
                    // Fetch original color from shared material and adjust alpha via MPB (no material instancing)
                    Color baseCol = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color") ? r.sharedMaterial.GetColor("_Color") : r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                    Color c = baseCol;
                    c.a = a;
                    mpb.SetColor("_Color", c);
                    r.SetPropertyBlock(mpb);
                }
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    // tiny MonoBehaviour helper to run coroutines from static context
    class MonoBehaviourHelper : MonoBehaviour { }
}