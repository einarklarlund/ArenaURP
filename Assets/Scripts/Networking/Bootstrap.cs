using FishNet.Managing.Client;
using FishNet.Managing.Server;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.CanoeWebRTC;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Boot-scene entry point. Selects between Local / Dev / Prod flows and brings the
/// FishNet NetworkManager online.
///
/// **Scene-loading invariant.** FishNet's <c>NetworkObject.Start</c> auto-deactivates
/// its GameObject if no NetworkManager has started server/client at scene-load time
/// (see <c>NetworkObject.TryStartDeactivation</c>). Deactivated scene NetworkObjects
/// don't recover automatically — they silently stay off, and OnStartServer never
/// fires. Any scene loaded from here MUST therefore have zero scene NetworkObjects.
/// Gameplay scenes (e.g. Online.unity, which has 7 NOs) load later from the menu
/// flow after the user clicks Host — at which point the server is started and the
/// invariant is satisfied. Offline.unity is intentionally empty of NOs for this
/// reason; if you add one, you must also reorder so the host is started first.
/// </summary>
public class Bootstrap : MonoBehaviour
{
    enum BuildStage
    {
        None,
        Local,
        Dev,
        Prod,
    }

    [SerializeField] private GameObject uiManagerObject;
    [SerializeField] private GameObject prodNetworkManagerObject;
    [SerializeField] private GameObject debugNetworkManagerObject;
    [SerializeField] private RoomManager roomManager;
    [SerializeField] private string offlineSceneName;
    [SerializeField] private string debugSceneName;
    [SerializeField] private BuildStage EnforceBuildStage = BuildStage.None;

    void Start()
    {
        uiManagerObject.SetActive(false);

        if (EnforceBuildStage == BuildStage.Prod)
        {
            StartProdBootstrap();
            return;
        }
        if (EnforceBuildStage == BuildStage.Dev)
        {
            StartDevBootstrap();
            return;
        }
        if(EnforceBuildStage == BuildStage.Local)
        {
            StartLocalBootstrap();
            return;
        }

        #if UNITY_EDITOR
        StartDevBootstrap();
        #endif

        #if !UNITY_EDITOR
        StartProdBootstrap();
        #endif
    }
    void StartDevBootstrap()
    {
        #if DEVELOPMENT_BUILD || UNITY_EDITOR

        Destroy(prodNetworkManagerObject);
        Debug.Log("Start dev bootstrap");

        #if UNITY_WEBGL && !UNITY_EDITOR
        var tugboat = debugNetworkManagerObject.GetComponent<Tugboat>();
        DestroyImmediate(tugboat);
        debugNetworkManagerObject.AddComponent<CanoeWebRTC>();
        debugNetworkManagerObject.SetActive(true);
        roomManager.CreateRoom();
        WebGLInput.captureAllKeyboardInput = false;
        #endif
        
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (IsPortInUse(7770))
        {
            Debug.Log("Port 7770 already in use; starting client-only connection.");
            debugNetworkManagerObject.SetActive(true);
            debugNetworkManagerObject.GetComponent<ClientManager>().StartConnection();
        }
        else
        {
            var serverManager = debugNetworkManagerObject.GetComponent<ServerManager>();
            serverManager.OnServerConnectionState += (args) =>
            {
                if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
                {
                    debugNetworkManagerObject
                        .GetComponent<ClientManager>()
                        .StartConnection();
                }
            };
            debugNetworkManagerObject.SetActive(true);
            serverManager.StartConnection();
        }
        #endif

        uiManagerObject.SetActive(true);

        #endif
    }

    void StartLocalBootstrap()
    {
        Destroy(prodNetworkManagerObject);
        var tugboat = debugNetworkManagerObject.GetComponent<Tugboat>();
        DestroyImmediate(tugboat);
        debugNetworkManagerObject.AddComponent<CanoeWebRTC>();
        debugNetworkManagerObject.SetActive(true);
        // Per scene-loading invariant on this class: offlineSceneName must have zero
        // scene NetworkObjects, since the server is not started here.
        SceneManager.LoadScene(offlineSceneName);
        uiManagerObject.SetActive(true);
    }

    void StartProdBootstrap()
    {
        Destroy(debugNetworkManagerObject);
        Debug.Log("Start prod bootstrap");
        prodNetworkManagerObject.SetActive(true);
        // Per scene-loading invariant on this class: offlineSceneName must have zero
        // scene NetworkObjects, since the server is not started here.
        SceneManager.LoadScene(offlineSceneName);
        uiManagerObject.SetActive(true);
    }

    static bool IsPortInUse(int port)
    {
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }
}
