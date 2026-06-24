using System.Collections;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Proves the PlayMode networking scaffolding works end-to-end:
    /// fixture loads the named scene, instantiates the TestNetworkManager prefab,
    /// and brings the FishNet host (server + local client) online.
    ///
    /// If this passes, every other test built on NetworkedPlayModeTestFixture
    /// can assume a live host.
    /// </summary>
    public sealed class SmokeTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestSmoke.unity";

        [UnityTest]
        public IEnumerator Host_is_running_and_test_scene_is_active()
        {
            // The fixture already asserted server + client started in [UnitySetUp].
            // Re-assert here so the test itself owns a clear failure message.
            Assert.IsTrue(NetworkManager.ServerManager.Started, "Server should be started");
            Assert.IsTrue(NetworkManager.ClientManager.Started, "Client should be connected");

            Assert.AreEqual(
                "TestSmoke",
                SceneManager.GetActiveScene().name,
                "Test scene should be the active scene"
            );

            yield return null;
        }
    }
}
