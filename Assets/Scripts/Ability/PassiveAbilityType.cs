using System.Collections.Generic;
using MackySoft.SerializeReferenceExtensions;
using UnityEngine;

/// <summary>
/// Always-on ability: contributes movement/weapon modifiers while held in the inventory (no max) and can add
/// extra operations to the projectiles the owner fires (looked up server-side, never payloaded). Value fields
/// default to 1 (no change), so a freshly-authored asset is a no-op until filled in.
///
/// Projectile contributions are AUTHORED AS DATA — three <c>[SerializeReference]</c> operation lists, one per
/// hit category (a "bouncy" powerup adds a <see cref="Ricochet"/> to <see cref="ProjectileOnSurface"/>; a
/// bonus-damage powerup adds a fixed <see cref="DealDamage"/> to <see cref="ProjectileOnPlayer"/>).
/// PawnAbilities aggregates these across equipped passives and exposes them to the owner's projectiles.
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

    [Header("Projectile contributions (extra operations on the owner's projectiles)")]
    [Tooltip("Added to the owner's projectiles' surface-hit operations.")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectileSurfaceCollision>> ProjectileOnSurface = new();

    [Tooltip("Added to the owner's projectiles' player-hit operations (e.g. a bonus DealDamage that does not despawn).")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectilePlayerCollision>> ProjectileOnPlayer = new();

    [Tooltip("Added to the owner's projectiles' projectile-hit operations.")]
    [SerializeReference, SubclassSelector]
    public List<IEffect<ProjectileProjectileCollision>> ProjectileOnProjectile = new();

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
