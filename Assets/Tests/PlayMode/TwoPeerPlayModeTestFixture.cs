using System.Collections;
using System.Linq;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Arena.PlayTests
{
    /// <summary>
    /// Base class for PlayMode tests that need TWO separate FishNet peers in one process: a
    /// server-only NetworkManager and a client-only NetworkManager, connected over Tugboat on
    /// localhost. Unlike <see cref="NetworkedPlayModeTestFixture"/> (a single host where the local
    /// client IS the server, so nothing is ever predicted), this lets us exercise a genuinely REMOTE
    /// client ??? including FishNet predicted spawning and the client/server round-trip.
    ///
    /// FishNet supports multiple NetworkManagers via PersistenceType.AllowMultiple (the TestNetworkManager
    /// prefab is already set to it). There is no in-memory transport and no manual tick API, so the peers
    /// talk over real localhost UDP and FishNet auto-ticks from the engine loop; <see cref="SetLatency"/>
    /// uses FishNet's LatencySimulator to add a deterministic round-trip delay so predicted-vs-replicated
    /// timing is observable.
    ///
    /// No gameplay scene with scene NetworkObjects is loaded (two NMs would both try to claim them);
    /// tests spawn runtime prefabs instead.
    /// </summary>
    public abstract class TwoPeerPlayModeTestFixture
    {
        const string TestNetworkManagerPath = "Assets/Tests/PlayMode/TestNetworkManager.prefab";
        const string BlankScenePath = "Assets/Tests/PlayMode/Scenes/TestSmoke.unity";
        const float ConnectionTimeoutSeconds = 8f;

        protected NetworkManager ServerNM { get; private set; }
        protected NetworkManager ClientNM { get; private set; }

        /// <summary>The remote client's connection as seen by the server (the spawn owner for client-owned objects).</summary>
        protected NetworkConnection ClientOnServer =>
            ServerNM.ServerManager.Clients.Values.FirstOrDefault(c => c != null && c.IsActive);

        [UnitySetUp]
        public IEnumerator TwoPeerSetUp()
        {
#if UNITY_EDITOR
            // Start from a known-empty scene so nothing else claims either NetworkManager.
            EditorSceneManager.LoadSceneInPlayMode(BlankScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestNetworkManagerPath);
            Assert.IsNotNull(prefab, $"TestNetworkManager prefab missing at {TestNetworkManagerPath}");

            // Two independent NetworkManagers (prefab is PersistenceType.AllowMultiple).
            // The prefab is saved inactive, so SetActive(true) is required for Awake (which
            // initializes ServerManager/ClientManager) to run.
            var serverObj = Object.Instantiate(prefab);
            serverObj.name = "ServerNM";
            ForceUnityPhysics(serverObj); // before Awake: two manual-physics TimeManagers are unsupported
            serverObj.SetActive(true);
            Object.DontDestroyOnLoad(serverObj);
            var clientObj = Object.Instantiate(prefab);
            clientObj.name = "ClientNM";
            ForceUnityPhysics(clientObj);
            clientObj.SetActive(true);
            Object.DontDestroyOnLoad(clientObj);
            yield return null;

            ServerNM = serverObj.GetComponent<NetworkManager>();
            ClientNM = clientObj.GetComponent<NetworkManager>();
            Assert.IsNotNull(ServerNM, "ServerNM has no NetworkManager");
            Assert.IsNotNull(ClientNM, "ClientNM has no NetworkManager");
            Assert.AreNotSame(ServerNM, ClientNM, "expected two distinct NetworkManagers");

            ServerNM.ServerManager.StartConnection();
            float t = 0f;
            while (!ServerNM.ServerManager.Started && t < ConnectionTimeoutSeconds) { t += Time.deltaTime; yield return null; }
            Assert.IsTrue(ServerNM.ServerManager.Started, "server failed to start");

            ClientNM.ClientManager.StartConnection();
            t = 0f;
            while ((!ClientNM.ClientManager.Started || ClientOnServer == null) && t < ConnectionTimeoutSeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(ClientNM.ClientManager.Started, "client failed to connect");
            Assert.IsNotNull(ClientOnServer, "server never registered the remote client connection");

            // A few frames for authentication / initial sync to settle.
            for (int i = 0; i < 5; i++) yield return null;
#else
            Assert.Fail("Two-peer PlayMode tests require the Unity Editor.");
            yield break;
#endif
        }

        /// <summary>
        /// Set the NM's TimeManager to Unity-driven physics BEFORE Awake. FishNet forbids two
        /// TimeManagers both using manual (TimeManager) physics in one process; our projectiles/pawns
        /// don't rely on FishNet manual physics, so Unity mode is fine for these tests.
        /// </summary>
        static void ForceUnityPhysics(GameObject nmObject)
        {
            var tm = nmObject.GetComponentInChildren<FishNet.Managing.Timing.TimeManager>(true);
            if (tm == null) return;
            var f = typeof(FishNet.Managing.Timing.TimeManager).GetField("_physicsMode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(tm, FishNet.Managing.Timing.PhysicsMode.Unity);
        }

        /// <summary>Add a deterministic round-trip delay (each direction) via FishNet's LatencySimulator.</summary>
        protected void SetLatency(long milliseconds)
        {
            foreach (var nm in new[] { ServerNM, ClientNM })
            {
                var sim = nm.TransportManager.LatencySimulator;
                sim.SetLatency(milliseconds);
                sim.SetEnabled(milliseconds > 0);
            }
        }

        /// <summary>Server-spawns a prefab owned by the remote client, so on the client it is IsOwner.</summary>
        protected GameObject ServerSpawnOwnedByClient(string prefabPath, Vector3 position)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"prefab missing at {prefabPath}");
            var instance = Object.Instantiate(prefab, position, Quaternion.identity);
            ServerNM.ServerManager.Spawn(instance, ClientOnServer);
            return instance;
#else
            return null;
#endif
        }

        /// <summary>Finds an object by ObjectId in a specific NetworkManager's spawned set (avoids InstanceFinder ambiguity).</summary>
        protected static GameObject FindSpawnedOn(NetworkManager nm, int objectId)
        {
            if (nm == null) return null;
            // Prefer the client cache when present; both managers expose Objects.Spawned.
            if (nm.IsClientStarted && nm.ClientManager.Objects.Spawned.TryGetValue(objectId, out var cnob) && cnob != null)
                return cnob.gameObject;
            if (nm.IsServerStarted && nm.ServerManager.Objects.Spawned.TryGetValue(objectId, out var snob) && snob != null)
                return snob.gameObject;
            return null;
        }

        /// <summary>The first spawned object on a manager carrying the given (Assembly-CSharp) component, or null.</summary>
        protected static GameObject FindFirstWithComponent(NetworkManager nm, string componentTypeName)
        {
            var spawned = nm.IsClientStarted ? nm.ClientManager.Objects.Spawned : nm.ServerManager.Objects.Spawned;
            foreach (var nob in spawned.Values)
                if (nob != null && nob.GetComponent(componentTypeName) != null)
                    return nob.gameObject;
            return null;
        }

        /// <summary>Counts spawned objects on a manager that carry a component of the given (Assembly-CSharp) type name.</summary>
        protected static int CountSpawnedWithComponent(NetworkManager nm, string componentTypeName)
        {
            int n = 0;
            var spawned = nm.IsClientStarted ? nm.ClientManager.Objects.Spawned : nm.ServerManager.Objects.Spawned;
            foreach (var nob in spawned.Values)
                if (nob != null && nob.GetComponent(componentTypeName) != null)
                    n++;
            return n;
        }

        [UnityTearDown]
        public IEnumerator TwoPeerTearDown()
        {
            if (ClientNM != null && ClientNM.ClientManager.Started) ClientNM.ClientManager.StopConnection();
            if (ServerNM != null && ServerNM.ServerManager.Started) ServerNM.ServerManager.StopConnection(true);
            yield return null;
            if (ClientNM != null) Object.Destroy(ClientNM.gameObject);
            if (ServerNM != null) Object.Destroy(ServerNM.gameObject);
            yield return null;
            ServerNM = null;
            ClientNM = null;
        }
    }
}
