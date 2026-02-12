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
        // Load optional inspector-editable settings from Resources/ExplosionFXSettings (if present)
        ExplosionFXSettings settings = Resources.Load<ExplosionFXSettings>("ExplosionFXSettings");
        if (settings != null)
        {
            shardCount = Mathf.Clamp(settings.shardCount, 0, settings.maxShardCount);
            shardSize = settings.shardSize;
            force = settings.shardForce;
            lifetime = settings.shardLifetime;

            Debug.Log($"[BlockExplosionEffect] Loaded ExplosionFXSettings from Resources (shardCount={shardCount})");
        }

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
            // Fallback: composite programmatic explosion (Minecraft-like)
            // 1) Many light-gray smoke puffs (values overridable via ExplosionFXSettings)
            int lightBurst = settings != null ? settings.lightCloudBurst : 60;
            float lightLife = settings != null ? settings.lightCloudLifetime : 1.6f;
            float lightRadius = settings != null ? settings.lightCloudRadius : 0.45f;

            GameObject lightCloudGO = new GameObject("explosion_lightclouds");
            lightCloudGO.transform.SetParent(root.transform, false);
            lightCloudGO.transform.position = center;
            var lightPS = lightCloudGO.AddComponent<ParticleSystem>();
            var mainL = lightPS.main;
            mainL.duration = lightLife;
            mainL.loop = false;
            mainL.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, lightLife);
            mainL.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            mainL.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            mainL.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.95f));
            mainL.maxParticles = settings != null ? Mathf.Min(settings.maxParticlesPerSystem, 300) : 300;
            mainL.simulationSpace = ParticleSystemSimulationSpace.World;
            var emL = lightPS.emission; emL.rateOverTime = 0f; emL.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)lightBurst) });
            var shL = lightPS.shape; shL.shapeType = ParticleSystemShapeType.Sphere; shL.radius = lightRadius;
            var colL = lightPS.colorOverLifetime; colL.enabled = true;
            Gradient gL = new Gradient(); gL.SetKeys(new GradientColorKey[]{ new GradientColorKey(new Color(0.9f,0.9f,0.9f),0f), new GradientColorKey(new Color(0.6f,0.6f,0.6f),1f) }, new GradientAlphaKey[]{ new GradientAlphaKey(0.95f,0f), new GradientAlphaKey(0f,1f)});
            colL.color = new ParticleSystem.MinMaxGradient(gL);
            var sizeL = lightPS.sizeOverLifetime; sizeL.enabled = true; var curveL = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f,1f,1f,1.6f)); sizeL.size = curveL;
            var noiseL = lightPS.noise; noiseL.enabled = true; noiseL.strength = 0.6f; noiseL.frequency = 0.8f; noiseL.scrollSpeed = 0.2f;
            lightPS.Play(); Object.Destroy(lightCloudGO, lightLife + 1f);

            // 2) Fewer dark/black smoke puffs (denser core)
            GameObject darkGO = new GameObject("explosion_darkpuffs");
            darkGO.transform.SetParent(root.transform, false);
            darkGO.transform.position = center;
            var darkPS = darkGO.AddComponent<ParticleSystem>();
            var mainD = darkPS.main; mainD.duration = 1.2f; mainD.loop = false; mainD.startLifetime = new ParticleSystem.MinMaxCurve(0.6f,1.2f);
            mainD.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f); mainD.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
            mainD.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f,0.18f,0.18f,0.95f)); mainD.maxParticles = 80; mainD.simulationSpace = ParticleSystemSimulationSpace.World;
            int darkBurst = settings != null ? settings.darkPuffBurst : 18;
            float darkLife = settings != null ? settings.darkPuffLifetime : 1.2f;
            float darkRadius = settings != null ? settings.darkPuffRadius : 0.25f;
            var emD = darkPS.emission; emD.rateOverTime = 0f; emD.SetBursts(new ParticleSystem.Burst[]{ new ParticleSystem.Burst(0f, (short)darkBurst) });
            var shD = darkPS.shape; shD.shapeType = ParticleSystemShapeType.Sphere; shD.radius = darkRadius;
            var colD = darkPS.colorOverLifetime; colD.enabled = true; Gradient gD = new Gradient(); gD.SetKeys(new GradientColorKey[]{ new GradientColorKey(new Color(0.12f,0.12f,0.12f),0f), new GradientColorKey(new Color(0.03f,0.03f,0.03f),1f) }, new GradientAlphaKey[]{ new GradientAlphaKey(0.95f,0f), new GradientAlphaKey(0f,1f)}); colD.color = new ParticleSystem.MinMaxGradient(gD);
            var noiseD = darkPS.noise; noiseD.enabled = true; noiseD.strength = 0.9f; noiseD.frequency = 0.9f; noiseD.scrollSpeed = 0.25f;
            darkPS.Play(); Object.Destroy(darkGO, darkLife + 1f);

            // 3) Swirling mid-sized particles (vortex-like)
            GameObject swirlGO = new GameObject("explosion_swirl");
            swirlGO.transform.SetParent(root.transform, false);
            swirlGO.transform.position = center;
            var swirlPS = swirlGO.AddComponent<ParticleSystem>();
            var mainS = swirlPS.main; mainS.duration = 1.8f; mainS.loop = false; mainS.startLifetime = new ParticleSystem.MinMaxCurve(0.9f,1.6f);
            mainS.startSpeed = new ParticleSystem.MinMaxCurve(1.2f,2.2f); mainS.startSize = new ParticleSystem.MinMaxCurve(0.18f,0.35f);
            mainS.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f,0.78f,0.78f,0.95f)); mainS.maxParticles = 140; mainS.simulationSpace = ParticleSystemSimulationSpace.World;
            int swirlBurst = settings != null ? settings.swirlBurst : 40;
            float swirlLife = settings != null ? settings.swirlLifetime : 1.8f;
            var emS = swirlPS.emission; emS.rateOverTime = 0f; emS.SetBursts(new ParticleSystem.Burst[]{ new ParticleSystem.Burst(0f, (short)swirlBurst) });
            var shS = swirlPS.shape; shS.shapeType = ParticleSystemShapeType.Cone; shS.radius = 0.2f; shS.angle = 35f;
            var velS = swirlPS.velocityOverLifetime; velS.enabled = true; velS.orbitalZ = new ParticleSystem.MinMaxCurve(2.2f, 4.0f); velS.radial = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            var noiseS = swirlPS.noise; noiseS.enabled = true; noiseS.strength = 0.6f; noiseS.frequency = 0.9f; noiseS.scrollSpeed = 0.6f;
            var colS = swirlPS.colorOverLifetime; colS.enabled = true; Gradient gS = new Gradient(); gS.SetKeys(new GradientColorKey[]{ new GradientColorKey(new Color(0.85f,0.85f,0.85f),0f), new GradientColorKey(new Color(0.6f,0.6f,0.6f),1f) }, new GradientAlphaKey[]{ new GradientAlphaKey(0.95f,0f), new GradientAlphaKey(0f,1f)}); colS.color = new ParticleSystem.MinMaxGradient(gS);
            swirlPS.Play(); Object.Destroy(swirlGO, swirlLife + 1f);
        }

        // schedule root cleanup after lifetime (no light flash)
        Object.Destroy(root, lifetime + 0.1f);
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

}
