using UnityEngine;

/// <summary>
/// A power-up a player picks on level-up or before spawning. It buffs the things its player has:
/// the player itself, their pawn, the pawn's weapons and the projectiles those fire.
///
/// A player applies its upgrades down that chain when each object becomes theirs (see
/// <see cref="NetworkPlayer.ApplyUpgradesTo(Pawn)"/>), once per stack, on every peer. Each hook applies
/// ONE stack on top of whatever is already there, so stacking is just applying again. A hook changes
/// its target through the target's own API, and only on the peer that owns that state (the server for
/// synced health, the owner for client-authoritative ammo, and so on).
/// </summary>
public abstract class Upgrade : ScriptableObject
{
    [SerializeField] private string displayName;
    [TextArea]
    [SerializeField] private string description;
    [Tooltip("How many times a player can pick this in one life. 0 for no limit.")]
    [SerializeField] private int maxStacks;
    [Tooltip("How likely this is to be offered, relative to the other upgrades.")]
    [SerializeField] private float weight = 1f;

    public string DisplayName => displayName;
    public string Description => description;
    public int MaxStacks => maxStacks;
    public float Weight => weight;

    /// <summary>Called once per stack when the upgrade is picked.</summary>
    public virtual void ApplyTo(NetworkPlayer player) { }

    /// <summary>Called once per stack when the player gets a pawn, and on the live pawn when picked.</summary>
    public virtual void ApplyTo(Pawn pawn) { }

    /// <summary>Called once per stack when a pawn of the player equips a weapon, and on held weapons when picked.</summary>
    public virtual void ApplyTo(Weapon weapon) { }

    /// <summary>Called once per stack when a projectile of the player comes alive.</summary>
    public virtual void ApplyTo(Projectile projectile) { }

    /// <summary>Called once per stack on the opener's client when the player opens a supply chest.</summary>
    public virtual void ApplyTo(SupplyChest chest) { }

    /// <summary>Called once per stack on the server when the player opens a weapon chest.</summary>
    public virtual void ApplyTo(WeaponChest chest) { }

    /// <summary>Called once per stack on every client when a chest spawned for the player comes alive there.</summary>
    public virtual void ApplyTo(ChestTimer chest) { }
}
