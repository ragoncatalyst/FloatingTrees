using UnityEngine;

[CreateAssetMenu(fileName = "ExplosionFXSettings", menuName = "FX/Explosion FX Settings", order = 1)]
public class ExplosionFXSettings : ScriptableObject
{
    [Header("Shard (debris) settings")]
    public int shardCount = 18;
    public float shardSize = 0.10f;
    public float shardForce = 8f;
    public float shardLifetime = 2f;

    [Header("Light cloud (large, light-gray) settings")]
    public int lightCloudBurst = 60;
    public float lightCloudLifetime = 1.6f;
    public float lightCloudRadius = 0.45f;

    [Header("Dark puff (fewer, dense) settings")]
    public int darkPuffBurst = 18;
    public float darkPuffLifetime = 1.2f;
    public float darkPuffRadius = 0.25f;

    [Header("Swirl vortex settings")]
    public int swirlBurst = 40;
    public float swirlLifetime = 1.8f;
    public float swirlStartSpeedMin = 1.2f;
    public float swirlStartSpeedMax = 2.2f;

    [Header("General performance limits")]
    public int maxShardCount = 20;
    public int maxParticlesPerSystem = 300;
}