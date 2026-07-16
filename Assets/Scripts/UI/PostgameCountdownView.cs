

using TMPro;
using UnityEngine;

public class PostgameCountdownView : View
{
    [SerializeField] private TMP_Text winnerUsernameText;
    [SerializeField] private TextMeshProUGUI countdownText;

    private void OnEnable()
    {
        NetworkUIEvents.OnCountdownChanged += HandleCountdown;
        NetworkUIEvents.OnLeadPlayerChanged += HandleLeadPlayer;
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnCountdownChanged -= HandleCountdown;
        NetworkUIEvents.OnLeadPlayerChanged -= HandleLeadPlayer;
    }

    private void HandleCountdown(int time)
    {
        countdownText.text = time.ToString();
    }

    private void HandleLeadPlayer(NetworkPlayer lead)
    {
        winnerUsernameText.text = lead != null ? lead.Username.Value : "";
    }
}
