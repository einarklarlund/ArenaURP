using UnityEngine;

/// <summary>
/// The damage controller of an entity (ADR 0005): the ONE home of the outgoing-damage datum, owning both the
/// authored amount and the dealer half of the damage exchange. A controller OWNS STATE (<see cref="Amount"/>) and
/// exposes an API to modify it (the <see cref="Amount"/> setter — the seam a runtime buff writes through) — the
/// state-owning half of the dispatch model. Carrying it IS what makes this entity's hits damaging.
///
/// DEALER half (a layer-2 hit responder): on a Player-channel <see cref="HitMessage"/> it resolves the struck
/// object's <see cref="IDamageable"/> — the same GetComponentInParent walk to the entity root every receiver uses
/// — and applies <see cref="Amount"/> (clamped non-negative; the arithmetic that a per-target damage buff will
/// grow into lives here now, not in a separate core). Damage is server-authoritative and only read server-side,
/// so it needs no networking of its own.
/// </summary>
public sealed class DamageController : MonoBehaviour, IHitMessageHandler
{
    [SerializeField] private int _amount = 1;

    [Tooltip("Which hit channels this KIND damages on — a struck IDamageable lives on the Player channel.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    /// <summary>Base damage dealt; authored on the prefab, mutable at runtime for buffs/modifiers.</summary>
    public int Amount
    {
        get => _amount;
        set => _amount = value;
    }

    public HitChannels EnabledHitChannels => enabledChannels;

    public void OnHit(HitMessage hit)
    {
        var damageable = hit.Victim != null ? hit.Victim.GetComponentInParent<IDamageable>() : null;
        if (damageable == null)
            return;   // nothing damageable at the hit site (a non-Player channel self-guards here)

        damageable.TakeDamage(new DamageInfo
        {
            Amount = Mathf.Max(0, _amount),
            Attacker = hit.Instigator,
            HitPoint = hit.Point,
            Direction = hit.Direction,
            Type = DamageType.Bullet,
        });
    }
}
