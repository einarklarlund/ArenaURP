using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Triggers muzzle VFX (lightning bolts) when the weapon fires.
/// Pre-placed in the hierarchy to avoid runtime instantiation.
/// Re-parents to the projectile transform each shot so the effect follows the bullet.
/// </summary>
public class ProjectileVfx : NetworkBehaviour
{
    [SerializeField] private GameObject onHitPrefab;

    private static GameObjectPool onHitEffectPool;

    private void Awake()
    {
        onHitEffectPool ??= new GameObjectPool(onHitPrefab);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        PlayHitVfx(transform.position, transform.rotation);
    }

    private void PlayHitVfx(Vector3 position, Quaternion rotation)
    {
        // Get pooled on-hit effects
        var onHitEffects = onHitEffectPool.Get(position, rotation);

        foreach (var onHitEffect in GetComponentsInChildren<ParticleSystem>())
            onHitEffect.Play();

        if (onHitEffects.TryGetComponent<PooledLifetime>(out var lifetime))
            lifetime.Initialize(onHitEffectPool);
    }
}
