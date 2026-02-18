using FishNet;
using UnityEngine;

public class RoomManager : MonoBehaviour
{

    private void Start()
    {
        // preliminary host behaviour:
        // auto-join a room whenever one is created
        SignalManager.RoomCreatedCallback += (roomCode) =>
        {
            JoinRoom(roomCode);
        };

        // host behaviour: create a room once server is started
        InstanceFinder.ServerManager.OnServerConnectionState += (e) =>
        {
            if (e.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                SignalManager.CreateRoom();
            }
        };
    }
    
    public void HostGame()
    {
        InstanceFinder.ServerManager.StartConnection();
    }

    public static void JoinRoom(string roomCode)
    {
        InstanceFinder.ClientManager.StartConnection();

        SignalManager.JoinRoom(roomCode);
    }
}