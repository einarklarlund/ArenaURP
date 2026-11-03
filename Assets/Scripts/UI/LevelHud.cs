using TMPro;
using UnityEngine;

/// <summary>
/// The level display and the "Level up!" panel at the bottom of the screen. Lives under MainView, so it
/// shows only while the local player is alive.
/// </summary>
public sealed class LevelHud : MonoBehaviour
{
    [SerializeField] private TMP_Text levelText;
    [Tooltip("\"Level up!\" plus the offer. Shown while there is an upgrade to pick.")]
    [SerializeField] private GameObject levelUpPanel;

    private void OnEnable()
    {
        NetworkUIEvents.OnLocalExperienceChanged += RefreshLevel;
        NetworkUIEvents.OnLocalUpgradeOfferChanged += RefreshPanel;

        NetworkPlayer player = NetworkPlayer.LocalInstance;
        RefreshLevel(player != null ? player.Experience : 0);
        RefreshPanel();
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnLocalExperienceChanged -= RefreshLevel;
        NetworkUIEvents.OnLocalUpgradeOfferChanged -= RefreshPanel;
    }

    private void RefreshLevel(int experience)
    {
        int level = Levels.ForExperience(experience);
        levelText.text = $"LV {level}  {experience}/{Levels.Threshold(level + 1)}";
    }

    private void RefreshPanel()
    {
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        bool picking = player != null && player.UpgradePicksPending.Value > 0 && player.UpgradeOffer.Value.x >= 0;
        levelUpPanel.SetActive(picking);
    }
}
