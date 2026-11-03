using UnityEngine;

/// <summary>One more supply chest, weapon chest and experience capsule around the player at a time.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Extra Chests")]
public sealed class ExtraChestsUpgrade : Upgrade
{
    [Tooltip("Added to each count per stack.")]
    [SerializeField] private int amount = 1;

    public override void ApplyTo(NetworkPlayer player)
    {
        // The directors run on the server only. The player resets their records when its upgrades clear.
        if (!player.IsServerInitialized) return;

        if (ChestDirector.Instance != null)
        {
            ChestDirector.PlayerRecord chests = ChestDirector.Instance.RecordFor(player);
            chests.ExtraSupplyChests += amount;
            chests.ExtraWeaponChests += amount;
        }
        if (ExperienceCapsuleDirector.Instance != null)
            ExperienceCapsuleDirector.Instance.RecordFor(player).ExtraCapsules += amount;
    }
}
