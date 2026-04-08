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
        if (damageable != null && !ShouldAvoidDamage(damageable))
        {
            if (IsServerInitialized)
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
            }

            lifecycle.Kill();
        }
    }
}
