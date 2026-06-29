using System.Collections.Generic;
using UnityEngine;

public class GameObjectPool
{
    private readonly GameObject prefab;
    private readonly Queue<GameObject> pool = new();

    public GameObjectPool(GameObject prefab)
    {
        this.prefab = prefab;
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        GameObject instance = null;

        // Discard any pooled instances that were destroyed out from under us (e.g. a scene unload —
        // this pool can be static and outlive the GameObjects it cached). Unity's overloaded
        // == null is true for destroyed objects, so this safely skips them.
        while (pool.Count > 0 && instance == null)
            instance = pool.Dequeue();

        if (instance == null)
        {
            instance = Object.Instantiate(prefab, position, rotation);
        }
        else
        {
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
        }

        return instance;
    }

    public void Return(GameObject instance)
    {
        instance.SetActive(false);
        pool.Enqueue(instance);
    }
}
