using UnityEngine;

/// <summary>
/// Generic Damage component: a thin data holder for how much damage this entity deals. Seeded from config
/// on spawn and left runtime-mutable so the modifier / level-up system can buff it (bonus damage, crits)
/// without the hit effects knowing the arithmetic. The <c>DealDamage</c> effect reads <see cref="Amount"/>
/// (resolving it through <see cref="Arena.Damage.DamageProcessor"/>) and applies it to the target it hit.
/// Damage is server-authoritative and only read server-side, so it needs no networking of its own.
/// </summary>
public sealed class Damage : MonoBehaviour
{
    [SerializeField] private int _amount = 1;

    /// <summary>Base damage dealt; seeded from the type on spawn, mutable at runtime for buffs/modifiers.</summary>
    public int Amount
    {
        get => _amount;
        set => _amount = value;
    }
}
