using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Entrypoint the client and server functions
/// </summary>
public sealed class MainMenuView : View
{
    [Header("Controls")]
    [SerializeField] private GameObject controlsParent;
    [SerializeField] private Button hostButton;
    [SerializeField] private TMP_InputField roomCodeInputField;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button settingsButton;

    [Header("Waiting screen")]
    [SerializeField] private GameObject waitScreen;
    [SerializeField] private TMP_Text waitText;

    private void Start()
    {
        hostButton.onClick.AddListener(HandleHostButtonClicked);
        connectButton.onClick.AddListener(HandleConnectButtonClicked);
        settingsButton.onClick.AddListener(HandleSettingsButtonClicked);

        SignalManager.RoomCreatedCallback += HandleRoomCreated;
    }

    private void HandleHostButtonClicked()
    {
        waitText.text = $"Starting room...";
        SetWaitScreenActive(true);
        InstanceFinder.ServerManager.StartConnection();
    }

    private void HandleConnectButtonClicked()
    {
        waitText.text = $"Connecting to room {roomCodeInputField.text}...";
        SetWaitScreenActive(true);
        RoomManager.JoinRoom(roomCodeInputField.text);
    }

    private void HandleSettingsButtonClicked()
    {
        LocalUIEvents.OnSettingsOpened?.Invoke();
    }

    private void HandleRoomCreated(string roomCode)
    {
        SetWaitScreenActive(true);
        waitText.text = $"Created room {roomCode}, connecting...";
    }

    private void SetWaitScreenActive(bool active)
    {
        controlsParent.SetActive(!active);
        waitScreen.SetActive(active);
    }

    public override void Show()
    {
        SetWaitScreenActive(false);
        base.Show();
    }
}
