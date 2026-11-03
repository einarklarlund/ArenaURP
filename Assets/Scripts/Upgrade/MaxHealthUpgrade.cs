using UnityEngine;

/// <summary>More max health, and the same amount of current health so the new life starts full.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Max Health")]
public sealed class MaxHealthUpgrade : Upgrade
{
    [SerializeField] private int amount = 9;

    public override void ApplyTo(Pawn pawn)
    {
        // Health is server-authoritative.
        if (!pawn.IsServerInitialized) return;

        pawn.MaxHealth.Value += amount;
        pawn.CurrentHealth.Value += amount;
    }
}
