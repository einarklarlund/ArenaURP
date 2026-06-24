using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-A match-layer test: the death -> respawn chain, server-authoritative, end to end.
    ///
    /// Replaces the manual "die and time the respawn" playtest. Drives the real managers + the real
    /// Bot Pawn prefab the game ships (spawned at the prefab's authored spawn points, on the scene's
    /// pre-baked NavMesh):
    ///   Pawn.TakeDamage -> Health hits 0 -> Pawn.OnDeath -> PawnManager despawns the pawn + fires
    ///   OnPawnKilled -> MatchMediator.ServerHandlePawnKilled records the kill and (while During)
    ///   calls PawnManager.ServerStartRespawnTimerFor, projecting NetworkPlayer.RespawnTimeEnd ->
    ///   PawnManager.Update ticks the RespawnProcessor -> at zero it SpawnPawnForPlayer again.
    ///
    /// Players are registered through MatchTestRig (robust direct registration) and the match is put
    /// into During so the real spawn + respawn path runs. RespawnDelay is shortened so the test is fast.
    /// </summary>
    public sealed class DeathRespawnTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        const float RespawnDelay = 0.5f;

        [UnityTest]
        public IEnumerator Killing_a_pawn_despawns_it_then_respawns_the_player_after_the_delay()
        {
            yield return new WaitForSeconds(0.2f); // OnStartServer wiring

            Assert.IsTrue((bool)Rig("ManagersPresent"));
            Rig("SetRespawnDelay", RespawnDelay);

            // Start the match, then register two bots. While During, registration spawns each bot's
            // real Bot Pawn at a spawn point (round-robin, far apart so they don't shoot each other).
            Rig("StartMatch");
            Assert.AreEqual("During", (string)Rig("MatchState"));

            var victim = Rig("RegisterPlayer", true);
            var attacker = Rig("RegisterPlayer", true);
            Assert.IsNotNull(victim);
            Assert.IsNotNull(attacker);

            // Let the network spawns settle.
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(2, (int)Rig("ActivePawnCount"),
                "registering two players while During should spawn two real pawns (if 0/1, the Bot Pawn " +
                "prefab failed to spawn headless — check the console for NREs from its subsystems)");
            var victimPawn = Rig("ControlledPawn", victim);
            Assert.IsNotNull(victimPawn, "victim should control a freshly spawned pawn");
            Assert.Greater((int)Rig("Health", victimPawn), 0, "fresh pawn starts with positive health");

            Rig("BeginCapture");

            // Kill the victim's pawn, attributed to the attacker.
            Rig("KillPawn", victimPawn, attacker);
            yield return null; // let OnDeath -> despawn -> OnPawnKilled -> respawn-timer run

            Assert.AreEqual(1, (int)Rig("PawnKilledCount"),
                "killing the pawn should fire PawnManager.OnPawnKilled exactly once");
            Assert.IsNull(Rig("ControlledPawn", victim),
                "the killed pawn should be despawned and the player's ControlledPawn cleared");
            Assert.AreEqual(1, (int)Rig("ActivePawnCount"), "one pawn remains (the attacker's)");
            Assert.Greater((double)Rig("RespawnTimeEnd", victim), 0.0,
                "ServerStartRespawnTimerFor should project a positive RespawnTimeEnd for the dead player");

            // Wait out the respawn delay; PawnManager.Update should re-spawn the player.
            float t = 0f;
            while (Rig("ControlledPawn", victim) == null && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Assert.IsNotNull(Rig("ControlledPawn", victim),
                $"the player should respawn within ~{RespawnDelay}s; a null ControlledPawn after {t:F2}s " +
                "means PawnManager.Update never ticked the RespawnProcessor to completion");
            Assert.AreEqual(2, (int)Rig("ActivePawnCount"), "the respawn should restore two live pawns");
            Assert.AreEqual(-1.0, (double)Rig("RespawnTimeEnd", victim),
                "RespawnTimeEnd should reset to -1 once the player has respawned");
        }

        [UnityTearDown]
        public IEnumerator MatchTearDown()
        {
            Rig("EndCapture");
            yield return null;
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
