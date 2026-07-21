using UnityEngine;

/// <summary>
/// A passive coupled to <see cref="SpawnScale"/>: multiplies the VISUAL scale of an object the owner spawns
/// (&gt;1 bigger, &lt;1 smaller, 1 = no effect). Cosmetic and per-peer — collision size is the separate
/// <see cref="CollisionRadiusPassive"/> (one component per passive), so a bullet that both LOOKS and HITS bigger
/// composes both on its KIND. Coupled to the ONE component it affects, not to the spawn moment that triggers it.
///
/// The seam is a per-spawn MULTIPLIER layer reset to identity in ResetState, so <see cref="Apply"/> COMPOSES —
/// several owned scale passives multiply together. Applied once per spawn; pooled reuse starts clean.
/// </summary>
public sealed class ScalePassive : Passive
{
    [Tooltip("Multiplies the spawned object's visual scale. 1 = no effect; >1 bigger, <1 smaller. Several owned " +
             "scale passives multiply together.")]
    [SerializeField] private float multiplier = 1f;

    public override bool CanApplyTo(GameObject target)
        => target != null && target.GetComponent<SpawnScale>() != null;

    public override void Apply(GameObject target)
    {
        if (multiplier != 1f && target.TryGetComponent<SpawnScale>(out var scale))
            scale.SetScaleMultiplier(scale.ScaleMultiplier * multiplier);
    }
}
