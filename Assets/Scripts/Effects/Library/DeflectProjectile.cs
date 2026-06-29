using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Redirect the invoking projectile along the OTHER projectile's heading, preserving this one's speed (ports
/// the old ProjectileBulletImpact). Reads both projectiles' <see cref="NetworkedMotion"/>, computes the
/// redirect with the pure <see cref="DeflectMath"/>, and begins a new synced segment from the hit point.
/// </summary>
public sealed class DeflectProjectile : IEffect
{
    public void Apply(IEffectContext ctx)
    {
        if (ctx.Invoker == null || !ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            return;

        var state = motion.State;
        var config = motion.Config;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in state, in config);

        Vector3 otherForward = Vector3.zero, otherVelocity = Vector3.zero;
        if (ctx is ITargetContext tc && tc.Target != null && tc.Target.TryGetComponent<NetworkedMotion>(out var otherMotion))
        {
            var os = otherMotion.State;
            var oc = otherMotion.Config;
            otherForward = os.OriginForward;
            otherVelocity = NetworkedMotionProcessor.VelocityAt(in os, in oc);
        }

        if (DeflectMath.TryRedirect(otherForward, otherVelocity, vel.magnitude, state.OriginSpeed, out Vector3 dir, out float speed))
        {
            Vector3 origin = ctx is IPointContext point ? point.Point : ctx.Invoker.transform.position;
            motion.BeginSegment(origin, dir, speed);
        }
    }
}
