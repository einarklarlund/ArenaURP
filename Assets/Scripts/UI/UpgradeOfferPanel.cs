using TMPro;
using UnityEngine;

/// <summary>
/// The local player's current upgrade offer: "Press 1 for: …" / "Press 2 for: …". Picks with the
/// UpgradeChoice1/2 actions while it is showing. Used by the level-up panel and the pick screen (DeathView).
/// </summary>
public sealed class UpgradeOfferPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text choice1Text;
    [SerializeField] private TMP_Text choice2Text;
    [Tooltip("\"+N more\" when more picks are queued behind this one. Optional.")]
    [SerializeField] private TMP_Text moreText;

    private void OnEnable()
    {
        NetworkUIEvents.OnLocalUpgradeOfferChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnLocalUpgradeOfferChanged -= Refresh;
    }

    private void Update()
    {
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        if (player == null || !InputManager.HasInstance) return;
        if (player.UpgradePicksPending.Value <= 0) return;

        Vector2Int offer = player.UpgradeOffer.Value;
        if (offer.x >= 0 && InputManager.ProgressionControls.UpgradeChoice1.WasPressedThisFrame())
            player.PickUpgrade(0);
        else if (offer.y >= 0 && InputManager.ProgressionControls.UpgradeChoice2.WasPressedThisFrame())
            player.PickUpgrade(1);
    }

    private void Refresh()
    {
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        if (player == null) return;

        Vector2Int offer = player.UpgradeOffer.Value;
        choice1Text.text = Describe(player, 1, offer.x);
        choice2Text.text = Describe(player, 2, offer.y);

        int more = player.UpgradePicksPending.Value - 1;
        if (moreText != null)
            moreText.text = more > 0 ? $"+{more} more" : "";
    }

    private static string Describe(NetworkPlayer player, int key, int id)
    {
        if (id < 0) return "";

        Upgrade upgrade = player.UpgradeCatalogue[id];
        return $"Press {key} for: <b>{upgrade.DisplayName}</b>\n<size=75%>{upgrade.Description}</size>";
    }
}
