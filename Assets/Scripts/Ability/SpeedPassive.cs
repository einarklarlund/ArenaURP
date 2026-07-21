using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="NetworkedMotion"/>: multiplies the speed of an object the owner spawns
/// (&gt;1 faster, &lt;1 slower, 1 = no effect). Coupled to the ONE component it affects, not to the lifecycle
/// moment that triggers it — a target applies it when it spawns, but this passive knows only
/// <see cref="NetworkedMotion"/>. Authored on a child of the shared passive registry, composable with other
/// passives on the same KIND (a fast + big bullet = <see cref="SpeedPassive"/> + <see cref="ScalePassive"/>).
///
/// The seam is a per-spawn MULTIPLIER layer reset to identity in ResetState, so <see cref="Apply"/> COMPOSES
/// (reads the current multiplier and multiplies onto it) — several owned kinds each carrying a speed passive
/// multiply together. Applied once per spawn, so composing never double-counts; pooled reuse starts clean.
/// </summary>
public sealed class SpeedPassive : Passive
{
    [Tooltip("Multiplies the spawned object's speed. 1 = no effect; >1 faster, <1 slower. Several owned speed " +
             "passives multiply together.")]
    [SerializeField] private float multiplier = 1f;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponent<NetworkedMotion>() != null;

    public override void Apply(GameObject target)
    {
        if (multiplier != 1f && target.TryGetComponent<NetworkedMotion>(out var motion))
            motion.SetSpeedMultiplier(motion.SpeedMultiplier * multiplier);
    }
}
