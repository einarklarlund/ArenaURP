using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Reflect the invoking projectile's velocity across the surface normal and continue on a new segment.
/// Reads the invoker's <see cref="NetworkedMotion"/> for the live velocity, reflects it with the pure
/// <see cref="RicochetMath"/>, and begins a new authoritative segment (which syncs to every peer).
/// </summary>
[CreateAssetMenu(menuName = "Arena/World Effects/Ricochet", fileName = "Ricochet")]
public sealed class Ricochet : WorldEffect
{
    public override void Apply(in EffectContext ctx)
    {
        if (ctx.Invoker == null || !ctx.Invoker.TryGetComponent<NetworkedMotion>(out var motion))
            return;

        var state = motion.State;
        var config = motion.Config;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(in state, in config);

        if (RicochetMath.TryReflect(vel, ctx.Normal, out Vector3 dir, out float speed))
            motion.BeginSegment(ctx.Point, dir, speed);
    }
}
