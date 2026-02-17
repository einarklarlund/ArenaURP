using FishNet.Managing;
using UnityEngine;

public class ServerBootstrap : MonoBehaviour
{
    private NetworkManager _networkManager;

    private void Awake()
    {
        _networkManager = GetComponent<NetworkManager>();

        // UNITY_SERVER is a built-in Unity symbol active only in Server Builds
        #if UNITY_SERVER
            Debug.Log("Server build detected. Starting Server...");
            Application.targetFrameRate = 60; // Conserve CPU on the Droplet
            _networkManager.ServerManager.StartConnection();
        #else
            Debug.Log("Client build detected. Awaiting manual connection.");
        #endif
    }
}