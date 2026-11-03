using UnityEngine;

/// <summary>Weapon chests the player opens drop more weapons.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Weapon Chest")]
public sealed class WeaponChestUpgrade : Upgrade
{
    [Tooltip("Added to the weapon count multiplier per stack: 1 is +100% of the base count.")]
    [SerializeField] private float bonus = 1f;

    // The server, which spawns the weapons.
    public override void ApplyTo(WeaponChest chest) => chest.WeaponMultiplier += bonus;
}
