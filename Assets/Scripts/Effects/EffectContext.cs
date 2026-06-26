using UnityEngine;

/// <summary>
/// Everything a <see cref="WorldEffect"/> command needs about the thing invoking it and the thing it is
/// acting on, gathered by an imperative shell (a projectile's ServerCollision, or PawnAbilities) and
/// passed IN. This is the ONE context shared by both projectile hit reactions and ability effects, which
/// is what lets a single <see cref="WorldEffect"/> asset (e.g. DealDamage, SpawnProjectiles) be reused by
/// both.
///
/// Unlike the pure-core structs, this is deliberately Unity-coupled: it carries the actual
/// <see cref="Invoker"/> and <see cref="Target"/> <see cref="GameObject"/>s so an effect can
/// <c>GetComponent</c> anything off them and reach into any system — that freedom is the whole point of
/// WorldEffects (see the world-effect-commands skill). An effect decides for itself whether it can act
/// (e.g. "only if the target has a Health/IDamageable").
/// </summary>
public readonly struct EffectContext
{
    /// <summary>What is causing the effect: the projectile on a hit, or the caster's pawn on an ability use.</summary>
    public readonly GameObject Invoker;

    /// <summary>
    /// What the effect is acting on: the GameObject that was hit (a surface collider, a pawn, or another
    /// projectile). Always set for a collision; may be null for an ability that has no single target.
    /// </summary>
    public readonly GameObject Target;

    /// <summary>Impact point (collision) or origin point (ability).</summary>
    public readonly Vector3 Point;

    /// <summary>Surface normal at the impact; <see cref="Vector3.zero"/> when there is none (e.g. an ability).</summary>
    public readonly Vector3 Normal;

    /// <summary>Travel direction (collision) or aim direction (ability), normalized.</summary>
    public readonly Vector3 Direction;

    /// <summary>NetworkPlayer.ObjectId of the invoker's owner (the firer / caster), or -1.</summary>
    public readonly int InvokerPlayerId;

    public EffectContext(GameObject invoker, GameObject target, Vector3 point, Vector3 normal, Vector3 direction, int invokerPlayerId)
    {
        Invoker = invoker;
        Target = target;
        Point = point;
        Normal = normal;
        Direction = direction;
        InvokerPlayerId = invokerPlayerId;
    }
}
