using UnityEngine;

/// <summary>Faster movement, on the ground and in the air.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Move Speed")]
public sealed class MoveSpeedUpgrade : Upgrade
{
    [Tooltip("Added to the move speed multiplier per stack: 0.125 is +12.5% of the base speed.")]
    [SerializeField] private float bonus = 0.125f;

    // Every peer. Only the owner reads it: movement is owner-simulated and carried to everyone else by a
    // client-authoritative NetworkTransform, so the server has no simulation for it to disagree with.
    public override void ApplyTo(Pawn pawn) => pawn.MoveSpeedMultiplier += bonus;
}
