using UnityEngine;

public class DevilDaggersBulletImpact : ProjectileBulletImpact
{
    [SerializeField] ProjectileLifecycle lifecycle;
    [SerializeField] ProjectileState state;

    public override void HandleImpact(ProjectileHitbox hitbox, HitInfo hit)
    {
        if (!IsServerInitialized)
            return;

        if (hitbox.TryGetComponent<ProjectileState>(out var otherState))
        {
            if (otherState.Firer == state.Firer)
                return;
        }
        else if (hitbox.transform.parent != null 
            && hitbox.transform.parent.TryGetComponent<ProjectileState>(out var parentState))
        {
            if (parentState.Firer == state.Firer)
                return;
        }

        state.Health--;

        if (state.Health < 1)
        {
            // figure out if this can be called client-side
            lifecycle.Kill();
        }
    }
}