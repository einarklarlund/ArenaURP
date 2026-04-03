using UnityEngine;

public class ProjectileHitEffects : MonoBehaviour
{
    protected ProjectileData data;

    public void OnInitialize(ProjectileData projectileData)
    {
        data = projectileData;
    }

    public virtual void PlayHitEffects(Vector3 point, Vector3 normal)
    {
        if (data.IsServerOnlyInitialized) return;
    }
}
