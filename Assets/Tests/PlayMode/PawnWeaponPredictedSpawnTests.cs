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
    /// Reproduces the reported predicted-spawn gap: when a REMOTE (non-host) client fires, its projectile
    /// should appear locally on the SAME frame via FishNet predicted spawning ??? not only after the
    /// server's authoritative spawn round-trips back. With a large simulated round-trip latency, a
    /// correctly-predicted bullet is on the client immediately, while a server-replication-only bullet
    /// would be absent for the whole latency window.
    ///
    /// Runs over the two-peer in-process harness (separate server + client NMs, Tugboat loopback).
    /// </summary>
    public sealed class PawnWeaponPredictedSpawnTests : TwoPeerPlayModeTestFixture
    {
        const string ShooterPrefabPath = "Assets/Tests/PlayMode/Prefabs/TestShooter.prefab";
        const long LatencyMs = 300; // each direction; authoritative round-trip ~600ms+

        [UnityTest]
        public IEnumerator Remote_client_predicts_its_projectile_locally_under_latency()
        {
            SetLatency(LatencyMs);

            // Distinctive, non-origin spawn site so "at the origin" is unmistakably wrong.
            var shooterPos = new Vector3(10f, 2f, -5f);
            // Server-spawn a shooter owned by the remote client; wait for it to replicate to the client.
            var serverShooter = ServerSpawnOwnedByClient(ShooterPrefabPath, shooterPos);
            Assert.IsNotNull(serverShooter, "server should spawn the shooter");
            int shooterId = serverShooter.GetComponent<NetworkObject>().ObjectId;

            GameObject clientShooter = null;
            float t = 0f;
            while ((clientShooter = FindSpawnedOn(ClientNM, shooterId)) == null && t < 6f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.IsNotNull(clientShooter, "shooter should replicate to the client NM");
            Assert.AreEqual(0, CountSpawnedWithComponent(ClientNM, "NetworkedMotion"),
                "precondition: no projectile on the client before firing");

            // Fire on the client's OWNED shooter (PawnWeapon.Update runs because IsOwner on the client).
            uint clientTickAtFire = ClientNM.TimeManager.Tick;
            RigInvoke("SetFire", clientShooter, true);

            // Within a short window ??? far less than the one-way latency ??? the predicted projectile must
            // already exist on the CLIENT. (Authoritative spawn cannot have round-tripped yet.)
            float window = 0f;
            int onClient = 0;
            while (window < 0.12f) // 120ms << 300ms one-way latency
            {
                onClient = CountSpawnedWithComponent(ClientNM, "NetworkedMotion");
                if (onClient > 0) break;
                window += Time.deltaTime;
                yield return null;
            }
            RigInvoke("SetFire", clientShooter, false);

            Assert.GreaterOrEqual(onClient, 1,
                $"the owning client must PREDICT-spawn its projectile locally within ~120ms of firing " +
                $"(latency is {LatencyMs}ms each way, so the server's authoritative spawn cannot have " +
                $"arrived yet). 0 here means the client waited for server replication — the bug.");

            // ...and it must be POSITIONED at the fire point, not flashing at the origin.
            var bullet = FindFirstWithComponent(ClientNM, "NetworkedMotion");
            Assert.IsNotNull(bullet, "predicted projectile should be findable on the client");
            var firePoint = clientShooter.transform.Find("FirePoint");
            Assert.Greater(Vector3.Distance(bullet.transform.position, Vector3.zero), 5f,
                $"the predicted projectile must NOT sit at the origin; it was at {bullet.transform.position}. " +
                "Spawn site is ~(10,3,-5), so an origin position is the reported bug.");
            Assert.Less(Vector3.Distance(bullet.transform.position, firePoint.position), 2f,
                $"the predicted projectile should be at the fire point {firePoint.position}, was {bullet.transform.position}");
        }

        static readonly Type RigType = Type.GetType("ProjectileSpawnTestRig, Assembly-CSharp");

        static object RigInvoke(string method, params object[] args)
        {
            Assert.IsNotNull(RigType, "ProjectileSpawnTestRig not found");
            var mi = RigType.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(mi, $"ProjectileSpawnTestRig.{method} not found");
            return mi.Invoke(null, args);
        }
    }
}
