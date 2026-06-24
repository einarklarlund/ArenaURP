using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Arena.PlayTests
{
    /// <summary>
    /// MatchMediator is the central server-side wiring point that subscribes managers
    /// to each other in OnStartServer. A dropped subscription is silent and game-breaking,
    /// which makes the wiring exactly the kind of thing the integration-testing skill
    /// flags as worth a PlayMode test.
    ///
    /// This test verifies the matchFlowManager.State.OnChange -> MatchMediator ->
    /// DeathmatchManager.BeginGame chain. If any link in that chain breaks
    /// (subscription dropped, reference unwired, handler renamed), DeathmatchManager's
    /// timer will not start when the match state flips to During.
    ///
    /// All cross-system types (MatchFlowManager, DeathmatchManager, MatchState,
    /// SyncVar&lt;T&gt;) live in Assembly-CSharp which this asmdef can't reference,
    /// so all manager access is via reflection. Helpers below contain the ugliness.
    /// </summary>
    public sealed class MatchMediatorWiringTests : NetworkedPlayModeTestFixture
    {
        protected override string ScenePath
            => "Assets/Tests/PlayMode/Scenes/TestMatchMediator.unity";

        [UnityTest]
        public IEnumerator MatchState_During_starts_DeathmatchManager_timer_via_MatchMediator()
        {
            // The scene already contains MatchMediator + its four child managers and a
            // BotSpawner as scene NetworkObjects. FishNet auto-spawns them when the
            // server starts; we just give OnStartServer a moment to wire its subscriptions.
            yield return new WaitForSeconds(0.2f);

            var mfm = FindAnyByTypeName("MatchFlowManager");
            var dmm = FindAnyByTypeName("DeathmatchManager");
            Assert.IsNotNull(mfm, "TestMatchMediator scene should contain a MatchFlowManager");
            Assert.IsNotNull(dmm, "TestMatchMediator scene should contain a DeathmatchManager");

            Assert.IsFalse(
                ReadDeathmatchTimerRunning(dmm),
                "DeathmatchManager.timerState.IsRunning should be false before the match starts"
            );

            // Trigger: SyncVar<MatchState>.Value = MatchState.During.
            // MatchMediator subscribed to State.OnChange in OnStartServer.
            var matchStateType = T("MatchState");
            var during = Enum.Parse(matchStateType, "During");
            SetSyncVarValue(mfm, "State", during);

            // Give SyncVar OnChange a frame to propagate, plus FixedUpdate time.
            yield return new WaitForSeconds(0.1f);

            Assert.IsTrue(
                ReadDeathmatchTimerRunning(dmm),
                "DeathmatchManager.BeginGame should run via MatchMediator wiring when " +
                "MatchFlowManager.State -> During. If this fails, the State.OnChange " +
                "subscription is dropped or a manager SerializedField is unwired."
            );
        }

        // --- reflection helpers ---

        static Type T(string name) => Type.GetType(name + ", Assembly-CSharp");

        static UnityEngine.Object FindAnyByTypeName(string typeName)
            => UnityEngine.Object.FindAnyObjectByType(T(typeName));

        /// <summary>
        /// Reads <c>deathmatchManager.timerState.IsRunning</c> via reflection.
        /// timerState is a private MatchTimerState struct; we copy it out, then read the field.
        /// </summary>
        static bool ReadDeathmatchTimerRunning(UnityEngine.Object dmm)
        {
            var stateField = dmm.GetType().GetField(
                "timerState", BindingFlags.NonPublic | BindingFlags.Instance);
            var state = stateField.GetValue(dmm);
            var isRunningField = state.GetType().GetField("IsRunning");
            return (bool)isRunningField.GetValue(state);
        }

        /// <summary>
        /// Sets the <c>Value</c> of a <c>readonly SyncVar&lt;T&gt;</c> field on the target.
        /// Equivalent to <c>target.FIELD.Value = value</c> when you can't reference T directly.
        /// </summary>
        static void SetSyncVarValue(UnityEngine.Object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            var syncVar = field.GetValue(target);
            var valueProp = syncVar.GetType().GetProperty("Value");
            valueProp.SetValue(syncVar, value);
        }
    }
}
