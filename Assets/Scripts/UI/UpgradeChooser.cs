using TMPro;
using UnityEngine;

/// <summary>
/// Picking upgrades before a spawn: how many are left and how long until the rest are picked at random,
/// above the current offer (an UpgradeOfferPanel). Shared by DeathView and FirstSpawnView.
/// </summary>
public sealed class UpgradeChooser : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;

    private void Update()
    {
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        double endTime = player != null ? player.RespawnTimeEnd.Value : -1;

        // The end time is on the server's tick. A client's Tick is its estimate of that; its default
        // LocalTick counts from when it connected, which would add the time connected to the countdown.
        double remaining = endTime - FishNet.InstanceFinder.TimeManager.TicksToTime(FishNet.Managing.Timing.TickType.Tick);
        if (endTime <= 0 || remaining <= 0)
        {
            timerText.text = "SPAWNING…";
            return;
        }

        int picks = player.UpgradePicksPending.Value;
        string upgrades = picks == 1 ? "UPGRADE" : "UPGRADES";
        timerText.text = $"PICK {picks} {upgrades}  ·  AUTO IN {remaining:F0}s";
    }
}
