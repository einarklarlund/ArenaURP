using NUnit.Framework;

namespace Arena.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="BotStateMachine"/>.
    ///
    /// Coverage: every documented state transition plus the two "stay" cases
    /// (no transition when conditions are not met).
    /// </summary>
    public sealed class BotStateMachineTests
    {
        private BotStateMachine sm;

        [SetUp]
        public void SetUp() => sm = new BotStateMachine();

        // Searching

        [Test]
        public void Searching_NoTarget_StaysSearching()
        {
            var state = sm.Evaluate(hasTarget: false, isLowHealth: false, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Searching, state);
        }

        [Test]
        public void Searching_TargetVisible_HealthyBot_TransitionsToChasing()
        {
            var state = sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Chasing, state);
        }

        [Test]
        public void Searching_TargetVisible_LowHealthBot_TransitionsToFleeing()
        {
            var state = sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Fleeing, state);
        }

        // Chasing

        [Test]
        public void Chasing_BotBecomesLowHealth_TransitionsToFleeing()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false); // enter Chasing
            var state = sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Fleeing, state);
        }

        [Test]
        public void Chasing_NoTargetNotArrived_StaysChasing()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false); // enter Chasing
            var state = sm.Evaluate(hasTarget: false, isLowHealth: false, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Chasing, state);
        }

        [Test]
        public void Chasing_NoTargetAndArrived_TransitionsToSearching()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false); // enter Chasing
            var state = sm.Evaluate(hasTarget: false, isLowHealth: false, isAtDestination: true);
            Assert.AreEqual(BotStateMachine.BotState.Searching, state);
        }

        // Fleeing

        [Test]
        public void Fleeing_HealthRestored_TargetStillVisible_TransitionsToChasing()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false); // enter Fleeing
            var state = sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Chasing, state);
        }

        [Test]
        public void Fleeing_NoTargetAndArrived_TransitionsToSearching()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false); // enter Fleeing
            var state = sm.Evaluate(hasTarget: false, isLowHealth: true, isAtDestination: true);
            Assert.AreEqual(BotStateMachine.BotState.Searching, state);
        }

        [Test]
        public void Fleeing_LowHealthTargetVisible_StaysFleeing()
        {
            sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false); // enter Fleeing
            var state = sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false);
            Assert.AreEqual(BotStateMachine.BotState.Fleeing, state);
        }

        // Multi-step

        [Test]
        public void FullCycle_Search_Chase_Flee_Search()
        {
            // Start searching
            Assert.AreEqual(BotStateMachine.BotState.Searching,
                sm.Evaluate(hasTarget: false, isLowHealth: false, isAtDestination: false));

            // Spot a target while healthy, transition to chase
            Assert.AreEqual(BotStateMachine.BotState.Chasing,
                sm.Evaluate(hasTarget: true, isLowHealth: false, isAtDestination: false));

            // Take damage, transition to flee
            Assert.AreEqual(BotStateMachine.BotState.Fleeing,
                sm.Evaluate(hasTarget: true, isLowHealth: true, isAtDestination: false));

            // Reach cover, target lost, transition to search
            Assert.AreEqual(BotStateMachine.BotState.Searching,
                sm.Evaluate(hasTarget: false, isLowHealth: true, isAtDestination: true));
        }
    }
}
