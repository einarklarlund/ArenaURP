using System.Collections;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Timing;
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
    /// Base class for PlayMode integration tests that need a live FishNet host
    /// (server + client in the same process). Mirrors the dev-bootstrap path in
    /// Bootstrap.cs: instantiates a TestNetworkManager prefab (same components/
    /// settings as the Debug NetworkManager), starts the server, then the client.
    ///
    /// **Ordering matters.** The host (server + client) is brought up BEFORE the
    /// test scene is loaded. FishNet's NetworkObject.Start auto-deactivates scene
    /// NetworkObjects if no NetworkManager is running at load time — if we loaded
    /// the scene first, every scene NetworkObject (MatchMediator, BotSpawner, etc.)
    /// would silently flip inactive, breaking FindAnyObjectByType lookups in
    /// OnStartServer. With the host already up, the scene-loaded hook in
    /// ServerObjects auto-registers the scene NetworkObjects via SetupSceneObjects.
    ///
    /// Subclasses override <see cref="ScenePath"/> to specify which test scene
    /// to load — that's the seam that makes this reusable across many scenes.
    /// </summary>
    public abstract class NetworkedPlayModeTestFixture
    {
        const string TestNetworkManagerPath = "Assets/Tests/PlayMode/TestNetworkManager.prefab";
        const string BlankScenePath = "Assets/Tests/PlayMode/Scenes/TestSmoke.unity";
        const float ConnectionTimeoutSeconds = 5f;
        const int FramesToSettleSceneSpawns = 3;

        protected NetworkManager NetworkManager { get; private set; }

        /// <summary>Asset-path of the test scene to load after the host is up.</summary>
        protected abstract string ScenePath { get; }

        [UnitySetUp]
        public IEnumerator NetworkedSetUp()
        {
#if UNITY_EDITOR
            // 0. Wipe any residue from a previous test in this run. Without this, the
            //    DontDestroyOnLoad NetworkManager below would survive but the previous
            //    test's scene NetworkObjects would still be loaded, and FishNet would
            //    try to re-init them under the new NetworkManager. Loading a known-
            //    empty scene via LoadSceneMode.Single clears everything.
            EditorSceneManager.LoadSceneInPlayMode(
                BlankScenePath,
                new LoadSceneParameters(LoadSceneMode.Single)
            );
            yield return null;

            // 1. Instantiate the TestNetworkManager prefab in the blank scene, BEFORE
            //    loading any test scene. DontDestroyOnLoad so LoadSceneMode.Single
            //    below doesn't nuke it.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestNetworkManagerPath);
            Assert.IsNotNull(prefab, $"TestNetworkManager prefab missing at {TestNetworkManagerPath}");

            var nmObject = Object.Instantiate(prefab);
            nmObject.name = "TestNetworkManager";
            nmObject.SetActive(true);
            Object.DontDestroyOnLoad(nmObject);
            yield return null;

            NetworkManager = nmObject.GetComponent<NetworkManager>();
            Assert.IsNotNull(NetworkManager, "Instantiated TestNetworkManager has no NetworkManager component");

            // 2. Start the host: server, then client when server reports Started.
            bool clientStartTriggered = false;
            void OnServerState(ServerConnectionStateArgs args)
            {
                if (args.ConnectionState == LocalConnectionState.Started && !clientStartTriggered)
                {
                    clientStartTriggered = true;
                    NetworkManager.ClientManager.StartConnection();
                }
            }
            NetworkManager.ServerManager.OnServerConnectionState += OnServerState;
            NetworkManager.ServerManager.StartConnection();

            float elapsed = 0f;
            while (
                (!NetworkManager.ServerManager.Started || !NetworkManager.ClientManager.Started)
                && elapsed < ConnectionTimeoutSeconds
            )
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            NetworkManager.ServerManager.OnServerConnectionState -= OnServerState;

            Assert.IsTrue(NetworkManager.ServerManager.Started, "Server failed to start within timeout");
            Assert.IsTrue(NetworkManager.ClientManager.Started, "Client failed to connect within timeout");

            // 3. Load the test scene. ServerManager.Started == true at this point, so:
            //    - Scene NetworkObjects.Start() doesn't auto-deactivate them.
            //    - FishNet's SceneManager_sceneLoaded queues them; SetupSceneObjects
            //      fires them through their normal OnStartServer pipeline next tick.
            EditorSceneManager.LoadSceneInPlayMode(
                ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single)
            );
            // Give FishNet a couple of frames to drain its loaded-scene queue and fire
            // each scene NetworkObject's OnStartServer.
            for (int i = 0; i < FramesToSettleSceneSpawns; i++) yield return null;
#else
            Assert.Fail("PlayMode tests in this fixture require the Unity Editor (uses EditorSceneManager).");
            yield break;
#endif
        }

        /// <summary>
        /// Server-spawns a prefab into the test scene and returns the live instance.
        /// Caller should yield 1-2 frames before asserting so the network spawn settles.
        /// </summary>
        protected GameObject SpawnServerPrefab(string prefabPath, Vector3 position)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, $"Prefab missing at {prefabPath}");
            var instance = Object.Instantiate(prefab, position, Quaternion.identity);
            NetworkManager.ServerManager.Spawn(instance);
            return instance;
