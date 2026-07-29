using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting;
using FishNet.Transporting.CanoeWebRTC;
using FishNet.Transporting.Tugboat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Arena.PlayTests
{
    /// <summary>
    /// The whole WebRTC join, end to end, against a REAL signal server the fixture launches and
    /// reaps: a peer creates a room, a client joins it, and both peers must reach
    /// data-channels-open. Nothing here is mocked — the production <c>SignalManager</c> drives the
    /// production <c>CanoeWebRTC</c> transport over a real websocket and real ICE, exactly as
    /// <c>RoomManager</c> does in game (the host joins its own room, so one process carries both
    /// peers of the handshake).
    ///
    /// **Channels-open is the assertion; duration is only logged.** Real ICE is asynchronous and a
    /// known flakiness source, so the timeout is deliberately generous and nothing asserts on time.
    /// The logged baseline exists to be beaten: the bundled-candidate path it was written against
    /// sat behind two serial 4s gather stalls, giving an ~8.05s floor. Grep the run log for
    /// <c>BASELINE join duration</c>.
    ///
    /// Values on the transport mirror Boot.unity's authored ones, so the baseline measures what
    /// players actually experience rather than the (much shorter) code defaults.
    /// </summary>
    public class WebRtcJoinTests
    {
        const string TestNetworkManagerPath = "Assets/Tests/PlayMode/TestNetworkManager.prefab";
        const string BlankScenePath = "Assets/Tests/PlayMode/Scenes/TestSmoke.unity";

        const float SignalServerReadyTimeoutSeconds = 30f;
        const float SignalSocketTimeoutSeconds = 15f;
        const float ServerStartTimeoutSeconds = 15f;
        const float RoomCreatedTimeoutSeconds = 15f;
        // Generous on purpose: see the class summary. Below the transport's own 80s signal timeout,
        // so a stalled handshake surfaces here as a clear assertion rather than as transport teardown.
        const float JoinTimeoutSeconds = 60f;

        // Boot.unity's authored CanoeWebRTC values.
        const int BootMtu = 2047;
        const int BootOperationTimeoutMs = 25000;
        const int BootSignalTimeoutMs = 80000;

        static readonly string[] BootIceServerUrls =
        {
            "stun:stun.l.google.com:19302",
            "stun:stun2.l.google.com:19302",
            "stun:stun3.l.google.com:19302",
            "stun:stun4.l.google.com:19302",
            "stun:stun.stunprotocol.org:3478",
        };

        SignalServerProcess _signalServer;
        NetworkManager _networkManager;
        GameObject _signalManagerObject;
        string _roomCode;
        bool _hostReachedChannelsOpen;
        bool _clientReachedChannelsOpen;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return LaunchSignalServer();
            yield return StartHost();
            yield return CreateRoom();
#else
            Assert.Fail("The WebRTC join harness requires the Unity Editor.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator Joining_a_room_opens_both_peers_data_channels()
        {
#if UNITY_EDITOR
            // The production join order (RoomManager.JoinRoom): bring the transport client to
            // Starting first, so the offer that follows has somewhere to land.
            var join = Stopwatch.StartNew();
            _networkManager.ClientManager.StartConnection();
            InvokeOnSignalManager("JoinRoom", _roomCode);

            yield return WaitUntil(
                () => _hostReachedChannelsOpen && _clientReachedChannelsOpen, JoinTimeoutSeconds);
            join.Stop();

            Assert.IsTrue(_clientReachedChannelsOpen,
                $"the joining peer never reached data-channels-open within {JoinTimeoutSeconds}s");
            Assert.IsTrue(_hostReachedChannelsOpen,
                $"the host never reached data-channels-open within {JoinTimeoutSeconds}s");

            // Logged, never asserted — the number ticket 03 has to beat.
            Debug.Log($"[IceHarness] BASELINE join duration (join attempt -> both data channels open): " +
                      $"{join.Elapsed.TotalSeconds:F2}s");
#else
            yield break;
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                // Cancel any live signal timeouts while the NetworkManager they log through is still
                // alive; their continuations would otherwise fault against a destroyed manager.
                ResetTransportSignalTimeouts();

                if (_networkManager != null)
                {
                    if (_networkManager.ClientManager.Started) _networkManager.ClientManager.StopConnection();
                    if (_networkManager.ServerManager.Started) _networkManager.ServerManager.StopConnection(true);
                }
                yield return null;

                // The signal socket closes with its owner, so drop it before the NetworkManager.
                if (_signalManagerObject != null) Object.Destroy(_signalManagerObject);
                if (_networkManager != null) Object.Destroy(_networkManager.gameObject);
                yield return null;
            }
            finally
            {
                // Reaped on the pass path and the fail path alike: an assertion that fires mid-join
                // must not leave a listener holding the port for the next run.
                _signalServer?.Dispose();
                _signalServer = null;

                ResetSignalManagerStatics();

                _signalManagerObject = null;
                _networkManager = null;
                _roomCode = null;
                _hostReachedChannelsOpen = false;
                _clientReachedChannelsOpen = false;
            }
        }

#if UNITY_EDITOR

        // ---- fixture steps -------------------------------------------------------------------

        IEnumerator LaunchSignalServer()
        {
            string serverDirectory = FindSignalServerDirectory();
            Assert.IsNotNull(serverDirectory,
                "no signal server found: expected a sibling ArenaInfra/signal-server checkout, " +
                "or ARENA_SIGNAL_SERVER_DIR pointing at one");
            Assert.IsTrue(Directory.Exists(Path.Combine(serverDirectory, "node_modules")),
                $"signal server dependencies are not installed — run `npm install` in {serverDirectory}");

            _signalServer = SignalServerProcess.Launch(serverDirectory);
            yield return WaitUntil(() => _signalServer.IsListening, SignalServerReadyTimeoutSeconds);
            Assert.IsTrue(_signalServer.IsListening,
                $"the signal server never opened port {SignalServerProcess.Port}. Its output was:\n" +
                _signalServer.CapturedOutput);
        }

        IEnumerator StartHost()
        {
            // Step 0: a known-empty scene, so a previous test's scene NetworkObjects can't be
            // re-claimed by the NetworkManager we are about to start.
            EditorSceneManager.LoadSceneInPlayMode(BlankScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestNetworkManagerPath);
            Assert.IsNotNull(prefab, $"TestNetworkManager prefab missing at {TestNetworkManagerPath}");

            // The prefab is saved inactive, so everything below happens before any Awake runs.
            var networkManagerObject = Object.Instantiate(prefab);
            networkManagerObject.name = "IceHarnessNetworkManager";

            var tugboat = networkManagerObject.GetComponentInChildren<Tugboat>(true);
            Assert.IsNotNull(tugboat, "TestNetworkManager no longer carries a Tugboat to swap out");
            var transportObject = tugboat.gameObject;
            Object.DestroyImmediate(tugboat);

            var transport = transportObject.AddComponent<CanoeWebRTC>();
            ConfigureLikeBootScene(transport);
            transport.OnClientConnectionState += HandleClientConnectionState;
            transport.OnRemoteConnectionState += HandleRemoteConnectionState;

            var transportManager = networkManagerObject.GetComponentInChildren<TransportManager>(true);
            Assert.IsNotNull(transportManager, "TestNetworkManager has no TransportManager");
            transportManager.Transport = transport;

            networkManagerObject.SetActive(true);
            Object.DontDestroyOnLoad(networkManagerObject);
            yield return null;

            _networkManager = networkManagerObject.GetComponent<NetworkManager>();
            Assert.IsNotNull(_networkManager, "IceHarnessNetworkManager has no NetworkManager");

            // SignalManager dials ws://localhost:9001/Signal from Start when running in the editor.
            _signalManagerObject = new GameObject("IceHarnessSignalManager");
            _signalManagerObject.AddComponent(SignalManagerType);
            yield return null;

            yield return WaitUntil(() => SignalClientState() == "Connected", SignalSocketTimeoutSeconds);
            Assert.AreEqual("Connected", SignalClientState(),
                "the signal socket never connected. The server's output was:\n" + _signalServer.CapturedOutput);

            _networkManager.ServerManager.StartConnection();
            yield return WaitUntil(() => _networkManager.ServerManager.Started, ServerStartTimeoutSeconds);
            Assert.IsTrue(_networkManager.ServerManager.Started, "the host's server never started");
        }

        IEnumerator CreateRoom()
        {
            // SignalManager refuses to create a room until the server is started, which is why this
            // follows StartHost rather than running beside it.
            AddRoomCreatedHandler(code => _roomCode = code);
            InvokeOnSignalManager("CreateRoom");

            yield return WaitUntil(() => _roomCode != null, RoomCreatedTimeoutSeconds);
            Assert.IsNotNull(_roomCode, "the signal server never issued a room code");
        }

        static void ConfigureLikeBootScene(CanoeWebRTC transport)
        {
            transport.MTU = BootMtu;
            transport.filterLocalConnections = false;
            transport.onlyAllowRelay = false;
            transport.operationTimeoutDurationMilliseconds = BootOperationTimeoutMs;
            transport.signalTimeoutDurationMilliseconds = BootSignalTimeoutMs;

            transport.iceServers = new List<ICEServer>();
            foreach (var url in BootIceServerUrls)
                transport.iceServers.Add(new ICEServer { url = url });
        }

        void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            // The client socket only reports Started once its peer connection has BOTH remote data
            // channels (Connection.RemoteChannelsOpened) — that is the channels-open signal.
            if (args.ConnectionState == LocalConnectionState.Started) _clientReachedChannelsOpen = true;
        }

        void HandleRemoteConnectionState(RemoteConnectionStateArgs args)
        {
            // Same signal from the host's side of the same peer connection.
            if (args.ConnectionState == RemoteConnectionState.Started) _hostReachedChannelsOpen = true;
        }

        /// <summary>
        /// The sibling ArenaInfra checkout. This project is usually opened from a git worktree
        /// several levels below the directory ArenaInfra sits next to, so walk up rather than
        /// assuming a fixed depth.
        /// </summary>
        static string FindSignalServerDirectory()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("ARENA_SIGNAL_SERVER_DIR");
            if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

            var directory = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "ArenaInfra", "signal-server");
                if (File.Exists(Path.Combine(candidate, "SignalServer.js"))) return candidate;
                directory = directory.Parent;
            }
            return null;
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            // Wall-clock, not frame-count: every wait here is on a real async network operation.
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        // ---- SignalManager bridge (reflection contained here) ---------------------------------
        // SignalManager lives in Assembly-CSharp, which a test asmdef cannot reference — the same
        // constraint the *TestRig classes exist for. Tests above never see reflection.

        static Type SignalManagerType => Type.GetType("SignalManager, Assembly-CSharp");

        static object SignalManagerInstance =>
            SignalManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        static void InvokeOnSignalManager(string method, params object[] args)
        {
            var instance = SignalManagerInstance;
            Assert.IsNotNull(instance, "SignalManager.Instance was never set");
            var methodInfo = SignalManagerType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(methodInfo, $"SignalManager.{method} not found");
            methodInfo.Invoke(instance, args);
        }

        static string SignalClientState()
        {
            try
            {
                var instance = SignalManagerInstance;
                if (instance == null) return "None";
                return SignalManagerType
                    .GetProperty("ClientState", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(instance)?.ToString() ?? "None";
            }
            catch (TargetInvocationException)
            {
                return "None"; // the socket is not built yet
            }
        }

        static void AddRoomCreatedHandler(Action<string> handler)
        {
            var field = SignalManagerType.GetField("OnRoomCreated", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, "SignalManager.OnRoomCreated not found");
            field.SetValue(null, Delegate.Combine((Delegate)field.GetValue(null), handler));
        }

        /// <summary>
        /// SignalManager keeps its instance and its listeners in statics that outlive the GameObject,
        /// so a second test would find the singleton already claimed and never dial the server.
        /// </summary>
        static void ResetSignalManagerStatics()
        {
            var type = SignalManagerType;
            if (type == null) return;

            foreach (var name in new[] { "OnRoomCreated", "OnRoomJoined", "OnClientStateChanged" })
                type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.SetValue(null, null);

            type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                ?.GetSetMethod(nonPublic: true)?.Invoke(null, new object[] { null });
        }

        static void ResetTransportSignalTimeouts()
        {
            // Snapshot first: cancelling resumes the transport's own timeout task, which removes its
            // entry from this very dictionary.
            var live = new List<System.Threading.CancellationTokenSource>(
                CanoeWebRTC.remoteCancellationTokenSources.Values);
            CanoeWebRTC.remoteCancellationTokenSources.Clear();

            foreach (var source in live)
            {
                try { source.Cancel(); }
                catch (ObjectDisposedException) { /* already stopped */ }
            }
            CanoeWebRTC.clientCancellationTokenSource = null;
        }
#endif
    }
}
