using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Smoke test that the two-peer (separate server NM + client NM) in-process harness actually
    /// connects over localhost, and that the LatencySimulator is usable. This validates the foundation
    /// the predicted-spawn tests build on.
    /// </summary>
    public sealed class TwoPeerConnectivityTests : TwoPeerPlayModeTestFixture
    {
        [UnityTest]
        public IEnumerator Server_and_remote_client_connect_in_process()
        {
            Assert.IsTrue(ServerNM.ServerManager.Started, "server should be started");
            Assert.IsTrue(ClientNM.ClientManager.Started, "client should be connected");
            Assert.IsNotNull(ClientOnServer, "server should see the remote client");
            Assert.AreNotSame(ServerNM, ClientNM, "two distinct NetworkManagers");

            // LatencySimulator should be drivable (DEVELOPMENT/editor).
            SetLatency(120);
            Assert.IsTrue(ServerNM.TransportManager.LatencySimulator.GetEnabled(), "latency sim should enable");
            Assert.AreEqual(120, ServerNM.TransportManager.LatencySimulator.GetLatency());
            SetLatency(0);
            yield return null;
        }
    }
}
