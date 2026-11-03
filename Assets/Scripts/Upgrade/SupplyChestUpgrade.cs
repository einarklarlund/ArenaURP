using UnityEngine;

/// <summary>Supply chests the player opens drop more pickups.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Supply Chest")]
public sealed class SupplyChestUpgrade : Upgrade
{
    [Tooltip("Added to the pickup count multiplier per stack: 1 is +100% of the base count.")]
    [SerializeField] private float bonus = 1f;

    // The opener's client, where the pickups spawn.
    public override void ApplyTo(SupplyChest chest) => chest.PickupMultiplier += bonus;
}
