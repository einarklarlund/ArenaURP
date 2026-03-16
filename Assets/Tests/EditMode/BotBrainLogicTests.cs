using NUnit.Framework;
using UnityEngine;

namespace Arena.Tests
{
    /// <summary>
    /// EditMode unit tests for BotBrainLogic. Because the logic class is pure C#
    /// with no MonoBehaviour or NavMesh dependencies, all behavioral decisions can
    /// be verified by constructing snapshots and asserting the returned BotDecision.
    /// </summary>
    public sealed class BotBrainLogicTests
    {
        private BotBrainLogic logic;

        [SetUp]
        public void SetUp()
        {
            logic = new BotBrainLogic();
            logic.PreferredCombatRange = 10f;
        }

        // Helper to build a default snapshot with common defaults.
        private BotWorldSnapshot MakeSnapshot(
            bool hasTarget = false,
            float healthFraction = 1f,
            bool isAtDestination = false,
            Vector3? selfPos = null,
            Vector3? targetPos = null,
            Vector3? lastKnownPos = null,
            Vector3? currentDest = null)
        {
            return new BotWorldSnapshot
            {
                SelfPosition = selfPos ?? Vector3.zero,
                HealthFraction = healthFraction,
                HasVisibleTarget = hasTarget,
                TargetPosition = targetPos ?? new Vector3(0f, 0f, 30f),
                LastKnownTargetPosition = lastKnownPos ?? new Vector3(0f, 0f, 30f),
                IsAtDestination = isAtDestination,
                CurrentDestination = currentDest ?? new Vector3(10f, 0f, 10f),
            };
        }

        // Searching

        [Test]
        public void Searching_FirstEvaluation_RequestsSearchWaypoint()
        {
            var decision = logic.Evaluate(MakeSnapshot());
            Assert.AreEqual(NavigationIntent.FindSearchWaypoint, decision.Navigation);
        }

        [Test]
        public void Searching_ArrivedAtWaypoint_RequestsNewWaypoint()
        {
            logic.Evaluate(MakeSnapshot()); // first call, requests waypoint
            var decision = logic.Evaluate(MakeSnapshot(isAtDestination: true));
            Assert.AreEqual(NavigationIntent.FindSearchWaypoint, decision.Navigation);
        }

        [Test]
        public void Searching_NotAtDestination_KeepsCurrent()
        {
            logic.Evaluate(MakeSnapshot()); // first call
            var decision = logic.Evaluate(MakeSnapshot(isAtDestination: false));
            Assert.AreEqual(NavigationIntent.KeepCurrent, decision.Navigation);
        }

        [Test]
        public void Searching_NeverFires()
        {
            var decision = logic.Evaluate(MakeSnapshot());
            Assert.IsFalse(decision.Fire);
        }

        [Test]
        public void Searching_NoLookTarget()
        {
            var decision = logic.Evaluate(MakeSnapshot());
            Assert.IsNull(decision.LookTarget);
        }

        // Chasing (visible target)

        [Test]
        public void Chasing_TargetFarAway_MovesToTarget()
        {
            Vector3 farTarget = new Vector3(0f, 0f, 30f);
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, targetPos: farTarget));

