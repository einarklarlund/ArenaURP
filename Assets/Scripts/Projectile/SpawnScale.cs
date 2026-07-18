using FishNet.Object;
using UnityEngine;

/// <summary>
/// Owns the per-spawn visual scale of a pooled spawnable, so a scale buff never mutates <c>transform.localScale</c>
/// directly (raw transform state has no ResetState owner and would leak a previous owner's buff across pooled
/// reuse). Baseline is the prefab-authored localScale, captured once in <see cref="Awake"/> — pooling never
/// re-runs Awake, and buffs can only run later (at OnStartNetwork via <c>SpawnContributionReceiver</c>), so the capture is
/// always the clean authored value. Buffs go through the idempotent <see cref="SetScaleMultiplier"/>;
/// <see cref="ResetState"/> restores the baseline so pooled reuse starts unbuffed.
///
/// Scale has no sync channel — this is per-peer cosmetic state by design (a peer that missed a just-granted
/// passive renders that one spawn un-buffed; see OnSpawnContext / BuffScale). Generic: knows nothing of
/// the projectile concept, works on any owned spawnable.
/// </summary>
public sealed class SpawnScale : NetworkBehaviour
{
    private Vector3 _baseline;
    private bool _captured;
    private float _multiplier = 1f;   // per-spawn buff layer; reset to identity in ResetState

    private void Awake()
    {
        _baseline = transform.localScale;
        _captured = true;
    }

    /// <summary>
    /// Multiply the authored baseline scale. Order-independent (house rule) — no sibling reads scale during init,
    /// but the shape matches <see cref="NetworkedMotion.SetSpeedMultiplier"/> so on-spawn buffs are uniform.
    /// </summary>
    public void SetScaleMultiplier(float multiplier)
    {
        _multiplier = multiplier;
        if (_captured)
            transform.localScale = _baseline * _multiplier;
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _multiplier = 1f;
        if (_captured)                 // guard: never write a zero baseline if ResetState somehow precedes Awake
            transform.localScale = _baseline;
    }
}
