using UnityEngine;

/// <summary>
/// A single, reusable unit of "something that happens in the world", authored as a ScriptableObject
/// command. <see cref="Apply"/> is the whole contract: given an <see cref="EffectContext"/> (who invoked
/// it, what it's acting on, where), do the thing — deal damage, spawn projectiles, knock back, debuff,
/// ricochet, play a VFX, anything.
///
/// WorldEffects are deliberately COUPLED to the Unity API. An effect may <c>GetComponent</c> off the
/// invoker or target, query the scene, spawn prefabs, call into any system — this freedom is exactly why
/// they exist: the game's depth comes from effects interacting with many systems in arbitrary ways
/// (pickups, level-ups, on-hit modifiers). Because a WorldEffect is a flyweight SO shared by every user,
/// it must hold NO per-invocation state: read the context, act, return. The effect itself decides whether
/// it can act on a given target (e.g. only if it has an IDamageable) — there is no separate gate.
///
/// The SAME effect asset is reused across systems: a projectile's <see cref="HitReaction"/> runs a list of
/// these on a hit, and PawnAbilities runs a list of these on an ability use, each building the appropriate
/// <see cref="EffectContext"/>. See the world-effect-commands skill.
/// </summary>
public abstract class WorldEffect : ScriptableObject
{
    /// <summary>Perform the effect. Runs in a server-authoritative context; never gate or store state.</summary>
    public abstract void Apply(in EffectContext ctx);
}
