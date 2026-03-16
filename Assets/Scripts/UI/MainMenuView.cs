using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Entrypoint the game
/// </summary>
public sealed class MainMenuView : View
{
    [Header("Controls")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button settingsButton;

    private void Start()
    {
        hostButton.onClick.AddListener(HandleHostButtonClicked);
        connectButton.onClick.AddListener(HandleConnectButtonClicked);
        settingsButton.onClick.AddListener(HandleSettingsButtonClicked);
    }

    private void OnDestroy()
    {
        hostButton.onClick.RemoveListener(HandleHostButtonClicked);
        connectButton.onClick.RemoveListener(HandleConnectButtonClicked);
        settingsButton.onClick.RemoveListener(HandleSettingsButtonClicked);
    }

    private void HandleHostButtonClicked()
    {
        LocalUIEvents.OnHostOptionsOpened?.Invoke();
    }

    private void HandleConnectButtonClicked()
    {
        LocalUIEvents.OnRoomBrowserOpened?.Invoke();
    }

    private void HandleSettingsButtonClicked()
    {
        LocalUIEvents.OnSettingsOpened?.Invoke();
    }
}
