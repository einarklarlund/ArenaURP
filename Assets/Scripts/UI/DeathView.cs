using UnityEngine;
using TMPro;

public sealed class DeathView : View
{
    [SerializeField] private TextMeshProUGUI timerText;
    private double _targetServerTime = -1;

    private void OnEnable()
    {
        NetworkUIEvents.OnRespawnTimerStarted += BeginCountdown;
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnRespawnTimerStarted -= BeginCountdown;
    }

    private void BeginCountdown(double endTime)
    {
        _targetServerTime = endTime;
    }

    private void Update()
    {
        if (_targetServerTime < 0) return;

        // FishNet's TimeManager gives us the current synchronized server time
        double remaining = _targetServerTime - FishNet.InstanceFinder.TimeManager.TicksToTime();

        if (remaining > 0)
        {
            timerText.text = $"RESPAWNING IN: {remaining:F1}s";
        }
        else
        {
            timerText.text = "READY";
            _targetServerTime = -1; 
        }
    }
}