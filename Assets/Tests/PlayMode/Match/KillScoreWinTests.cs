using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-A match-layer test: the kill -> score -> win chain, server-authoritative, headless.
    ///
    /// Replaces a tedious manual playtest ("rack up kills, confirm the match ends when someone
    /// hits the cap"). Drives the REAL managers loaded from the MatchMediator prefab:
    ///   DeathmatchManager.ServerRecordKill increments Score and tracks the leader ->
    ///   DeathmatchManager.Update ticks the MatchTimerProcessor -> on reaching KillsToWin it fires
    ///   OnGameModeEnded -> MatchMediator.ServerHandleGameModeEnded resets scores and pushes the
    ///   flow into PostgameCountdown.
    ///
    /// Needs no pawns or NavMesh: players are registered via the real BotSpawner.SpawnBot path and
    /// kills are recorded directly, so this stays a clean server-only slice. All Assembly-CSharp
    /// access goes through MatchTestRig by reflection (this asmdef can't reference Assembly-CSharp).
    /// </summary>
    public sealed class KillScoreWinTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        const int KillsToWin = 3;

        [UnityTest]
        public IEnumerator Recording_kills_increments_score_and_ends_match_at_the_cap()
        {
            // Let the scene NetworkObjects' OnStartServer run (MatchMediator wires the managers
            // and calls NetworkPlayerManager.Initialize here).
            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue((bool)Rig("ManagersPresent"),
                "TestMatchMediator scene should load the MatchMediator prefab's four managers");
            Assert.AreEqual("Pregame", (string)Rig("MatchState"), "match starts in Pregame");
            Assert.AreEqual(0, (int)Rig("ActivePlayerCount"), "no players registered yet");

            Rig("BeginCapture");
            Rig("SetMatchDuration", 600f);   // don't let the time limit end the match first
            Rig("SetKillsToWin", (float)KillsToWin);

            // Register two players (spawned + added to ActivePlayers via the manager's own
            // registration). Match is still Pregame, so no pawns spawn (keeps this slice pawn-free).
            var killer = Rig("RegisterPlayer", true);
            var victim = Rig("RegisterPlayer", true);
            Assert.IsNotNull(killer, "RegisterPlayer should spawn + register a player");
            Assert.IsNotNull(victim);
            Assert.AreEqual(2, (int)Rig("ActivePlayerCount"), "two players registered");

            // Start the deathmatch timer, then score kills for the killer.
            Rig("BeginGame");
            for (int i = 1; i <= KillsToWin; i++)
            {
                Rig("RecordKill", killer, victim);
                Assert.AreEqual(i, (int)Rig("Score", killer),
                    $"each recorded kill should increment the killer's score (after kill #{i})");
            }

            Assert.AreEqual(0, (int)Rig("GameModeEndedCount"),
                "precondition: the match has not ended yet (event fires on the next Update tick)");

            // DeathmatchManager.Update needs a tick to observe LeadingScore >= KillsToWin and fire.
            float t = 0f;
            while ((int)Rig("GameModeEndedCount") == 0 && t < 2f)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(1, (int)Rig("GameModeEndedCount"),
                "reaching the kills-to-win cap must fire OnGameModeEnded exactly once");
            Assert.AreSame(killer, Rig("LastWinner"),
                "the leading scorer should be reported as the winner");

            // MatchMediator.ServerHandleGameModeEnded must have reacted: scores reset, flow advanced.
            Assert.AreEqual(0, (int)Rig("Score", killer),
                "ServerHandleGameModeEnded should reset every player's score on match end");
            Assert.AreEqual("PostgameCountdown", (string)Rig("MatchState"),
                "ending the match should push the flow into PostgameCountdown via the mediator");
        }

        [UnityTearDown]
        public IEnumerator MatchTearDown()
        {
            // Unsubscribe the rig's latched handlers before the base fixture destroys the managers.
            Rig("EndCapture");
            yield return null;
        }

        // --- MatchTestRig reflection bridge (Assembly-CSharp is unreferenceable here) ---

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
