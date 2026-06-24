using System;
using System.Collections;
using System.Reflection;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-B test on the two-peer harness (separate server + remote-client NetworkManagers): a remote
    /// client must OBSERVE server-authoritative state changes. This is the only class of test that can
    /// catch server/client divergence — the scariest bug in a server-authoritative game — because the
    /// single-host fixture's client IS the server, so nothing is ever actually replicated.
    ///
    /// A bare Pawn is spawned on the server; the server reduces its Health (a ServerOnly SyncVar); the
    /// test asserts the CLIENT's replicated copy of that Pawn reflects the new Health. If the SyncVar
    /// stops replicating (write-permission drift, a missed dirty, a serializer change), this goes red.
    /// </summary>
    public sealed class ClientObservesStateTests : TwoPeerPlayModeTestFixture
    {
        const string TargetPawnPrefab = "Assets/Tests/PlayMode/Prefabs/TestTargetPawn.prefab";
        const int StartHealth = 21;
        const int Damage = 5;

        [UnityTest]
        public IEnumerator Server_health_change_replicates_to_the_remote_client()
        {
            // Spawn a pawn on the server (owned by the remote client) and wait for it to replicate.
            var serverPawn = ServerSpawnOwnedByClient(TargetPawnPrefab, new Vector3(0f, 1f, 0f));
            Assert.IsNotNull(serverPawn, "server should spawn the target pawn");
            int objectId = serverPawn.GetComponent<NetworkObject>().ObjectId;

            GameObject clientPawn = null;
            float t = 0f;
            while ((clientPawn = FindSpawnedOn(ClientNM, objectId)) == null && t < 6f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsNotNull(clientPawn, "the pawn should replicate to the remote client");

            // Precondition: both ends agree on the starting health.
            Assert.AreEqual(StartHealth, (int)Rig("HealthOfGo", serverPawn), "server starts at full health");
            float tt = 0f;
            while ((int)Rig("HealthOfGo", clientPawn) != StartHealth && tt < 2f)
            {
                tt += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(StartHealth, (int)Rig("HealthOfGo", clientPawn),
                "the client's replicated copy should start at full health");

            // Server-authoritative damage.
            Rig("ServerDamageGo", serverPawn, Damage);
            Assert.AreEqual(StartHealth - Damage, (int)Rig("HealthOfGo", serverPawn),
                "the server applies the damage immediately");

            // The remote client must observe the new Health via SyncVar replication.
            float w = 0f;
            while ((int)Rig("HealthOfGo", clientPawn) != StartHealth - Damage && w < 3f)
            {
                w += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(StartHealth - Damage, (int)Rig("HealthOfGo", clientPawn),
                $"the remote client should observe the server's Health change within ~{w:F2}s; a stale " +
                "value means the Health SyncVar isn't replicating server -> client (divergence).");
        }

        // --- MatchTestRig reflection bridge (GameObject-addressed helpers; no managers in two-peer) ---

        static readonly Type RigType = Type.GetType("MatchTestRig, Assembly-CSharp");

        static object Rig(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "MatchTestRig not found in Assembly-CSharp");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"MatchTestRig.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
