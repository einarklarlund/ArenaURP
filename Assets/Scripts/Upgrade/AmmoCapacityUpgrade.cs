using UnityEngine;

/// <summary>Raises how much of every ammo type the player can carry.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Ammo Capacity")]
public sealed class AmmoCapacityUpgrade : Upgrade
{
    [Tooltip("Added to the capacity multiplier per stack: 0.4 is +40% of the base capacity.")]
    [SerializeField] private float bonus = 0.4f;

    public override void ApplyTo(NetworkPlayer player) => player.AddAmmoCapacity(bonus);
}
