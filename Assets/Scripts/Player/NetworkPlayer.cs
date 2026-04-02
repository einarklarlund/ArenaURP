using System;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;

/// <summary>
/// Represents the network identity and persistent data of an individual participant
/// in a match. Used for both human players and bots.
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

    [NonSerialized] public bool IsBot;

    public Action<NetworkPlayer> OnDespawn;

    public override void OnStopServer()
    {
        base.OnStopServer();

        OnDespawn?.Invoke(this);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (IsBot)
            GiveOwnership(InstanceFinder.ClientManager.Connection);

        if (!IsOwner || IsBot) return;

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
