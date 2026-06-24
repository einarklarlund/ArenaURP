using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// Tier-A match-layer test: the pregame ready-check -> countdown wiring, server-authoritative.
    ///
    /// Replaces the manual "everyone readies up and the match counts down" playtest. Drives the real
    /// MatchMediator wiring loaded from the prefab:
    ///   NetworkPlayerManager registers a (human) player -> MatchMediator.ServerHandlePlayerRegistered
    ///   subscribes the player's IsReady.OnChange -> setting IsReady fires
    ///   MatchMediator.ServerHandleIsReadyChanged -> all humans ready -> MatchFlowManager
    ///   .EnterPregameCountdown flips State to PregameCountdown and starts the countdown.
    ///
    /// Stops before the countdown reaches During, so no pawns are spawned: this keeps the slice
    /// pawn-free (pawn spawning is covered by the death/respawn test against stripped test pawns).
    /// All Assembly-CSharp access is via MatchTestRig by reflection.
    /// </summary>
    public sealed class MatchLifecycleTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        [UnityTest]
        public IEnumerator All_humans_ready_advances_Pregame_to_PregameCountdown()
        {
            yield return new WaitForSeconds(0.2f); // let OnStartServer wire the managers

            Assert.IsTrue((bool)Rig("ManagersPresent"));
            Assert.AreEqual("Pregame", (string)Rig("MatchState"), "match starts in Pregame");

            // Register a single human player. Registration alone must NOT start the countdown.
            var human = Rig("RegisterPlayer", false);
            Assert.IsNotNull(human);
            Assert.IsFalse((bool)Rig("IsBot", human), "registered as a human, not a bot");
            yield return null;
            Assert.AreEqual("Pregame", (string)Rig("MatchState"),
                "registering a player should not by itself leave Pregame");

            // Ready up. With every human ready, the mediator's ready-check should start the countdown.
            Rig("SetReady", human, true);
            yield return null;

            Assert.AreEqual("PregameCountdown", (string)Rig("MatchState"),
                "when all humans are ready, MatchMediator should enter PregameCountdown. A wrong state " +
                "here means the IsReady.OnChange -> ServerHandleIsReadyChanged -> EnterPregameCountdown " +
                "wiring is broken.");
            Assert.Greater((int)Rig("CountdownTime"), 0,
                "entering the countdown should project a positive CountdownTime to the SyncVar HUD reads");
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
