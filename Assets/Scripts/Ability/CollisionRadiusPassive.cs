using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="ServerCollision"/>: multiplies the COLLISION radius of an object the owner
/// spawns (&gt;1 hits from further, &lt;1 tighter, 1 = no effect). Server-only (collision resolves on the server);
/// the matching visual size is the separate <see cref="ScalePassive"/>. Coupled to the ONE component it affects,
/// not to the spawn moment that triggers it.
///
/// The seam is a per-spawn MULTIPLIER layer reset to identity in ResetState, so <see cref="Apply"/> COMPOSES —
/// several owned radius passives multiply together. Applied once per spawn; pooled reuse starts clean.
/// </summary>
public sealed class CollisionRadiusPassive : Passive
{
    [Tooltip("Multiplies the spawned object's collision radius. 1 = no effect; >1 hits from further, <1 tighter. " +
             "Several owned radius passives multiply together.")]
    [SerializeField] private float multiplier = 1f;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponent<ServerCollision>() != null;

    public override void Apply(GameObject target)
    {
        if (multiplier != 1f && target.TryGetComponent<ServerCollision>(out var collision))
            collision.SetRadiusMultiplier(collision.RadiusMultiplier * multiplier);
    }
}
