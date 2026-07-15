/// <summary>
/// The on-spawn buffs a passive ability applies to each networked object its owner spawns — authored DATA, not a
/// command (the component-handler migration, ADR 0003: varies-in-data → an authored struct). One identity-default
/// struct of multipliers, the shape shared with <see cref="MovementModifier"/> / <see cref="WeaponModifier"/>:
/// a passive's <c>SpawnBuff</c> field, folded by <see cref="SpawnBuffReceiver"/> which — on every peer, at the
/// spawn instant — walks the owner's roster, MULTIPLIES the contributions together, and pokes the spawned
/// object's generic sibling systems once each (never a raw scale/health write; the idempotent per-spawn
/// multiplier layer that resets under pooling).
///
/// Each channel reaches its system through that system's dedicated multiplier seam:
/// <see cref="SpeedMultiplier"/> → <c>NetworkedMotion.SetSpeedMultiplier</c> (has a sync channel; a late client
/// self-heals from the seeded segment), <see cref="ScaleMultiplier"/> → both
/// <c>SpawnScale.SetScaleMultiplier</c> (per-peer cosmetic) and <c>ServerCollision.SetRadiusMultiplier</c>
/// (server-only collision), <see cref="MaxHealthMultiplier"/> → <c>Health.SetMaxHealthMultiplier</c>
/// (server-authoritative durability). A spawnable carrying only some of those siblings is buffed on whichever it
/// has. Identity (all 1) contributes nothing to the fold.
/// </summary>
[System.Serializable]
public struct SpawnModifier
{
    [UnityEngine.Tooltip("Multiplies the spawned object's initial motion speed.")]
    public float SpeedMultiplier;
    [UnityEngine.Tooltip("Multiplies the spawned object's visual scale and, where present, its collision radius.")]
    public float ScaleMultiplier;
    [UnityEngine.Tooltip("Multiplies the spawned object's max health (durability) over its authored baseline.")]
    public float MaxHealthMultiplier;

    public static SpawnModifier Identity => new()
    {
        SpeedMultiplier = 1f,
        ScaleMultiplier = 1f,
        MaxHealthMultiplier = 1f,
    };
}
