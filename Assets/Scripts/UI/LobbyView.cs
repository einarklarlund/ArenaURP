using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// The UI screen displayed before a match begins.
/// Responsibilities:
/// - Displays a list of all connected players and their ready status.
/// - Forwards user input (Ready toggle, username) to the local Player script.
/// </summary>
public sealed class LobbyView : View
{
    [Header("Lobby Controls")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private Toggle readyToggle;

    [Header("Player List")]
    [SerializeField] private PlayerListEntry playerListEntryPrefab;
    [SerializeField] private Transform playerListContainer;

    private NetworkPlayerManager playerManager;
    private readonly Dictionary<NetworkPlayer, PlayerListEntry> entries = new();

    public override void Show()
    {
        readyToggle.isOn = false;
        readyToggle.onValueChanged.AddListener(OnToggleClicked);
        usernameInput.onValueChanged.AddListener(OnUsernameChanged);

        playerManager = FindAnyObjectByType<NetworkPlayerManager>();

        if (playerManager != null)
        {
            playerManager.ActivePlayers.OnChange += HandleActivePlayersChanged;

            // Build initial list from players already present
            foreach (var player in playerManager.ActivePlayers)
            {
                AddEntry(player);
            }
        }

        base.Show();
    }

    public override void Hide()
    {
        readyToggle.onValueChanged.RemoveListener(OnToggleClicked);
        usernameInput.onValueChanged.RemoveListener(OnUsernameChanged);

        if (playerManager != null)
        {
            playerManager.ActivePlayers.OnChange -= HandleActivePlayersChanged;
            playerManager = null;
        }

        ClearAllEntries();

        base.Hide();
    }

    private void HandleActivePlayersChanged(SyncListOperation op, int index,
        NetworkPlayer oldItem, NetworkPlayer newItem, bool asServer)
    {
        switch (op)
        {
            case SyncListOperation.Add:
                AddEntry(newItem);
                break;
            case SyncListOperation.RemoveAt:
                RemoveEntry(oldItem);
                break;
            case SyncListOperation.Clear:
                ClearAllEntries();
                break;
            case SyncListOperation.Complete:
                // Full list sync on late-join; rebuild everything
                RebuildAllEntries();
                break;
        }
    }

    private void AddEntry(NetworkPlayer player)
    {
        if (player == null || entries.ContainsKey(player)) return;

        var entry = Instantiate(playerListEntryPrefab, playerListContainer);
        entry.Bind(player);
        entries[player] = entry;
    }

    private void RemoveEntry(NetworkPlayer player)
    {
        if (player == null || !entries.TryGetValue(player, out var entry)) return;

        entry.Unbind();
        Destroy(entry.gameObject);
        entries.Remove(player);
    }

    private void ClearAllEntries()
    {
        foreach (var entry in entries.Values)
        {
            entry.Unbind();
            Destroy(entry.gameObject);
        }
        entries.Clear();
    }

    private void RebuildAllEntries()
    {
        ClearAllEntries();

        if (playerManager == null) return;
        foreach (var player in playerManager.ActivePlayers)
        {
            AddEntry(player);
        }
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