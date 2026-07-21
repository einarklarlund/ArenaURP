using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="HealthController"/>: multiplies the max health of the target it is applied to
/// (&gt;1 tougher, &lt;1 frailer, 1 = no effect). <see cref="HealthController"/> is the one health system shared by
/// pawns, projectiles and props, so this reads as pawn durability when applied to a pawn and projectile durability
/// when applied to a spawned bullet — the passive is coupled to the component, not to which kind of target carries
/// it. Coupled to the ONE component it affects, not to the spawn/grant moment that triggers it.
///
/// The seam is a per-spawn MULTIPLIER layer reset to identity in ResetState, so <see cref="Apply"/> COMPOSES —
/// several owned max-health passives multiply together. Applied once per spawn; pooled reuse starts clean.
/// </summary>
public sealed class MaxHealthPassive : Passive
{
    [Tooltip("Multiplies the target's max health. 1 = no effect; >1 tougher, <1 frailer. Several owned max-health " +
             "passives multiply together.")]
    [SerializeField] private float multiplier = 1f;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponent<HealthController>() != null;

    public override void Apply(GameObject target)
    {
        if (multiplier != 1f && target.TryGetComponent<HealthController>(out var health))
            health.SetMaxHealthMultiplier(health.MaxHealthMultiplier * multiplier);
    }
}
