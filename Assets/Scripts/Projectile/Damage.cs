using UnityEngine;

/// <summary>
/// Generic Damage component: a thin data holder for how much damage this entity deals, AUTHORED ON THE PREFAB.
/// The <c>DealDamage</c> effect reads <see cref="Amount"/> through the context's damage-source capability
/// (resolving it via <see cref="Arena.Damage.DamageProcessor"/>) and applies it to the target it hit. Damage is
/// server-authoritative and only read server-side, so it needs no networking of its own. (How runtime buffs
/// reach this value — a modifier list here, a context-carried contribution, etc. — is a separate, deliberately
/// deferred design.)
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