            Assert.AreEqual(NavigationIntent.MoveToPosition, decision.Navigation);
            Assert.AreEqual(farTarget, decision.MovePosition);
        }

        [Test]
        public void Chasing_TargetWithinCombatRange_KeepsCurrent()
        {
            Vector3 closeTarget = new Vector3(0f, 0f, 5f);
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, targetPos: closeTarget));

            Assert.AreEqual(NavigationIntent.KeepCurrent, decision.Navigation);
        }

        [Test]
        public void Chasing_TargetVisible_LooksAtTarget()
        {
            Vector3 target = new Vector3(0f, 0f, 20f);
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, targetPos: target));

            Assert.AreEqual(target, decision.LookTarget);
        }

        [Test]
        public void Chasing_TargetVisible_Fires()
        {
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true));
            Assert.IsTrue(decision.Fire);
        }

        // Chasing (lost target)

        private BotDecision EnterChasingThenLoseTarget(Vector3 lastKnown)
        {
            // First frame: spot target, enter chasing.
            logic.Evaluate(MakeSnapshot(hasTarget: true));
            // Second frame: target lost, still in chasing (not yet at destination).
            return logic.Evaluate(MakeSnapshot(hasTarget: false, lastKnownPos: lastKnown));
        }

        [Test]
        public void Chasing_LostTarget_MovesToLastKnownPosition()
        {
            Vector3 lastKnown = new Vector3(5f, 0f, 15f);
            var decision = EnterChasingThenLoseTarget(lastKnown);

            Assert.AreEqual(NavigationIntent.MoveToPosition, decision.Navigation);
            Assert.AreEqual(lastKnown, decision.MovePosition);
        }

        [Test]
        public void Chasing_LostTarget_LooksAtLastKnownPosition()
        {
            Vector3 lastKnown = new Vector3(5f, 0f, 15f);
            var decision = EnterChasingThenLoseTarget(lastKnown);

            Assert.AreEqual(lastKnown, decision.LookTarget);
        }

        [Test]
        public void Chasing_LostTarget_DoesNotFire()
        {
            var decision = EnterChasingThenLoseTarget(new Vector3(5f, 0f, 15f));
            Assert.IsFalse(decision.Fire);
        }

        // Fleeing

        private BotDecision EnterFleeing(
            bool hasTarget = true,
            Vector3? targetPos = null,
            Vector3? currentDest = null)
        {
            // Spot target while at low health to transition from Searching to Fleeing.
            return logic.Evaluate(MakeSnapshot(
                hasTarget: hasTarget,
                healthFraction: 0.3f,
                targetPos: targetPos,
                currentDest: currentDest));
        }

        [Test]
        public void Fleeing_OnEntry_RequestsCoverPosition()
        {
            var decision = EnterFleeing();
            Assert.AreEqual(NavigationIntent.FindCoverPosition, decision.Navigation);
        }

        [Test]
        public void Fleeing_AfterEntry_KeepsCurrent()
        {
            EnterFleeing(); // enter fleeing
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, healthFraction: 0.3f));
            Assert.AreEqual(NavigationIntent.KeepCurrent, decision.Navigation);
        }

        [Test]
        public void Fleeing_TargetVisible_Fires()
        {
            var decision = EnterFleeing(hasTarget: true);
            Assert.IsTrue(decision.Fire);
        }

        [Test]
        public void Fleeing_TargetNotVisible_DoesNotFire()
        {
            // Enter fleeing with target visible, then lose sight.
            EnterFleeing(hasTarget: true);
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: false, healthFraction: 0.3f));
            Assert.IsFalse(decision.Fire);
        }

        [Test]
        public void Fleeing_TargetVisible_LooksAtTarget()
        {
            Vector3 target = new Vector3(10f, 0f, 10f);
            var decision = EnterFleeing(hasTarget: true, targetPos: target);
            Assert.AreEqual(target, decision.LookTarget);
        }

        [Test]
        public void Fleeing_TargetNotVisible_LooksAtDestination()
        {
            Vector3 dest = new Vector3(20f, 0f, 5f);
            EnterFleeing(hasTarget: true, currentDest: dest);
            var decision = logic.Evaluate(MakeSnapshot(
                hasTarget: false, healthFraction: 0.3f, currentDest: dest));
            Assert.AreEqual(dest, decision.LookTarget);
        }

        // Cross-state transitions

        [Test]
        public void Searching_SpotsTarget_SwitchesToChasingDecision()
        {
            // Searching
            var d1 = logic.Evaluate(MakeSnapshot());
            Assert.AreEqual(NavigationIntent.FindSearchWaypoint, d1.Navigation);
            Assert.IsFalse(d1.Fire);

            // Spots target while healthy
            var d2 = logic.Evaluate(MakeSnapshot(hasTarget: true));
            Assert.IsTrue(d2.Fire);
            Assert.IsNotNull(d2.LookTarget);
        }

        [Test]
        public void Chasing_TakesDamage_SwitchesToFleeingDecision()
        {
            // Enter chasing
            logic.Evaluate(MakeSnapshot(hasTarget: true));

            // Take damage (low health) with target still visible
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, healthFraction: 0.3f));
            Assert.AreEqual(NavigationIntent.FindCoverPosition, decision.Navigation);
        }

        [Test]
        public void Fleeing_Heals_SwitchesToChasingDecision()
        {
            // Enter fleeing
            EnterFleeing(hasTarget: true);

            // Health restored, target still visible
            var decision = logic.Evaluate(MakeSnapshot(hasTarget: true, healthFraction: 0.8f));
            Assert.IsTrue(decision.Fire);
            Assert.AreNotEqual(NavigationIntent.FindCoverPosition, decision.Navigation);
        }

        [Test]
        public void Fleeing_LostTargetAndArrived_ReturnsToSearching()
        {
            EnterFleeing(hasTarget: true);

            // Lost target and arrived at cover
            var decision = logic.Evaluate(MakeSnapshot(
                hasTarget: false, healthFraction: 0.3f, isAtDestination: true));

            // Back to searching: no fire, no look target, requests waypoint
            Assert.IsFalse(decision.Fire);
            Assert.IsNull(decision.LookTarget);
            Assert.AreEqual(NavigationIntent.FindSearchWaypoint, decision.Navigation);
        }
    }
}
