using UnityEngine;

/// <summary>The beam that marks a new chest for its player lasts longer.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Chest Beacon")]
public sealed class ChestBeaconUpgrade : Upgrade
{
    [Tooltip("Added to the beam duration multiplier per stack: 1 is +100% of the base duration.")]
    [SerializeField] private float bonus = 1f;

    // Every client: each draws the beam for its own player only.
    public override void ApplyTo(ChestTimer chest) => chest.BeamDurationMultiplier += bonus;
}
