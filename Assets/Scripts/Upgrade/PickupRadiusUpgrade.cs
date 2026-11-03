using UnityEngine;

/// <summary>Pickups home in on the pawn from further away.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Pickup Radius")]
public sealed class PickupRadiusUpgrade : Upgrade
{
    [Tooltip("Added to the pickup radius multiplier per stack: 1 doubles the base radius.")]
    [SerializeField] private float bonus = 1f;

    // Every peer: each one claims its own local pickups, reading every nearby pawn's radius.
    public override void ApplyTo(Pawn pawn) => pawn.PickupRadiusMultiplier += bonus;
}
