using UnityEngine;

public class DestroyAfter : MonoBehaviour
{
    [SerializeField] private float seconds;

    private float startedAt;

    void Start()
    {
        startedAt = Time.time;
    }

    private void Update()
    {
        if (Time.time - startedAt > seconds)
        {
            Destroy(gameObject);
        }
    }
}
