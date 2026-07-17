using UnityEngine.EventSystems;

/// <summary>
/// A hit responder (ADR 0005, the layered model): any component on the dispatch target — the source projectile,
/// or an owner's passive instance — that reacts to the one fan-out <see cref="HitMessage"/>. Dispatched by
/// <see cref="ServerCollision.DispatchHit"/> through engine-maintained <c>ExecuteEvents</c> machinery: delivery is
/// component order on the GameObject, a DISABLED component is silently skipped (the composability/strip hook),
/// and each handler runs inside ExecuteEvents' per-handler try/catch. Server-only by construction: every dispatch
/// site is server-side hit resolution.
///
/// A handler declares only WHICH CHANNELS IT REACTS ON (<see cref="EnabledHitChannels"/>). What it ACTS ON is
/// semantic to the concretion: each handler resolves the same components from the hit context by lookup regardless
/// of channel (damage acts on the victim's health, a steal on the two rosters, a ricochet on the source's motion),
/// and guards its own preconditions where a channel doesn't populate them.
///
/// Presence is the wiring (component-handler shape, ADR 0003): authored on the projectile prefab, or on a
/// passive-ability instance prefab for the owner-contribution path — never serialized into a list, never
/// AddComponent-ed onto pooled instances. The channel guard (enabled ∩ current channel) lives ONLY in
/// <see cref="HitMessage.Functor"/>; handler bodies are unconditional.
/// </summary>
public interface IHitMessageHandler : IEventSystemHandler
{
    /// <summary>Which channels this handler reacts on — a serialized field on the component, authored per prefab
    /// copy (the LayerMask idiom), where anything varies; a code constant where nothing does.</summary>
    HitChannels EnabledHitChannels { get; }

    void OnHit(HitMessage hit);
}
