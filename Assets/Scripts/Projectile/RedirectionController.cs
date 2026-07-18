using Arena.NetworkedMotion;
using UnityEngine;

/// <summary>
/// The redirection controller of a mover (ADR 0005): the ONE home of the "resume on a new same-speed segment at
/// contact" reaction, consolidating the old Ricochet (reflect) and Deflect halves — semantically the SAME move
/// (redirect the source onto a fresh authoritative segment from the standoff point, preserving speed), differing
/// only in what basis the new heading comes from, which the hit's channel already selects:
///
/// - Struck ANOTHER mover (the victim resolves to a <see cref="NetworkedMotion"/>): adopt the struck mover's
///   heading — the old deflect, via <see cref="DeflectMath"/>.
/// - Struck a SURFACE (no motion on the victim): reflect the INCOMING direction across the surface normal — the old
///   ricochet, via <see cref="RicochetMath"/>. It reflects the direction captured at classification, NOT the live
///   velocity, so the redirect stays IDEMPOTENT for one hit (two redirections on one impact still bounce instead
///   of composing to the identity — the atomic-hit rule).
///
/// There are no per-channel tunables today, so the branch is on the presence of a second mover, not authored
/// config; if angle tuning ever varies per channel, a serialized per-channel struct is the seam to add. A
/// controller OWNS STATE (its <see cref="enabledChannels"/>) and exposes the reaction as its behaviour. Discovered
/// by presence at the hit site and dispatched the one <see cref="HitMessage"/>; also an
/// <see cref="IOwnedObjectContribution"/>, so an owner passive can add "your shots redirect" onto the objects it
/// spawns. Enabling it on the Projectile channel wakes the mover-vs-mover overlap sweep (ServerCollision's
/// effective-channel-mask gate); without any such handler the sweep never runs.
/// </summary>
public sealed class RedirectionController : MonoBehaviour, IHitMessageHandler, IOwnedObjectContribution
{
    [Tooltip("Which hit channels this KIND redirects on (the LayerMask idiom). Surface reflects across the normal; " +
             "Projectile adopts the struck mover's heading. Author whichever the object should react to.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Surface;

    public HitChannels EnabledHitChannels => enabledChannels;

    public void OnHit(HitMessage hit)
    {
        // The source's own motion — looked up from the source (this handler runs on it), the same channel-
        // independent lookup shape every handler uses. Absent = nothing to redirect.
        var self = hit.Source != null ? hit.Source.GetComponent<NetworkedMotion>() : null;
        if (self == null)
            return;

        var state = self.State;
        float speed = NetworkedMotionProcessor.VelocityAt(self.Strategy, in state).magnitude;
        if (speed < 0.0001f)
            return;   // a stationary mover (e.g. a laser) has no redirect; leave the segment unchanged

        // Resolve the struck thing's motion from the victim (channel-independent lookup): a struck mover has one →
        // adopt its heading (deflect); a surface/pawn has none → reflect below. The motion sits on the struck
        // object itself (the overlap reports the mover's own GameObject as the victim), so prefer the direct
        // GetComponent and fall back to a parent walk only if the victim was a child collider.
        NetworkedMotion other = null;
        if (hit.Victim != null)
            other = hit.Victim.GetComponent<NetworkedMotion>() ?? hit.Victim.GetComponentInParent<NetworkedMotion>();

        Vector3 dir;
        float newSpeed;
        if (other != null)
        {
            // Struck another mover: adopt its heading (the old deflect).
            var os = other.State;
            Vector3 otherForward = os.OriginForward;
            Vector3 otherVelocity = NetworkedMotionProcessor.VelocityAt(other.Strategy, in os);
            if (!DeflectMath.TryRedirect(otherForward, otherVelocity, speed, state.OriginSpeed, out dir, out newSpeed))
                return;
        }
        else
        {
            // Struck a surface: reflect the incoming direction across its normal (the old ricochet), speed intact.
            if (!RicochetMath.TryReflect(hit.Direction, hit.Normal, out dir, out float _))
                return;
            newSpeed = speed;
        }

        // Resume at the mover's CENTRE at contact (SourceCenterPoint), which stands off the struck face by the
        // collision radius — beginning at the surface Point would leave the mover embedded and re-colliding next tick.
        self.BeginSegment(hit.SourceCenterPoint, dir, newSpeed);
    }

    /// <summary>Owner contribution (self-cloning): add a redirection reaction onto <paramref name="target"/> unless
    /// it already has one, copying this instance's authored channels. Returns the added component, or null when one
    /// was already present (so pooled-reuse teardown never strips a handler the target was authored with).</summary>
    public Component Apply(GameObject target)
    {
        if (target.TryGetComponent<RedirectionController>(out _))
            return null;
        var clone = target.AddComponent<RedirectionController>();
        clone.enabledChannels = enabledChannels;
        return clone;
    }
}
