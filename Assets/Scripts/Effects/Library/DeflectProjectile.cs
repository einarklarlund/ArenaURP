using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Redirect the source projectile along the OTHER projectile's heading, preserving this one's speed (ports the
/// old ProjectileBulletImpact). Reads both projectiles' motion systems from the context —
/// <see cref="IMotionContext.Motion"/> (the source) and <see cref="ProjectileProjectileCollision.OtherMotion"/>
/// (the struck projectile) — computes the redirect with the pure <see cref="DeflectMath"/>, and begins a new
/// synced segment from the hit point. Deliberately typed to the CONCRETE
/// <see cref="ProjectileProjectileCollision"/> rather than a system capability: its correctness depends on the
/// target being a projectile with its own motion, so it should only be authorable on projectile-hit lists — the
/// concrete type is the contract.
/// </summary>
[System.Serializable]
public sealed class DeflectProjectile : IEffect<ProjectileProjectileCollision>
{
    public void Apply(ProjectileProjectileCollision ctx)
    {
        if (ctx.Motion == null)
            return;

        var state = ctx.Motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(ctx.Motion.Strategy, in state);

        Vector3 otherForward = Vector3.zero, otherVelocity = Vector3.zero;
        if (ctx.OtherMotion != null)
        {
            var os = ctx.OtherMotion.State;
            otherForward = os.OriginForward;
            otherVelocity = NetworkedMotionProcessor.VelocityAt(ctx.OtherMotion.Strategy, in os);
        }

        // Resume from the mover's centre (SourceCenterPoint); in this projectile-vs-projectile channel that is the
        // classified Point itself (this mover's own position), so this is unchanged behaviour — but it keeps both
        // segment-redirect effects consistently anchored on the source centre rather than a bare impact point.
        if (DeflectMath.TryRedirect(otherForward, otherVelocity, vel.magnitude, state.OriginSpeed, out Vector3 dir, out float speed))
            ctx.Motion.BeginSegment(ctx.SourceCenterPoint, dir, speed);
    }
}