#else
            Assert.Fail("SpawnServerPrefab requires the Unity Editor (uses AssetDatabase).");
            return null;
#endif
        }

        /// <summary>
        /// Server-spawns a projectile through the PRODUCTION path: a real authored <c>ProjectileType</c>
        /// asset's <c>Create</c> (which initializes the instance's generic components) + the spawn payload,
        /// driven by <c>ProjectileSpawnTestRig.SpawnViaType</c>. The rig lives in Assembly-CSharp
        /// (unreferenceable from this asmdef), so it's invoked by reflection — contained to this one helper.
        /// <paramref name="typeAssetPath"/> is a `.asset` ProjectileType (e.g. Assets/Data/Projectiles/Bullet.asset).
        /// </summary>
        protected GameObject SpawnProjectile(string typeAssetPath, Vector3 position, Vector3 forward,
            int ownerId = 999)
        {
#if UNITY_EDITOR
            var type = AssetDatabase.LoadMainAssetAtPath(typeAssetPath);
            Assert.IsNotNull(type, $"ProjectileType asset missing at {typeAssetPath}");

            var rig = System.Type.GetType("ProjectileSpawnTestRig, Assembly-CSharp");
            Assert.IsNotNull(rig, "ProjectileSpawnTestRig not found in Assembly-CSharp");
            int id = (int)rig.GetMethod("SpawnViaType").Invoke(null, new object[] { type, position, forward, ownerId });
            var go = (GameObject)rig.GetMethod("FindSpawned").Invoke(null, new object[] { id });
            Assert.IsNotNull(go, $"spawned projectile (id {id}) not found after SpawnViaType");
            return go;
#else
            Assert.Fail("SpawnProjectile requires the Unity Editor (uses AssetDatabase).");
            return null;
#endif
        }

        /// <summary>
        /// Drives FakePawnInput on a spawned pawn via reflection. The PlayTests
        /// asmdef can't reference FakePawnInput's type directly (it lives in
        /// Assembly-CSharp), so the test asks for it by name. Reflection is
        /// contained to this one helper — tests stay clean.
        /// </summary>
        protected static void SetFakePawnInput(GameObject pawn, Vector2 move, bool jump = false)
        {
            var comp = pawn.GetComponent("FakePawnInput");
            Assert.IsNotNull(comp, "FakePawnInput component not found on pawn");
            var type = comp.GetType();
            type.GetField("Move").SetValue(comp, move);
            type.GetField("Jump").SetValue(comp, jump);
        }

        [UnityTearDown]
        public IEnumerator NetworkedTearDown()
        {
            if (NetworkManager != null)
            {
                if (NetworkManager.ClientManager.Started)
                    NetworkManager.ClientManager.StopConnection();
                if (NetworkManager.ServerManager.Started)
                    NetworkManager.ServerManager.StopConnection(true);
                yield return null;

                if (NetworkManager != null && NetworkManager.gameObject != null)
                    Object.Destroy(NetworkManager.gameObject);
            }
            yield return null;
            NetworkManager = null;
        }
    }
}
