using UnityEngine;

/// <summary>
/// What an <see cref="IEffect"/> is handed when it runs — built by an imperative shell (a projectile's
/// ServerCollision, or PawnAbilities) and passed IN. The context is the SINGLE composable mechanism for
/// "the data an effect needs": it advertises what this invocation provides through CAPABILITY interfaces,
/// and each effect pulls only the slice it cares about (<c>ctx is ISurfaceContext s</c>).
///
/// Why capabilities live on a polymorphic CONTEXT (not flat fields, not the effect's type): "implements
/// interface X" is a per-type fact, so the only way "do I have a surface normal?" can vary by what happened
/// is for the concrete context type to vary — <see cref="CollisionContext"/> implements
/// <see cref="ISurfaceContext"/>; <see cref="AimContext"/> does not. The base carries what EVERY invocation
/// has (the invoker + owner identity); the role interfaces carry what only SOME have. There is no
/// participant/event split and no per-axis effect subclass — one rule: an effect declares its needs by the
/// capabilities it queries; the shell builds the context that truthfully advertises this site's data.
///
/// These are reference types, built fresh per invocation (one small server-side allocation per hit /
/// ability use — negligible; pool if a profiler ever complains).
/// </summary>
public interface IEffectContext
{
    /// <summary>What is causing the effect: the projectile on a hit, or the caster's pawn on an ability use.</summary>
    GameObject Invoker { get; }

    /// <summary>NetworkPlayer.ObjectId of the invoker's owner (the firer / caster), or -1.</summary>
    int InvokerPlayerId { get; }

    /// <summary>The resolved owner NetworkPlayer (for damage attribution), or null. Resolved by the shell.</summary>
    NetworkPlayer InvokerPlayer { get; }
}

/// <summary>The effect acts on a specific target GameObject (the thing hit, or an ability's single target).</summary>
public interface ITargetContext : IEffectContext { GameObject Target { get; } }

/// <summary>The invocation happened at a world position (impact point, or an ability's origin).</summary>
public interface IPointContext : IEffectContext { Vector3 Point { get; } }

/// <summary>The invocation has a direction (a projectile's travel, or an ability's aim), normalized.</summary>
public interface IDirectedContext : IEffectContext { Vector3 Direction { get; } }

/// <summary>The invocation occurred on a surface, so it has a normal (and therefore a point). Collisions only.</summary>
public interface ISurfaceContext : IPointContext { Vector3 Normal { get; } }

/// <summary>
/// A projectile collision: it has everything — a target GameObject, a contact point + surface normal, and a
/// travel direction. One context type serves all three hit categories (surface/enemy/projectile); the
/// category selects which reaction list runs, not what data is available.
/// </summary>
public sealed class CollisionContext : ITargetContext, ISurfaceContext, IDirectedContext
{
    public GameObject Invoker { get; }
    public GameObject Target { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Normal { get; }
    public Vector3 Direction { get; }

    public CollisionContext(GameObject invoker, GameObject target, int invokerPlayerId, NetworkPlayer invokerPlayer,
        Vector3 point, Vector3 normal, Vector3 direction)
    {
        Invoker = invoker;
        Target = target;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Normal = normal;
        Direction = direction;
    }
}

/// <summary>
/// An aimed ability from the caster: an origin point and an aim direction, but NO target and NO surface
/// normal. So surface-only effects (Ricochet, oriented VFX) authored into an ability read no
/// <see cref="ISurfaceContext"/> and no-op — by construction, not by a zero sentinel.
/// </summary>
public sealed class AimContext : IPointContext, IDirectedContext
{
    public GameObject Invoker { get; }
    public int InvokerPlayerId { get; }
    public NetworkPlayer InvokerPlayer { get; }
    public Vector3 Point { get; }
    public Vector3 Direction { get; }

    public AimContext(GameObject invoker, int invokerPlayerId, NetworkPlayer invokerPlayer, Vector3 point, Vector3 direction)
    {
        Invoker = invoker;
        InvokerPlayerId = invokerPlayerId;
        InvokerPlayer = invokerPlayer;
        Point = point;
        Direction = direction;
    }
}
