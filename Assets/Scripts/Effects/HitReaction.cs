using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A projectile's response to one category of collision: an authored, reusable bundle of
/// <see cref="WorldEffect"/>s that fire when the projectile hits something. A ServerCollision component
/// holds a reaction per category and runs the matching one on a hit; the effects are the shared
/// <see cref="WorldEffect"/> vocabulary, so the same effect assets are reused here and by abilities.
///
/// The base is abstract; author the CONCRETE per-category subclasses (<see cref="SurfaceHitReaction"/> /
/// <see cref="ProjectileHitReaction"/> / <see cref="EnemyHitReaction"/>) so that typed slots on the
/// consumer can't be mis-filled — you cannot drop a surface reaction into the enemy slot. The category
/// difference is purely about WHERE a reaction may be slotted; all three run their effect list the same
/// way.
/// </summary>
public abstract class HitReaction : ScriptableObject
{
    [Tooltip("The world effects that fire, in order, when this reaction runs.")]
    [SerializeField] private List<WorldEffect> effects = new();

    /// <summary>The effects this reaction runs, in order.</summary>
    public IReadOnlyList<WorldEffect> Effects => effects;

    /// <summary>Run every effect against the hit context, in author order. Null entries are skipped.</summary>
    public void React(in EffectContext ctx)
    {
        for (int i = 0; i < effects.Count; i++)
            effects[i]?.Apply(in ctx);
    }

    /// <summary>
    /// Build a runtime reaction of the given category from a set of effects, in code rather than as an
    /// authored asset. Used by tests and by systems that compose reactions at runtime.
    /// </summary>
    public static T CreateRuntime<T>(params WorldEffect[] effects) where T : HitReaction
    {
        var reaction = CreateInstance<T>();
        reaction.effects = new List<WorldEffect>(effects);
        return reaction;
    }
}

/// <summary>A reaction slottable only where a SURFACE (non-player world geometry) hit is handled.</summary>
[CreateAssetMenu(menuName = "Arena/Hit Reactions/Surface", fileName = "SurfaceHitReaction")]
public sealed class SurfaceHitReaction : HitReaction { }

/// <summary>A reaction slottable only where a PROJECTILE-vs-projectile hit is handled.</summary>
[CreateAssetMenu(menuName = "Arena/Hit Reactions/Projectile", fileName = "ProjectileHitReaction")]
public sealed class ProjectileHitReaction : HitReaction { }

/// <summary>A reaction slottable only where an ENEMY (player) hit is handled.</summary>
[CreateAssetMenu(menuName = "Arena/Hit Reactions/Enemy", fileName = "EnemyHitReaction")]
public sealed class EnemyHitReaction : HitReaction { }
