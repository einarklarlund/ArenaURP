using UnityEngine;

public class ProjectileDamageableImpact : ProjectileImpact
{
    protected ProjectileState state;
    protected ProjectileLifecycle lifecycle;

    protected override void Awake()
    {
        base.Awake();
        state = GetComponent<ProjectileState>();
        lifecycle = GetComponent<ProjectileLifecycle>();
    }

    protected virtual bool ShouldAvoidDamage(IDamageable damageable)
    {
        return damageable is Pawn pawn && pawn.ControllingPlayer.Value == state.Firer;
    }

    public void HandleImpact(HitInfo hit, IDamageable damageable)
    {
        if (IsServerInitialized)
        {
            foreach (var prefab in data.spawnOnDamageableImpact)
            {
                SpawnPrefab(prefab);
            }

            if (hit.point == Vector3.zero)
            {
                hit.normal = hit.transform.position - transform.position;
                hit.point = hit.collider.ClosestPoint(transform.position);
            }

            if (damageable != null && !ShouldAvoidDamage(damageable))
            {
                DamageInfo info = new()
                {
                    Amount    = data.damage,
                    Attacker  = state.Firer,
                    HitPoint  = hit.point,
                    Direction = transform.forward,
                    Type      = DamageType.Bullet
                };
                damageable.ServerTakeDamage(info);
                lifecycle.Kill();
            }
        }
        else
        {
            ActivateVfx();
        }
    }
}
