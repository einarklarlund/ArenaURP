using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseView : View
{
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button unpauseButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private SignalStatusDisplay signalStatusDisplay;

    [SerializeField] private Button debugButton;

    public override void Show()
    {
        settingsButton.onClick.AddListener(HandleSettingsClicked);
        unpauseButton.onClick.AddListener(HandleUnpauseButtonClicked);
        disconnectButton.onClick.AddListener(HandleDisconnectClicked);
        signalStatusDisplay.Initialize(SignalManager.Instance.ClientState);

        debugButton.gameObject.SetActive(Debug.isDebugBuild);
        if (Debug.isDebugBuild) debugButton.onClick.AddListener(HandleDebugClicked);

        base.Show();
    }

    public override void Hide()
    {
        settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        unpauseButton.onClick.RemoveListener(HandleUnpauseButtonClicked);
        disconnectButton.onClick.RemoveListener(HandleDisconnectClicked);

        debugButton.onClick.RemoveListener(HandleDebugClicked);

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

    private void HandleDisconnectClicked()
    {
        RoomManager.Instance.Leave();
    }

    private void HandleDebugClicked()
    {
        LocalUIEvents.OnDebugViewOpened?.Invoke();
    }
}