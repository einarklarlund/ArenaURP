using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;

/// <summary>
/// Represents the network identity and persistent data of an individual user.
/// Responsibilities:
/// - Stores player-specific SyncVars like Ready status and the reference to their ControlledPawn.
/// - Exposes player-specific game events, like pawn death.
/// - Handles ServerRpc calls initiated by the user's input (e.g., toggling Ready).
/// </summary>
public sealed class NetworkPlayer : NetworkBehaviour
{
    public static NetworkPlayer LocalInstance { get; private set; }

    public readonly SyncVar<string> Username = new();
    public readonly SyncVar<string> ID = new(Guid.NewGuid().ToString());
    public readonly SyncVar<bool> IsReady = new();
    public readonly SyncVar<Pawn> ControlledPawn = new();
    public readonly SyncVar<double> RespawnTimeEnd = new();
    public readonly SyncVar<int> Score = new();

    public Action<NetworkPlayer> OnDespawn;

    public override void OnStopServer()
    {
        base.OnStopServer();

        OnDespawn?.Invoke(this);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner) return;

        LocalInstance = this;
    }

    [ServerRpc]
    public void ServerSetIsReady(bool value)
    {
        IsReady.Value = value;
    }

    [ServerRpc]
    public void ServerSetUsername(string input)
    {
        Username.Value = input;
    }
}