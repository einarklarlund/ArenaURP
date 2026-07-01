using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Redirect the invoking projectile along the OTHER projectile's heading, preserving this one's speed (ports
/// the old ProjectileBulletImpact). Reads both projectiles' <see cref="NetworkedMotion"/>, computes the
/// redirect with the pure <see cref="DeflectMath"/>, and begins a new synced segment from the hit point.
/// Deliberately typed to the CONCRETE <see cref="ProjectileProjectileCollision"/> rather than a capability
/// interface: its correctness depends on the target being a projectile (it reads the target's NetworkedMotion),
/// so it should only be authorable on projectile-hit lists — the concrete type is the contract.
/// </summary>
[System.Serializable]
public sealed class DeflectProjectile : IEffect<ProjectileProjectileCollision>
{
    public void Apply(ProjectileProjectileCollision ctx)
    {
        if (ctx.Owner == null || !ctx.Owner.TryGetComponent<NetworkedMotion>(out var motion))
            return;

        var state = motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(motion.Strategy, in state);

        Vector3 otherForward = Vector3.zero, otherVelocity = Vector3.zero;
        if (ctx.Target != null && ctx.Target.TryGetComponent<NetworkedMotion>(out var otherMotion))
        {
            var os = otherMotion.State;
            otherForward = os.OriginForward;
            otherVelocity = NetworkedMotionProcessor.VelocityAt(otherMotion.Strategy, in os);
        }

        if (DeflectMath.TryRedirect(otherForward, otherVelocity, vel.magnitude, state.OriginSpeed, out Vector3 dir, out float speed))
            motion.BeginSegment(ctx.Point, dir, speed);
    }
}
