using FishNet;
using FishNet.Transporting;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static string CurrentRoom { get; private set; }
    public static RoomManager Instance { get; private set; }

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

        SignalManager.OnRoomCreated -= HandleRoomCreated;
        SignalManager.OnRoomJoined -= HandleRoomJoined;
    }

    public void CreateRoom()
    {
        LocalUIEvents.OnHostInitiated?.Invoke();

        // To properly use SignalManager, the host room must be 
        // created after the host server is started
        InstanceFinder.ServerManager.OnServerConnectionState += HandleServerConnectionState;
        InstanceFinder.ServerManager.StartConnection();
    }

    public void JoinRoom(string roomCode)
    {
        LocalUIEvents.OnJoinInitiated?.Invoke();

        SetCurrentRoom(roomCode);
        InstanceFinder.ClientManager.StartConnection();
        SignalManager.Instance.JoinRoom(roomCode);
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
        // The host joins the room after it's created.
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