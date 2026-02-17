using UnityEngine;
using TMPro;

/// <summary>
/// A specialized overlay used during the transition from Lobby to Match.
/// Responsibilities:
/// - Listens to the NetworkUIEvents bus for the remaining countdown time.
/// - Provides a high-visibility visual ticker (3... 2... 1...) to signal match start.
/// - Automatically yields to the MainView once the match begins.
/// </summary>
public sealed class PregameCountdownView : View
{
    [SerializeField] private TextMeshProUGUI countdownText;

    private void OnEnable()
    {
        countdownText.text = "" + MatchFlowManager.Instance.CountdownTime.Value;
        NetworkUIEvents.OnCountdownChanged += HandleCountdown;
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnCountdownChanged -= HandleCountdown;
    }

    private void HandleCountdown(int time)
    {
        countdownText.text = time.ToString();
    }
}