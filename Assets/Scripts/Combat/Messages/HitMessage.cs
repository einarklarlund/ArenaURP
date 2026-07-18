using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The ONE fan-out hit context (ADR 0005, the layered model): a pooled, typed ExecuteEvents message the collision
/// site builds and hands to <see cref="Dispatch"/>, which runs it against every <see cref="IHitMessageHandler"/>
/// on the source projectile. Owner-contributed behaviour rides the source too — a passive ADDS its handler onto
/// the owner's projectiles at spawn — so dispatch never has to know about the ability system. The message carries
/// only the two context GameObjects (<see cref="Source"/>, <see cref="Victim"/>), the resolved
/// <see cref="Instigator"/>, and the hit geometry; every COMPONENT a handler needs it looks up itself from Source
/// or Victim (as <c>DamageController</c>/<c>KnockbackController</c>/<c>RedirectionController</c> do),
/// channel-independently — so no component ref can go stale in the pool. The channel guard in <see cref="Functor"/>
/// keeps a handler off a channel it doesn't consume.
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

    private HitMessage() : base(null) { }

    public void Init(HitChannels channel, GameObject source, NetworkPlayer instigator, GameObject victim,
        Vector3 point, Vector3 normal, Vector3 direction, Vector3 sourceCenterPoint)
    {
        Channel = channel;
        Source = source;
        Instigator = instigator;
        Victim = victim;
        Point = point;
        Normal = normal;
        Direction = direction;
        SourceCenterPoint = sourceCenterPoint;
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

    // ---- the atomic hit sequence (ADR 0005) ----

    /// <summary>
    /// Resolve a classified hit ATOMICALLY: (1) spend the source's per-hit durability FIRST — presence of a live
    /// <see cref="HealthController"/> is the durability switch — then (2) dispatch this message to every
    /// <see cref="IHitMessageHandler"/> on the source, in component order, each filtered to the hit's channel by
    /// <see cref="Functor"/>. Every handler runs even if the projectile died on the durability spend, so authored
    /// order can never change a hit's outcome. Owner-contributed handlers are NOT reached here — a passive
    /// contributes by ADDING its handler onto the owner's projectiles at spawn (see SpawnContributionReceiver), so by hit
    /// time they are ordinary handlers on the source. Server-only by construction: every caller is server-side
    /// hit resolution. Any handler may build and dispatch a further HitMessage (an explosion, a ricochet) — that
    /// is an ordinary nested call.
    /// </summary>
    public static void Dispatch(HitMessage hit)
    {
        if (hit.Source == null)
            return;

        // (1) durability spent first — a mid-hit death cannot retract a handler that runs below. Presence of a
        // live HealthController on the source is the durability switch; a source without one is indestructible.
        hit.Source.GetComponent<HealthController>()?.ServerLose(1);

        // (2) every handler on the source, component order, channel-filtered by the functor.
        ExecuteEvents.Execute(hit.Source, hit, Functor);
    }
}
