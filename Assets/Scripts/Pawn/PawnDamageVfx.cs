using UnityEngine;

public class PawnDamageVfx : MonoBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] GameObject onHitPrefab;
    [SerializeField] GameObject onDeathPrefab;

    private static GameObjectPool onHitEffectPool;
    private static GameObjectPool onDeathEffectPool;

    private void Awake()
    {
        onHitEffectPool ??= new GameObjectPool(onHitPrefab);
        onDeathEffectPool ??= new GameObjectPool(onDeathPrefab);
        pawn.OnDamageTaken += OnDamageTaken;
        pawn.OnDeath += OnDeath;
    }

    private void OnDestroy()
    {
        pawn.OnDamageTaken -= OnDamageTaken;
        pawn.OnDeath -= OnDeath;
    }

    private void OnDamageTaken(DamageInfo damageInfo)
    {
        // Get pooled on-hit effects
        var onHitEffects = onHitEffectPool.Get(damageInfo.HitPoint, Quaternion.LookRotation(damageInfo.Direction));
        var lifetime = onHitEffects.GetComponent<PooledLifetime>();
        onHitEffects.GetComponent<ParticleSystem>().Play();

        if (lifetime != null)
            lifetime.Initialize(onHitEffectPool);
    }

    private void OnDeath(DamageInfo damageInfo)
    {
        var onDeathEffects = onDeathEffectPool.Get(damageInfo.HitPoint, Quaternion.LookRotation(damageInfo.Direction));
        var lifetime = onDeathEffects.GetComponent<PooledLifetime>();
        onDeathEffects.GetComponent<ParticleSystem>().Play();

        if (lifetime != null)
            lifetime.Initialize(onDeathEffectPool);
    }
}