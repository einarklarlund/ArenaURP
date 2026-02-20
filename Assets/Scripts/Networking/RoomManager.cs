using FishNet;
using FishNet.Transporting;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static string CurrentRoom { get; private set; }

    private void Start()
    {
        // To properly use SignalManager, a room must be created once the host server is started
        InstanceFinder.ServerManager.OnServerConnectionState += HandleServerConnectionState;

        // Not sure if this callback is called regardless of whether this client requested the room creation.
        SignalManager.RoomCreatedCallback += HandleRoomCreated;
        SignalManager.JoinRoomCallback += HandleRoomJoined;
    }

    private void OnDestroy()
    {
        InstanceFinder.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
        SignalManager.RoomCreatedCallback -= HandleRoomCreated;
        SignalManager.JoinRoomCallback -= HandleRoomJoined;
    }

    public static void JoinRoom(string roomCode)
    {
        InstanceFinder.ClientManager.StartConnection();
        SignalManager.JoinRoom(roomCode);

        CurrentRoom = roomCode;
    }

    private void HandleServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            SignalManager.CreateRoom();
        }
    }

    private void HandleRoomCreated(string roomCode)
    {
        // The host joins the room after it's created.
        CurrentRoom = roomCode;
        JoinRoom(roomCode);        
    }

    private void HandleRoomJoined(bool succeeded)
    {
        if (!succeeded) CurrentRoom = "";
    }
}