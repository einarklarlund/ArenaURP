using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure, stateless functional core of the hitstun system. Zero Unity lifecycle dependencies.
///
/// A hitstun exchange folds TWICE, once per end: the dealer calls <see cref="ResolveDuration"/> (its authored
/// config + its outgoing modifiers), the receiver calls <see cref="ResolveIncoming"/> (that duration + its own
/// stun-resistance modifiers).
///
/// Stun is stored as an EXPIRY TICK rather than a countdown timer. A tick is a value both peers agree on, so the
/// server can publish one number through a ServerOnly SyncVar and the server (which simulates bots) and the
/// owning client (which simulates its own weapon) each evaluate <see cref="IsStunned"/> against their own
/// <c>TimeManager.Tick</c> and reach the same answer. A countdown float would have to be ticked down somewhere,
/// and "somewhere" differs between a bot pawn and a human pawn.
/// </summary>
public static class HitstunProcessor
{
    /// <summary>
    /// Folds a modifier list into a single multiplier. An empty or null list yields the identity (1), so an
    /// absent contributor changes nothing.
    /// </summary>
    public static float FoldMultiplier(IReadOnlyList<HitstunModifier> modifiers)
    {
        float multiplier = 1f;
        if (modifiers == null)
            return multiplier;

        for (int i = 0; i < modifiers.Count; i++)
            multiplier *= modifiers[i].DurationMultiplier;

        return multiplier;
    }

    /// <summary>
    /// The OUTGOING stun a dealer emits: its authored seconds scaled by the dealer's own modifiers, floored at
    /// zero. Never mutates the authored config.
    /// </summary>
    public static float ResolveDuration(in HitstunConfig config, IReadOnlyList<HitstunModifier> modifiers)
        => Mathf.Max(0f, config.Seconds * FoldMultiplier(modifiers));

    /// <summary>
    /// The INCOMING stun a receiver accepts: the dealt seconds scaled by the receiver's own resistance
    /// modifiers, floored at zero. A multiplier of 0 makes the receiver stun-immune.
    /// </summary>
    public static float ResolveIncoming(float seconds, IReadOnlyList<HitstunModifier> modifiers)
        => Mathf.Max(0f, seconds * FoldMultiplier(modifiers));

    /// <summary>
    /// The tick a stun of <paramref name="seconds"/> starting now expires on. Rounds UP, so any positive duration
    /// costs at least one full tick and can never round away to "not stunned". A non-positive duration or an
    /// invalid tick delta yields <paramref name="currentTick"/> — i.e. already expired.
    /// </summary>
    public static uint EndTick(uint currentTick, float seconds, float tickDelta)
    {
        if (seconds <= 0f || tickDelta <= 0f)
            return currentTick;

        int ticks = Mathf.Max(1, Mathf.CeilToInt(seconds / tickDelta));
        return currentTick + (uint)ticks;
    }

    /// <summary>Whether a stun expiring on <paramref name="stunnedUntilTick"/> is still in effect.</summary>
    public static bool IsStunned(uint currentTick, uint stunnedUntilTick) => currentTick < stunnedUntilTick;

    /// <summary>
    /// Stacking rule: a new stun EXTENDS an existing one but never cuts it short, so a weak late hit cannot
    /// rescue a player from a strong early one. Returns whichever expiry is later.
    /// </summary>
    public static uint ExtendTo(uint currentEndTick, uint newEndTick)
        => newEndTick > currentEndTick ? newEndTick : currentEndTick;
}
