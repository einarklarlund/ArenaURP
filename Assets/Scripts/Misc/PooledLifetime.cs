using UnityEngine;

public class PooledLifetime : MonoBehaviour
{
    [SerializeField] private float lifetime = 10f;
    private GameObjectPool pool;

    public void Initialize(GameObjectPool pool)
    {
        this.pool = pool;
    }

    public void ReturnToPool()
    {
        if (pool != null)
            pool.Return(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        Invoke(nameof(ReturnToPool), lifetime);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(ReturnToPool));
    }
}
