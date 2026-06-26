using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Always-on ability: contributes movement/weapon modifiers while held in the inventory (no max) and can
/// add extra hit reactions to the projectiles the owner fires (looked up server-side, never payloaded).
/// Value fields default to 1 (no change) so a freshly-authored asset is a no-op until filled in.
/// </summary>
[CreateAssetMenu(fileName = "New Passive Ability", menuName = "Arena/Ability/Passive")]
public sealed class PassiveAbilityType : AbilityType
{
    [Header("Pawn value modifiers (1 = no change)")]
    public float WalkSpeedMultiplier = 1f;
    public float WalkAccelerationMultiplier = 1f;
    public float JumpForceMultiplier = 1f;
    public float GravityMultiplier = 1f;
    public float FireRateMultiplier = 1f;
    public float SpreadMultiplier = 1f;

    [Header("Extra reactions added to the owner's projectiles")]
    [Tooltip("Hit reaction assets this passive adds to the projectiles the player fires (looked up server-side, never payloaded). e.g. a bonus-damage EnemyHitReaction.")]
    public List<EnemyHitReaction> ProjectileOnEnemy = new();
    public List<SurfaceHitReaction> ProjectileOnSurface = new();
    public List<ProjectileHitReaction> ProjectileOnProjectile = new();

    /// <summary>Build the movement modifier this passive contributes while equipped.</summary>
    public MovementModifier ToMovementModifier() => new()
    {
        WalkSpeedMultiplier = WalkSpeedMultiplier,
        WalkAccelerationMultiplier = WalkAccelerationMultiplier,
        JumpForceMultiplier = JumpForceMultiplier,
        GravityMultiplier = GravityMultiplier,
        PreImpulseVelocityMultiplier = 1f,
    };

    /// <summary>Build the weapon modifier this passive contributes while equipped.</summary>
    public WeaponModifier ToWeaponModifier() => new()
    {
        FireRateMultiplier = FireRateMultiplier,
        SpreadMultiplier = SpreadMultiplier,
    };
}
