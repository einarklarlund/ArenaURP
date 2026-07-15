using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// Hit handler (component-handler shape, ADR 0003): redirect the source projectile along the OTHER projectile's
/// heading, preserving this one's speed (ports the old DeflectProjectile effect). Reads both projectiles' motion —
/// the source's from <see cref="HitContext.Motion"/>, the struck projectile's from <see cref="HitContext.OtherMotion"/>
/// — computes the redirect with the pure <see cref="DeflectMath"/>, and begins a new synced segment.
///
/// <see cref="HitContext.OtherMotion"/> is populated ONLY on the Projectile channel, so this behaviour supports
/// exactly that channel (<see cref="SupportedChannels"/>) — the supported-channels filter is what guarantees the
/// handle is non-null where this reads it. A KIND carrying this handler wakes the projectile-vs-projectile overlap
/// sweep (see ServerCollision's effective-channel-mask gate); without any such handler the sweep never runs.
/// </summary>
public sealed class DeflectProjectileHandler : MonoBehaviour, IHitHandler
{
    [Tooltip("Which hit channels this KIND deflects on — per-prefab-copy authoring (the LayerMask idiom). A deflect " +
             "reads the struck projectile's motion, which only the Projectile channel provides.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Projectile;

    public HitChannels SupportedChannels => HitChannels.Projectile;
    public HitChannels EnabledChannels => enabledChannels;

    public void OnHit(in HitContext context)
    {
        if (context.Motion == null)
            return;

        var state = context.Motion.State;
        Vector3 vel = NetworkedMotionProcessor.VelocityAt(context.Motion.Strategy, in state);

        Vector3 otherForward = Vector3.zero, otherVelocity = Vector3.zero;
        if (context.OtherMotion != null)
        {
            var os = context.OtherMotion.State;
            otherForward = os.OriginForward;
            otherVelocity = NetworkedMotionProcessor.VelocityAt(context.OtherMotion.Strategy, in os);
        }

        if (DeflectMath.TryRedirect(otherForward, otherVelocity, vel.magnitude, state.OriginSpeed, out Vector3 dir, out float speed))
            context.Motion.BeginSegment(context.SourceCenterPoint, dir, speed);
    }
}
