using UnityEngine;

/// <summary>Picking this doesn't use up the pick: the player gets a new offer for it straight away.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Re-roll")]
public sealed class RerollUpgrade : Upgrade
{
    public override void ApplyTo(NetworkPlayer player)
    {
        // Offers are rolled by the server.
        if (player.IsServerInitialized)
            player.ServerGrantPick();
    }
}
