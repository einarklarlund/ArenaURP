using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-A "soak": spin up a small bot deathmatch with the REAL Bot Pawn prefab and let the whole
    /// server stack run for several seconds, then assert invariants. This is automated playtesting ???
    /// it replaces the manual "start a bot match and watch nothing explode" ritual.
    ///
    /// What it exercises at once: BotSpawner-style registration, MatchMediator wiring, PawnManager spawn
    /// + respawn, and every server component on the real Bot Pawn (BotBrain, NavMeshAgent on the scene's
    /// pre-baked NavMesh, PawnSensor, PawnMovement, PawnWeapon, knockback, temperature) ticking together.
    ///
    /// The strongest invariant is implicit: the Unity Test Framework fails the test on ANY uncaught error
    /// log, so a NullReferenceException from any bot subsystem during the run fails this automatically.
    /// On top of that we assert: pawns spawn, scripted kills under load produce respawns, health never
    /// goes negative, and the match stays live.
    ///
    /// EXPECTED CHURN (warnings, not errors ??? do NOT chase or globally suppress these): the deliberate
    /// kill/respawn loop despawns Bot Pawns mid-tick, so FishNet's own NetworkTransform fires one last
    /// post-tick "Action cannot be completed as object Bot Pawn(Clone) [Id N] is not spawned." for the
    /// tick after a pawn despawns (the queued transform RPC is simply dropped), and a freshly respawned
    /// pawn can land a frame off the baked NavMesh ("Failed to create agent because it is not close
    /// enough to the NavMesh" ??? BotBrain already guards every agent read with isOnNavMesh). Both are
    /// benign byproducts of the churn this test exists to stress, not assertions and not bugs. The
    /// meaningful contract is "no ERROR logs"; tolerate these warnings.
    /// </summary>
    public sealed class BotMatchSoakTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        const string RealBotPawn = "Assets/Prefabs/Player/Bot Pawn.prefab";
        const int BotCount = 3;
        const float RespawnDelay = 0.5f;
        const int KillRounds = 5;

        [UnityTest]
        public IEnumerator Bot_deathmatch_runs_clean_and_respawns_under_load()
        {
            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue((bool)Rig("ManagersPresent"));
            Rig("SetRespawnDelay", RespawnDelay);
            Rig("UseBotPawnPrefab", RealBotPawn); // real Bot Pawn, not the stripped TestPawn

            // Keep the match from ending mid-soak: we want sustained running, not a win condition.
            Rig("SetKillsToWin", 9999f);
            Rig("SetMatchDuration", 9999f);

            Rig("StartMatch");
            Assert.AreEqual("During", (string)Rig("MatchState"));

            var bots = new object[BotCount];
            for (int i = 0; i < BotCount; i++) bots[i] = Rig("RegisterPlayer", true);

            // Let the real bot pawns spawn + their NavMeshAgents settle onto the baked NavMesh.
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(BotCount, (int)Rig("ActivePawnCount"),
                $"all {BotCount} real Bot Pawns should spawn headless. Fewer means a subsystem on the " +
                "Bot Pawn NRE'd on spawn ??? check the console for the offending component.");
            Assert.IsFalse((bool)Rig("AnyPawnHealthNegative"));

            Rig("BeginCapture");

            // Soak: repeatedly kill a living bot (attributed to another) and let it respawn, while the
            // full bot stack keeps ticking. Each round spaces > RespawnDelay so victims come back.
            for (int round = 0; round < KillRounds; round++)
            {
                ScriptOneKill(bots);
                Assert.IsFalse((bool)Rig("AnyPawnHealthNegative"),
                    $"no pawn should ever have negative health (round {round})");
                yield return new WaitForSeconds(1.0f);
            }

            Assert.GreaterOrEqual((int)Rig("PawnKilledCount"), KillRounds,
                "every scripted kill should have driven the death -> OnPawnKilled chain");
            Assert.AreEqual("During", (string)Rig("MatchState"),
                "with the win caps raised, the match should still be running after the soak");

            // Let the final respawns complete (poll rather than a fixed wait).
            float t = 0f;
            while ((int)Rig("ActivePawnCount") < BotCount && t < 3f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.AreEqual(BotCount, (int)Rig("ActivePawnCount"),
                $"after the soak every bot should be respawned and alive again (waited {t:F2}s)");
            Assert.IsFalse((bool)Rig("AnyPawnHealthNegative"));
        }

        /// <summary>Kill the first bot that currently controls a pawn, attributed to a different bot.</summary>
        static void ScriptOneKill(object[] bots)
        {
            object victim = null, victimPawn = null, attacker = null;
            foreach (var b in bots)
            {
                var pawn = Rig("ControlledPawn", b);
                if (pawn != null) { victim = b; victimPawn = pawn; break; }
            }
            if (victimPawn == null) return; // everyone mid-respawn this instant; skip
            foreach (var b in bots)
                if (!ReferenceEquals(b, victim)) { attacker = b; break; }

            Rig("KillPawn", victimPawn, attacker);
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
