using System.Collections.Generic;
using Arena.Damage;
using UnityEngine;

/// <summary>
/// The reusable hit-resolution site (ADR 0002): given the DEALING side's components, a struck object, and the
/// hit geometry, fire the hit EXCHANGES from component presence and run the hit REACTIONS. Extracted from
/// ServerCollision so that apply-time target discovery — an explosion overlapping N victims, a cast hitting a
/// pawn — resolves through the SAME pipeline instead of hand-rolling a private copy (both review spikes had to;
/// see the ADR's evidence). Server-only by construction: every caller is server-side hit resolution.
///
/// Reactions are presence-discovered <see cref="IHitHandler"/> components (ADR 0003), dispatched by the
/// per-channel entry points (<see cref="ResolveSurfaceHit"/> / <see cref="ResolvePlayerHit"/> /
/// <see cref="ResolveProjectileHit"/> — the channel is the CALL SITE, not a context subtype), plus the source's
/// per-hit durability spend (<see cref="SpendSelfDurability"/>). The resolver enumerates MECHANISMS, never content:
/// adding a reaction is a new handler component, never an edit here.
/// </summary>
public static class HitResolver
{
    // ---- per-channel entry points (exchanges where the channel has them, then handler dispatch) ----

    /// <summary>Resolve a surface (wall/stage) hit: no exchanges — surfaces carry no receivers — then the source's
    /// per-hit durability spend and the channel-filtered handlers.</summary>
    public static void ResolveSurfaceHit(in HitContext context)
    {
        SpendSelfDurability(in context);
        RunHandlers(in context);
    }

    /// <summary>Resolve a player hit: the damage/knockback/hitstun exchanges first
    /// (<see cref="ResolveExchanges"/>), then the source's durability spend and the channel-filtered handlers —
    /// the same atomic hit.</summary>
    public static void ResolvePlayerHit(
        Damage damage, KnockbackDealer knockback, HitstunDealer hitstun,
        in HitContext context, float damageMultiplier = 1f)
    {
        ResolveExchanges(damage, knockback, hitstun,
            context.Victim, context.Point, context.Direction, context.Instigator, damageMultiplier);
        SpendSelfDurability(in context);
        RunHandlers(in context);
    }

    /// <summary>Resolve a projectile-vs-projectile hit: durability spend then handlers — exchanges deliberately do
    /// not run in this channel (no content wants bullet-destroys-bullet; see ADR 0002).</summary>
    public static void ResolveProjectileHit(in HitContext context)
    {
        SpendSelfDurability(in context);
        RunHandlers(in context);
    }

    /// <summary>
    /// The dissolved LoseHealth (ADR 0003): a projectile carrying a live <see cref="HealthController"/> spends ONE point of
    /// durability per hit — presence of the Health component IS the durability switch, uniform across every channel
    /// (a normal bullet dies on contact; a bullet buffed to MaxHealth &gt; 1 survives that many bounces). A
    /// projectile with no Health is immortal by construction. Death at zero gates only what comes AFTER the hit:
    /// ServerCollision despawns once the atomic hit finishes, so a mid-hit death never retracts an exchange or a
    /// reaction.
    /// </summary>
    private static void SpendSelfDurability(in HitContext context)
    {
        if (context.SelfHealth != null)
            context.SelfHealth.ServerLose(1);
    }

    // ---- handler dispatch ----

    // Scratch buffers for the per-hit harvest (server-only, single-threaded). Dispatch is NOT reentrant: a
    // handler's OnHit must not synchronously resolve another hit (nothing does today — spawned projectiles
    // collide on their own server tick).
    private static readonly List<IHitHandler> _handlers = new();
    private static readonly List<IHitHandler> _componentBuf = new();

    /// <summary>
    /// Harvest the hit handlers present at this hit — the source projectile's own components first, then the
    /// owner's contributions (components authored on the passive instance prefabs under the instigator's pawn)
    /// — and run each whose supported ∩ enabled ∩ current channel is non-empty. Presence IS the wiring: no
    /// serialized handler lists exist anywhere, and pooled instances never get components added at runtime.
    /// </summary>
    private static void RunHandlers(in HitContext context)
    {
        _handlers.Clear();
        if (context.Source != null)
        {
            context.Source.GetComponents(_componentBuf); // GetComponents(List) clears the buffer itself
            _handlers.AddRange(_componentBuf);
        }
        CollectOwnerHandlers(context.Instigator, _handlers);

        for (int i = 0; i < _handlers.Count; i++)
        {
            IHitHandler handler = _handlers[i];
            if ((handler.SupportedChannels & handler.EnabledChannels & context.Channel) != 0)
                handler.OnHit(in context);
        }
    }

    private static void CollectOwnerHandlers(NetworkPlayer owner, List<IHitHandler> results)
    {
        var pawn = owner != null ? owner.ControlledPawn.Value : null;
        if (pawn != null && pawn.TryGetComponent<PawnPassiveAbilities>(out var passives))
            passives.CollectHitHandlers(results);
    }

    /// <summary>
    /// Fire the damage / knockback / hitstun exchanges against <paramref name="victim"/>: each fires iff its
    /// dealer is passed (non-null) AND the victim carries the matching receiver, each end folding only its own
    /// modifiers. <paramref name="damageMultiplier"/> is the per-TARGET fold slot (e.g. blast falloff) — it
    /// scales only damage and is 1 for a plain hit. Receivers are resolved here with one GetComponentInParent
    /// walk up from the struck collider: receivers live on the entity ROOT.
    /// </summary>
    public static void ResolveExchanges(
        Damage damage, KnockbackDealer knockback, HitstunDealer hitstun,
        GameObject victim, Vector3 point, Vector3 direction, NetworkPlayer instigator,
        float damageMultiplier = 1f)
    {
        if (damage != null)
        {
            var damageable = ResolveDamageable(victim);
            if (damageable != null)
                damageable.TakeDamage(new DamageInfo
                {
                    Amount = DamageProcessor.Resolve(damage.Amount, damageMultiplier),
                    Attacker = instigator,
                    HitPoint = point,
                    Direction = direction,
                    Type = DamageType.Bullet,
                });
        }

        if (knockback != null)
        {
            var receiver = ResolveKnockbackReceiver(victim);
            if (receiver != null)
                receiver.ApplyImpulse(knockback.EffectiveImpulse(direction));
        }

        if (hitstun != null)
        {
            var receiver = ResolveHitstunReceiver(victim);
            if (receiver != null)
                receiver.ApplyHitstun(hitstun.EffectiveSeconds);
        }
    }

    // Receiver resolution: one walk UP from the struck collider to the entity root.

    public static IDamageable ResolveDamageable(GameObject hit)
        => hit != null ? hit.GetComponentInParent<IDamageable>() : null;

    public static KnockbackReceiver ResolveKnockbackReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<KnockbackReceiver>() : null;

    public static HitstunReceiver ResolveHitstunReceiver(GameObject hit)
        => hit != null ? hit.GetComponentInParent<HitstunReceiver>() : null;
}
