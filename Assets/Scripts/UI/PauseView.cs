using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PauseView : View
{
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button unpauseButton;
    [SerializeField] private SignalStatusDisplay signalStatusDisplay;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    [SerializeField] private Button debugButton;
#endif

    public override void Show()
    {
        settingsButton.onClick.AddListener(HandleSettingsClicked);
        unpauseButton.onClick.AddListener(HandleUnpauseButtonClicked);
        signalStatusDisplay.Initialize(SignalManager.Instance.ClientState);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        debugButton.gameObject.SetActive(true);
        debugButton.onClick.AddListener(HandleDebugClicked);
#endif

        base.Show();
    }

    public override void Hide()
    {
        settingsButton.onClick.RemoveListener(HandleSettingsClicked);
        unpauseButton.onClick.RemoveListener(HandleUnpauseButtonClicked);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        debugButton.onClick.RemoveListener(HandleDebugClicked);
#endif

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

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private void HandleDebugClicked()
    {
        LocalUIEvents.OnDebugViewOpened?.Invoke();
    }
#endif
}