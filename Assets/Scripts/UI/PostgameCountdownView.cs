

using TMPro;
using UnityEngine;

public class PostgameCountdownView : View
{
    [SerializeField] private TMP_Text winnerUsernameText;
    [SerializeField] private TextMeshProUGUI countdownText;

    private void OnEnable()
    {
        countdownText.text = "" + MatchFlowManager.Instance.CountdownTime.Value;
        NetworkUIEvents.OnCountdownChanged += HandleCountdown;
        winnerUsernameText.text = DeathmatchManager.Instance.leadPlayer.Value.Username.Value;
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