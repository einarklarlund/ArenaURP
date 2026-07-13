using UnityEngine;

/// <summary>
/// Generic Damage component: a thin data holder for how much damage this entity deals, AUTHORED ON THE PREFAB.
/// Carrying it IS what makes this entity's hits damaging: ServerCollision fires the damage exchange from
/// component presence (this dealer ∧ the victim's <see cref="IDamageable"/>; ADR 0002), resolving
/// <see cref="Amount"/> via <see cref="Arena.Damage.DamageProcessor"/>. Damage is server-authoritative and only
/// read server-side, so it needs no networking of its own. (How runtime buffs reach this value — a modifier
/// list here, a context-carried contribution, etc. — is a separate, deliberately deferred design.)
/// </summary>
public sealed class Damage : MonoBehaviour
{
    [SerializeField] private int _amount = 1;

    /// <summary>Base damage dealt; authored on the prefab, mutable at runtime for buffs/modifiers.</summary>
    public int Amount
    {
        get => _amount;
        set => _amount = value;
    }
}
