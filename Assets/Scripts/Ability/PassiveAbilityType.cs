using UnityEngine;

/// <summary>
/// Always-on ability: contributes movement/weapon modifiers while held in the inventory (no max) and can add
/// extra hit reactions to the projectiles the owner fires (looked up server-side, never payloaded). Value
/// fields default to 1 (no change) and flags to off, so a freshly-authored asset is a no-op until filled in.
///
/// Projectile contributions are built IN CODE from feature flags via <see cref="BuildProjectileContributions"/>
/// — e.g. a "bouncy" powerup sets <see cref="GrantsRicochet"/>; a bonus-damage powerup sets
/// <see cref="BonusEnemyDamage"/>. PawnAbilities aggregates these across equipped passives and exposes them
/// to the owner's projectiles.
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

    [Header("Projectile contributions (feature flags)")]
    [Tooltip("Extra fixed damage this passive adds to the owner's projectiles on an enemy hit (0 = none).")]
    public int BonusEnemyDamage = 0;
    [Tooltip("If set, the owner's projectiles reflect off surfaces (a bouncy-projectile powerup).")]
    public bool GrantsRicochet = false;

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

    /// <summary>
    /// Append this passive's extra projectile effects to the owner's aggregated reactions (built in code from
    /// the flags above). A bonus is a fixed-amount DealDamage that does NOT despawn the projectile, so it
    /// stacks on top of the projectile's own enemy reaction.
    /// </summary>
    public void BuildProjectileContributions(ProjectileReactions r)
    {
        if (BonusEnemyDamage > 0)
            r.OnEnemy.Add(new DealDamage(useInvokerDamage: false, amount: BonusEnemyDamage, despawnAfter: false));
        if (GrantsRicochet)
            r.OnSurface.Add(new Ricochet());
    }
}
