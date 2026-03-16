using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI component for a single row in the lobby player list.
/// Displays a player's username and ready status.
/// Attach this to a prefab containing a TMP_Text and an Image (or similar indicator).
/// </summary>
public sealed class PlayerListEntry : MonoBehaviour
{
    [SerializeField] private TMP_Text usernameLabel;
    [SerializeField] private Toggle readyIndicator;

    private NetworkPlayer trackedPlayer;

    private void Start()
    {
        readyIndicator.interactable = false;
    }

    /// <summary>
    /// Binds this entry to a NetworkPlayer, subscribing to their SyncVar changes.
    /// </summary>
    public void Bind(NetworkPlayer player)
    {
        trackedPlayer = player;

        player.Username.OnChange += HandleUsernameChanged;
        player.IsReady.OnChange += HandleReadyChanged;

        // Initialize with current values
        UpdateUsername(player.Username.Value);
        UpdateReadyIndicator(player.IsReady.Value);
    }

    /// <summary>
    /// Unsubscribes from SyncVar callbacks. Must be called before destroying this entry.
    /// </summary>
    public void Unbind()
    {
        if (trackedPlayer == null) return;

        trackedPlayer.Username.OnChange -= HandleUsernameChanged;
        trackedPlayer.IsReady.OnChange -= HandleReadyChanged;

        trackedPlayer = null;
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void HandleUsernameChanged(string prev, string next, bool asServer)
    {
        UpdateUsername(next);
    }

    private void HandleReadyChanged(bool prev, bool next, bool asServer)
    {
        UpdateReadyIndicator(next);
    }

    private void UpdateUsername(string username)
    {
        usernameLabel.text = string.IsNullOrEmpty(username) ? "Player" : username;
    }

    private void UpdateReadyIndicator(bool isReady)
    {
        readyIndicator.isOn = isReady;
    }
}