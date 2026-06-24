using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-B match-layer test: a player leaving mid-match must be fully cleaned up.
    ///
    /// Replaces the manual "someone rage-quits, does the server tidy up?" check. A disconnect is, on the
    /// server, just the player's NetworkObject despawning (OnStopServer -> NetworkPlayer.OnDespawn). This
    /// test drives that cascade on the single-host fixture and asserts BOTH halves of the cleanup:
    ///   - NetworkPlayerManager removes the player from ActivePlayers (OnDespawn -> ServerUnregisterPlayer);
    ///   - the player's Pawn is despawned (no orphaned pawn left controlled by a gone player).
    ///
    /// The pawn half is the load-bearing one: an orphaned pawn is invisible until it matters (a corpse
    /// that never leaves, a phantom target). A single-host test catches it without a second peer.
    /// </summary>
    public sealed class PlayerDisconnectTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        [UnityTest]
        public IEnumerator Player_leaving_mid_match_is_unregistered_and_its_pawn_despawned()
        {
            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue((bool)Rig("ManagersPresent"));

            // Live match with two players, each controlling a pawn.
            Rig("StartMatch");
            var leaver = Rig("RegisterPlayer", true);
            var stayer = Rig("RegisterPlayer", true);
            yield return new WaitForSeconds(0.2f);

            Assert.AreEqual(2, (int)Rig("ActivePlayerCount"), "two players registered");
            Assert.AreEqual(2, (int)Rig("ActivePawnCount"), "each player controls a pawn");
            Assert.IsNotNull(Rig("ControlledPawn", leaver), "the leaver controls a pawn before leaving");

            // Simulate the disconnect: despawn the leaver's NetworkObject.
            Rig("DespawnPlayer", leaver);
            yield return null; // OnStopServer -> OnDespawn cascade
            yield return null;

            Assert.IsFalse((bool)Rig("PlayerIsRegistered", leaver),
                "the leaver should be removed from NetworkPlayerManager.ActivePlayers on despawn");
            Assert.AreEqual(1, (int)Rig("ActivePlayerCount"), "only the staying player remains registered");

            Assert.AreEqual(1, (int)Rig("ActivePawnCount"),
                "the leaver's pawn must be despawned on disconnect — a remaining count of 2 means the " +
                "pawn was orphaned (player gone, pawn still alive).");
            Assert.IsNotNull(Rig("ControlledPawn", stayer), "the staying player keeps its pawn");
        }

        // --- MatchTestRig reflection bridge ---

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
