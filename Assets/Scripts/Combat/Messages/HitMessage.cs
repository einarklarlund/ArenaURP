using System.Collections.Generic;
using Arena.NetworkedMotion;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The ONE fan-out hit context (ADR 0005, the layered model): a pooled, typed ExecuteEvents message dispatched at
/// the hit site (<see cref="ServerCollision.DispatchHit"/>) to every <see cref="IHitMessageHandler"/> on the source
/// projectile and the owner's passive instances. Fields mirror the hit the site classified; per-channel population
/// is a documented contract: <see cref="OtherMotion"/> is non-null only on the Projectile channel, and the
/// supported-channels guard in <see cref="Functor"/> is what keeps it non-null where consumed.
///
/// POOLING: server-side hits are per-tick and single-threaded, so instances recycle through a static stack —
/// <see cref="Get"/> at the dispatch site, <see cref="Release"/> immediately after dispatch returns. A nested
/// dispatch (a handler causing a synchronous second hit) pops a SECOND instance, so a live outer message is never
/// aliased; <see cref="Reset"/> nulls every reference so the pool retains nothing.
///
/// The channel guard lives ONLY in <see cref="Functor"/> (enabled ∩ current channel) — handler bodies are
/// unconditional. Deliberately carries no damage-buff fields (outgoing modifier folds) — that is the deferred
/// damage-buff design session's surface (DESIGN.md open question).
/// </summary>
public sealed class HitMessage : BaseEventData
{
    /// <summary>The channel this hit was classified into (exactly one flag set).</summary>
    public HitChannels Channel;
    /// <summary>The dealing entity — the projectile whose hit is being resolved.</summary>
    public GameObject Source;
    /// <summary>The player (human or bot) whose action caused the hit; null when the owner is gone.</summary>
    public NetworkPlayer Instigator;
    /// <summary>The struck object as reported by the sweep — a collider's GameObject; receivers/rosters resolve
    /// from it with one GetComponentInParent walk to the entity root.</summary>
    public GameObject Victim;
    /// <summary>The hit point on the struck surface.</summary>
    public Vector3 Point;
    /// <summary>The struck surface's normal at the hit point.</summary>
    public Vector3 Normal;
    /// <summary>The projectile's travel direction at the hit.</summary>
    public Vector3 Direction;
    /// <summary>This mover's centre at contact — stands off the surface point by the sweep radius along the
    /// normal, so a redirect resumes tangent to the surface instead of embedded in it.</summary>
    public Vector3 SourceCenterPoint;
    /// <summary>The source projectile's motion (velocity, strategy, age).</summary>
    public NetworkedMotion Motion;
    /// <summary>The source projectile's own durability, if it has one — presence is the durability switch.</summary>
    public Health SelfHealth;
    /// <summary>The OTHER projectile's motion — populated only on the Projectile channel.</summary>
    public NetworkedMotion OtherMotion;

    private HitMessage() : base(null) { }

    public void Init(HitChannels channel, GameObject source, NetworkPlayer instigator, GameObject victim,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint,
        NetworkedMotion motion, Health selfHealth, NetworkedMotion otherMotion = null)
    {
        Channel = channel;
        Source = source;
        Instigator = instigator;
        Victim = victim;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
        Motion = motion;
        SelfHealth = selfHealth;
        OtherMotion = otherMotion;
    }

    /// <summary>Null every reference (the pool must retain nothing across hits).</summary>
    public override void Reset()
    {
        base.Reset();
        Channel = HitChannels.None;
        Source = null;
        Instigator = null;
        Victim = null;
        Point = Vector3.zero;
        Normal = Vector3.zero;
        Direction = Vector3.zero;
        SourceCenterPoint = Vector3.zero;
        Motion = null;
        SelfHealth = null;
        OtherMotion = null;
    }

    // ---- pool (server-only, single-threaded — the same discipline the old scratch buffers relied on) ----

    private static readonly Stack<HitMessage> _pool = new();

    /// <summary>Pop a recycled message (or allocate the pool's next). Pair with <see cref="Release"/>.</summary>
    public static HitMessage Get() => _pool.Count > 0 ? _pool.Pop() : new HitMessage();

    /// <summary>Reset and return a message to the pool. Call immediately after dispatch returns; never retain.</summary>
    public static void Release(HitMessage message)
    {
        message.Reset();
        _pool.Push(message);
    }

    // ---- dispatch functor (the ONE home of the channel guard) ----

    /// <summary>
    /// The cached ExecuteEvents functor every hit dispatch uses. The channel guard is baked in here — a handler
    /// runs iff its enabled channels ∩ the message's channel is non-empty — so handler bodies stay unconditional
    /// and no second copy of the guard can drift.
    /// </summary>
    public static readonly ExecuteEvents.EventFunction<IHitMessageHandler> Functor = static (handler, data) =>
    {
        var hit = ExecuteEvents.ValidateEventData<HitMessage>(data);
        if ((handler.EnabledHitChannels & hit.Channel) != 0)
            handler.OnHit(hit);
    };
}
