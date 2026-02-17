using FishNet.Object;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The UI screen displayed before a match begins.
/// Responsibilities:
/// - Displays the ready status of the local player.
/// - Forwards user input (Ready toggle) to the Player script.
/// - Updates visual elements based on whether the match is ready to start.
/// </summary>
public sealed class LobbyView : View
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Toggle readyToggle;

    private void OnEnable()
    {
        readyToggle.isOn = false;
        readyToggle.onValueChanged.AddListener(OnToggleClicked);
        usernameInput.onValueChanged.AddListener(OnUsernameChanged);
    }

    private void OnDisable()
    {
        readyToggle.onValueChanged.RemoveListener(OnToggleClicked);
    }

    private void OnUsernameChanged(string input)
    {
        NetworkPlayer.LocalInstance.ServerSetUsername(input);
    }

    private void OnToggleClicked(bool val)
    {
        NetworkPlayer.LocalInstance.ServerSetIsReady(val);
    }
}