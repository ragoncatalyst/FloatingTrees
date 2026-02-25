using System.Collections;
using UnityEngine;

/// <summary>
/// Responsible for spawning Phantom enemies around the player.
/// Attempts to keep up to maxActive phantoms alive; spawns new ones at random
/// positions in the annulus defined by spawnMinDistance/spawnMaxDistance.
/// </summary>
public class PhantomSpawner : MonoBehaviour
{
    [Header("Phantom Settings")]
    public Phantom phantomPrefab;
    public int maxActive = 5;
    public float spawnInterval = 5f;

    [Header("Spawn Radius")]
    public float spawnMinDistance = 20f;
    public float spawnMaxDistance = 30f;

    private Transform player;
    private int currentCount = 0;

    void Start()
    {
        player = GameObject.FindWithTag("Rocket")?.transform;
        if (player == null)
            Debug.LogWarning("[PhantomSpawner] no object tagged 'Rocket' found");
        StartCoroutine(SpawnLoop());
        Debug.Log("[PhantomSpawner] starting spawn loop");
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            if (player != null && currentCount < maxActive)
            {
                SpawnOne();
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnOne()
    {
        // determine random point around player
        Vector3 dir = Random.onUnitSphere;
        dir.y = 0f;
        float dist = Random.Range(spawnMinDistance, spawnMaxDistance);
        Vector3 pos = player.position + dir.normalized * dist + Vector3.up * 10f;

        Phantom p = Instantiate(phantomPrefab, pos, Quaternion.identity);
        currentCount++;
        Debug.Log("[PhantomSpawner] spawned phantom, total="+currentCount);
        // listen for death to decrement count
        PhantomDeathNotifier notifier = p.gameObject.AddComponent<PhantomDeathNotifier>();
        notifier.spawner = this;
    }

    public void NotifyDeath(Phantom phantom)
    {
        currentCount = Mathf.Max(0, currentCount - 1);
    }
}

// small helper that notifies spawner when phantom destroyed
public class PhantomDeathNotifier : MonoBehaviour
{
    public PhantomSpawner spawner;
    void OnDestroy()
    {
        if (spawner != null)
            spawner.NotifyDeath(null);
    }
}