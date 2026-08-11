using FishNet;
using FishNet.Transporting;
using JamesFrowen.SimpleWeb;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static string CurrentRoom { get; private set; }
    public static RoomManager Instance { get; private set; }

    /// <summary>
    /// True between the local peer asking to join a room and that attempt resolving. Only while it
    /// is set does a stopped client mean the join <em>failed</em> rather than the player leaving.
    /// </summary>
    private bool joinInFlight;

    private void Start()
    {
        SignalManager.OnRoomCreated += HandleRoomCreated;
        SignalManager.OnRoomJoined += HandleRoomJoined;
        Instance = this;
    }

    private void OnDestroy()
    {
        if (InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerConnectionState;

        if (InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.OnClientConnectionState -= HandleClientConnectionState;

        SignalManager.OnRoomCreated -= HandleRoomCreated;
        SignalManager.OnRoomJoined -= HandleRoomJoined;
        SignalManager.OnClientStateChanged -= HandleSignalClientState;
    }

    public void CreateRoom()
    {
        LocalUIEvents.OnHostInitiated?.Invoke();

        // SignalManager refuses to create a room until the server is started.
        InstanceFinder.ServerManager.OnServerConnectionState += HandleServerConnectionState;
        InstanceFinder.ServerManager.StartConnection();
    }

    public void JoinRoom(string roomCode)
    {
        LocalUIEvents.OnJoinInitiated?.Invoke();

        joinInFlight = true;

        // Connected and Failed are transport-level transitions, published from here because the
        // CanoeWebRTC assembly cannot see LocalUIEvents.
        InstanceFinder.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
        InstanceFinder.ClientManager.OnClientConnectionState += HandleClientConnectionState;

        // A join that never reaches the signal server has nothing else to end it: the 39.5s signal
        // timeout only starts once an offer arrives.
        SignalManager.OnClientStateChanged -= HandleSignalClientState;
        SignalManager.OnClientStateChanged += HandleSignalClientState;

        SetCurrentRoom(roomCode);
        InstanceFinder.ClientManager.StartConnection();
        SignalManager.Instance.JoinRoom(roomCode);
    }

    /// <summary>Aborts an in-flight join and returns the local peer to the menu (ADR 0012).</summary>
    public void CancelJoin()
    {
        // Cleared first: the stop below is deliberate, not a failure, and must not report one.
        joinInFlight = false;

        var transport = InstanceFinder.TransportManager != null
            ? InstanceFinder.TransportManager.Transport
            : null;

        if (transport != null && transport.GetConnectionState(false) != LocalConnectionState.Stopped)
            InstanceFinder.ClientManager.StopConnection();

        if (InstanceFinder.ServerManager != null)
        {
            // A host cancelling before its server came up would otherwise still create a room.
            InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerConnectionState;

            if (transport != null && transport.GetConnectionState(true) != LocalConnectionState.Stopped)
                InstanceFinder.ServerManager.StopConnection(true);
        }

        SetCurrentRoom("");
        LocalUIEvents.OnJoinCancelled?.Invoke();
    }

    private void HandleClientConnectionState(ClientConnectionStateArgs args)
    {
        if (!joinInFlight)
            return;

        if (args.ConnectionState == LocalConnectionState.Started)
        {
            joinInFlight = false;
            LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Connected);
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            joinInFlight = false;
            LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Failed);
        }
    }

    /// <summary>
    /// The signal socket dropping mid-join kills the attempt: every remaining step of the handshake
    /// is relayed through it. Only <see cref="ClientState.NotConnected"/> is a verdict here -
    /// <c>StartSignalClient</c> announces <c>Connected</c> before it has connected, so that state
    /// says nothing about whether the server is actually reachable.
    /// </summary>
    private void HandleSignalClientState(ClientState state)
    {
        if (!joinInFlight || state != ClientState.NotConnected)
            return;

        joinInFlight = false;
        LocalUIEvents.OnJoinPhaseChanged?.Invoke(JoinPhase.Failed);
    }

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            SignalManager.Instance.CreateRoom();
        }
    }

    private void HandleRoomCreated(string roomCode)
    {
        JoinRoom(roomCode);
    }

    private void HandleRoomJoined(bool succeeded)
    {
        if (!succeeded)
        {
            SetCurrentRoom("");
        }
    }

    private static void SetCurrentRoom(string room)
    {
        CurrentRoom = room;
        NetworkUIEvents.OnCurrentRoomChanged?.Invoke(room);
    }
}