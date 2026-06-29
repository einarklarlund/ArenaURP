using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Triggered ability (used with F): builds a set of <see cref="IEffect"/> commands IN CODE from serialized
/// config + feature flags, has limited uses and an optional cooldown, and occupies one of the 5 active
/// inventory slots. The effects are the SAME reusable <see cref="IEffect"/> commands a projectile reaction
/// runs — composed here rather than referenced as assets — so an effect written once works for both.
/// </summary>
[CreateAssetMenu(fileName = "New Active Ability", menuName = "Arena/Ability/Active")]
public sealed class ActiveAbilityType : AbilityType
{
    [Min(1)] public int Uses = 1;
    [Min(0f)] public float Cooldown = 0f;

    [Header("Spawn-projectiles effect (feature flag: set a type to enable)")]
    [Tooltip("If set, using the ability spawns this projectile type, fanned by the count/spread below.")]
    public ProjectileType SpawnProjectile;
    [Min(1)] public int SpawnCount = 1;
    [Tooltip("0 = all along aim; 360 = even ring; in between = arc centred on aim.")]
    public float SpawnSpread = 0f;

    /// <summary>Compose the effects this active ability runs when used, from its config + flags.</summary>
    public void BuildEffects(List<IEffect> effects)
    {
        if (SpawnProjectile != null)
            effects.Add(new SpawnProjectiles(SpawnProjectile, SpawnCount, SpawnSpread));
    }
}
