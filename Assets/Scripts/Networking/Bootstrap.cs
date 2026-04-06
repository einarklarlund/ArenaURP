using FishNet.Managing.Client;
using FishNet.Managing.Server;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.CanoeWebRTC;

public class Bootstrap : MonoBehaviour
{
    enum BuildStage
    {
        None,
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
            StartProdBootstrap();
            return;
        }

        #if UNITY_EDITOR
        StartDevBootstrap();
        #endif

        #if !UNITY_EDITOR
        StartProdBootstrap();
        #endif
    }

    #if DEVELOPMENT_BUILD || UNITY_EDITOR
    void StartDevBootstrap()
    {
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
        #endif

        uiManagerObject.SetActive(true);
    }
    #endif

    void StartProdBootstrap()
    {
        Destroy(debugNetworkManagerObject);
        Debug.Log("Start prod bootstrap");
        prodNetworkManagerObject.SetActive(true);
        SceneManager.LoadScene(offlineSceneName);
        uiManagerObject.SetActive(true);
    }
}
