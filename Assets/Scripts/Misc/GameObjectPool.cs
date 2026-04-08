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
        GameObject instance;

        if (pool.Count > 0)
        {
            instance = pool.Dequeue();
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
        }
        else
        {
            instance = Object.Instantiate(prefab, position, rotation);
        }

        return instance;
    }

    public void Return(GameObject instance)
    {
        instance.SetActive(false);
        pool.Enqueue(instance);
    }
}
