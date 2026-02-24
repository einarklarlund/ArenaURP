using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseView : View
{
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button unpauseButton;

    public override void Show()
    {
        settingsButton.onClick.AddListener(HandleSettingsClicked);
        unpauseButton.onClick.AddListener(HandleUnpauseButtonClicked);

        base.Show();
    }

    public override void Hide()
    {
        settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        unpauseButton.onClick.RemoveListener(HandleUnpauseButtonClicked);

        base.Hide();
    }

    private void HandleSettingsClicked()
    {
        LocalUIEvents.OnSettingsOpened?.Invoke();
    }

    private void HandleUnpauseButtonClicked()
    {
        LocalUIEvents.OnUnpause?.Invoke();
    }
}