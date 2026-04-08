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
    protected ProjectileData data;
    private ProjectileState state;
    [SerializeField] private GameObject lightningBoltsPrefab;
    [SerializeField] private GameObject onHitPrefab;
    [SerializeField] private Transform visuals;

    private GameObject lightningBoltsInstance;
    private static GameObjectPool lightningBoltsPool;
    private static GameObjectPool onHitEffectPool;
    private Vector3 lastPosition;

    private void Awake()
    {
        data = GetComponent<ProjectileData>();
        state = GetComponent<ProjectileState>();
        lightningBoltsPool ??= new GameObjectPool(lightningBoltsPrefab);
        onHitEffectPool ??= new GameObjectPool(onHitPrefab);
        lastPosition = transform.position;
    }

    private void Update()
    {
        if (lightningBoltsInstance != null)
            lightningBoltsInstance.transform.position = transform.position;

        lastPosition = transform.position;
    }

    public void HideVisuals()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = false;

        // Stop trail particle system
        lightningBoltsInstance.GetComponent<ParticleSystem>().Stop();

        // Get pooled on-hit effects
        var onHitEffects = onHitEffectPool.Get(transform.position, transform.rotation);

        foreach (var onHitEffect in GetComponentsInChildren<ParticleSystem>())
            onHitEffect.Play();

        if (onHitEffects.TryGetComponent<PooledLifetime>(out var lifetime))
            lifetime.Initialize(onHitEffectPool);
    }

    public void ShowVisuals()
    {
        // should add some IsServerInitialized guard here
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = true;

        lightningBoltsInstance = lightningBoltsPool.Get(transform.position, transform.rotation);
        
        if (lightningBoltsInstance.TryGetComponent<PooledLifetime>(out var lifetime))
            lifetime.Initialize(lightningBoltsPool);
        
        lightningBoltsInstance.GetComponent<ParticleSystem>().Play();
    }
}
