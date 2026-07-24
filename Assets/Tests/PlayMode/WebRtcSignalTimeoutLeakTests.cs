#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.CanoeWebRTC;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Regression test for the host-side join leak fixed in ADR 0012.
    ///
    /// The host used to cancel its signal timeout the moment the client's **answer** arrived. An
    /// answer only means the two peers negotiated - it says nothing about whether they connected -
    /// so a join that negotiated and then never established left its <c>Connection</c> wrapper and
    /// its <c>RTCPeerConnection</c> alive forever, with no timer left to reap them. The timeout is
    /// now cancelled on <c>RemoteConnectionState.Started</c> instead, so a join that never
    /// establishes is torn down when the deadline passes.
    ///
    /// The test drives the transport directly rather than through a FishNet host: it needs a host
    /// that has offered and is waiting on a peer that never arrives, which no live peer would
    /// reproduce. No signal server is involved, and with no ICE servers configured the offer is
    /// built from host candidates alone, so the test needs no network.
    ///
    /// Deadlines are shortened from the shipped 39.5s so the test runs in seconds - the behaviour
    /// under test is *that* the deadline reaps the connection, not how long it is.
    /// </summary>
    public sealed class WebRtcSignalTimeoutLeakTests
    {
        const string TestNetworkManagerPath = "Assets/Tests/PlayMode/TestNetworkManager.prefab";

        /// Shortened stand-in for the shipped 39.5s (ADR 0012).
        const int SignalTimeoutMilliseconds = 5000;

        const float ServerStartTimeoutSeconds = 5f;
        const float OfferTimeoutSeconds = 10f;

        /// After the signal deadline the timeout path waits out one more operation timeout before
        /// it reaps, so allow well past both.
        const float TeardownTimeoutSeconds = 20f;

        GameObject _networkManagerObject;
        GameObject _transportObject;
        CanoeWebRTC _transport;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // The transport logs through InstanceFinder.NetworkManager on every path, so a
            // NetworkManager has to exist. It is never started - FishNet drives nothing here.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestNetworkManagerPath);
            Assert.IsNotNull(prefab, $"TestNetworkManager prefab missing at {TestNetworkManagerPath}");

            _networkManagerObject = Object.Instantiate(prefab);
            _networkManagerObject.name = "SignalTimeoutTestNetworkManager";
            _networkManagerObject.SetActive(true);
            Assert.IsNotNull(_networkManagerObject.GetComponent<NetworkManager>(),
                "Instantiated TestNetworkManager has no NetworkManager component");
            yield return null;

            // Separate GameObject: the prefab's TransportManager already owns a Tugboat, and this
            // transport is driven by hand, not by FishNet.
            _transportObject = new GameObject("SignalTimeoutTestTransport");
            _transport = _transportObject.AddComponent<CanoeWebRTC>();
            _transport.iceServers = new List<ICEServer>();
            _transport.signalTimeoutDurationMilliseconds = SignalTimeoutMilliseconds;

            Assert.IsTrue(_transport.StartConnection(true), "server transport failed to start");

            yield return PumpUntil(
                () => _transport.GetConnectionState(true) == LocalConnectionState.Started,
                ServerStartTimeoutSeconds);
            Assert.AreEqual(LocalConnectionState.Started, _transport.GetConnectionState(true),
                "server transport never reported Started");

            // Tearing an RTCPeerConnection down is chatty in the console; this test asserts on
            // host state, not on log output.
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTest]
        public IEnumerator Host_holds_no_connection_for_a_join_that_never_establishes()
        {
            int connectionId = CanoeWebRTC.CreateNewRemoteConnection();

            var offerTask = CanoeWebRTC.CreateOfferForClient(connectionId);
            yield return PumpUntil(() => offerTask.IsCompleted, OfferTimeoutSeconds);
            Assert.IsTrue(offerTask.IsCompleted, "host never produced an offer");
            Assert.IsFalse(offerTask.Result.error,
                $"host failed to produce an offer: {offerTask.Result.errorMessage}");

            // The host is now mid-join: it has offered, and no peer will ever answer or connect.
            Assert.IsTrue(CanoeWebRTC._server.connections.ContainsKey(connectionId),
                "host should still hold the connection while the join is in flight");
            Assert.IsTrue(CanoeWebRTC.remoteCancellationTokenSources.ContainsKey(connectionId),
                "the signal timeout should still be running while the join is in flight");

            yield return PumpUntil(
                () => !CanoeWebRTC._server.connections.ContainsKey(connectionId),
                TeardownTimeoutSeconds);

            Assert.IsFalse(CanoeWebRTC._server.connections.ContainsKey(connectionId),
                "host leaked the connection wrapper (and its RTCPeerConnection) past the deadline");
            Assert.IsFalse(CanoeWebRTC.remoteCancellationTokenSources.ContainsKey(connectionId),
                "host leaked the signal timeout past the deadline");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            if (_transport != null)
            {
                _transport.StopConnection(true);
                yield return null;
            }

            // The token sources are static, so a failed run must not poison the next test.
            foreach (CancellationTokenSource source in CanoeWebRTC.remoteCancellationTokenSources.Values)
                source.Dispose();
            CanoeWebRTC.remoteCancellationTokenSources.Clear();

            if (_transportObject != null)
                Object.Destroy(_transportObject);
            if (_networkManagerObject != null)
                Object.Destroy(_networkManagerObject);
            yield return null;

            _transport = null;
            _transportObject = null;
            _networkManagerObject = null;
        }

        /// <summary>
        /// Pumps the transport's incoming queue once per frame until <paramref name="condition"/>
        /// holds or the budget runs out. Nothing else drives this transport, and the server-side
        /// close only runs while the socket reports Started, which needs that queue drained.
        /// </summary>
        IEnumerator PumpUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!condition() && elapsed < timeoutSeconds)
            {
                _transport.IterateIncoming(true);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            _transport.IterateIncoming(true);
        }
    }
}
#endif
