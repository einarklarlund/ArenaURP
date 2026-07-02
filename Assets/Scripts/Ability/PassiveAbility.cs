using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The stateful half of an always-on ability, living on the ability's INSTANCE prefab — a logical
/// NetworkObject spawned by <see cref="PawnPassiveAbilities"/> and parented under the owning pawn. The pawn
/// value multipliers and the extra projectile operations are serialized prefab data (runtime-tunable per
/// instance); while this instance exists under a pawn, <see cref="PawnPassiveAbilities"/> folds the
/// multipliers into the pawn's movement/weapon modifier lists and aggregates the projectile lists for the
/// owner-contribution path.
///
/// Identity (display name / icon) resolves through the sibling <see cref="TypeObjectRoot"/>'s baked
/// <see cref="AbilityType"/>.
/// </summary>
public sealed class PassiveAbility : NetworkBehaviour
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
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectileSurfaceCollision>> ProjectileOnSurface = new();

    [Tooltip("Added to the owner's projectiles' player-hit operations (e.g. an extra DealDamage).")]
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectilePlayerCollision>> ProjectileOnPlayer = new();

    [Tooltip("Added to the owner's projectiles' projectile-hit operations.")]
    [SerializeReference, EffectSelector]
    public List<IEffect<ProjectileProjectileCollision>> ProjectileOnProjectile = new();

    private PawnPassiveAbilities _inventory;

    /// <summary>This ability's identity SO, via the sibling TypeObjectRoot (null if none baked).</summary>
    public AbilityType Type => TryGetComponent<TypeObjectRoot>(out var root) ? root.Type as AbilityType : null;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _inventory = GetComponentInParent<PawnPassiveAbilities>();
        NotifyInventory();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        NotifyInventory();
        _inventory = null;
    }

    private void NotifyInventory()
    {
        if (_inventory != null)
            _inventory.NotifyInventoryChanged();
    }

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
