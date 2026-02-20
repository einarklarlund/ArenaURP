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
    [Header("Lobby Controls")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Toggle readyToggle;

    public override void Show()
    {
        readyToggle.isOn = false;
        readyToggle.onValueChanged.AddListener(OnToggleClicked);
        usernameInput.onValueChanged.AddListener(OnUsernameChanged);

        base.Show();
    }

    public override void Hide()
    {
        readyToggle.onValueChanged.RemoveListener(OnToggleClicked);
        usernameInput.onValueChanged.RemoveListener(OnUsernameChanged);

        base.Hide();
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