using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class ValidatorFixtureTests
    {
        [Test]
        public void PitWithOnlyDropsIn_Fails()
        {
            // Floor at 3 drops into a pit at 0: within maxSafeDrop, but above jumpHeight, so there's no way out.
            var report = MetricFixtureTests.RaisedPlotWithPit(floorHeight: 3f).Measure();
            var noStuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsFalse(noStuck.Passed, noStuck.Detail);

            int pitFloor = Fixture.TopSurfaceAt(report, 15f, 15f);
            Assert.IsTrue(report.Debug.Reachability.InR[pitFloor]);
            Assert.IsTrue(report.Debug.Reachability.Sink[pitFloor]);
        }

        [Test]
        public void PitShallowEnoughToJumpOutOf_Passes()
        {
            var report = MetricFixtureTests.RaisedPlotWithPit(floorHeight: 1f).Measure();
            var noStuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsTrue(noStuck.Passed, noStuck.Detail);
        }

        [Test]
        public void UnreachablePillarTop_IsAllowed()
        {
            var report = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Box(PrimitiveRole.Pillar, 14f, 14f, 15f, 15f, 0f, 1.5f)
                .Measure();
            var noStuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsTrue(noStuck.Passed, noStuck.Detail);

            int pillarTop = Fixture.TopSurfaceAt(report, 14.5f, 14.5f);
            Assert.AreEqual(1.5f, report.Debug.Surfaces[pillarTop].Height);
            Assert.IsFalse(report.Debug.Reachability.InR[pillarTop]);
        }

        [Test]
        public void PlotWithOnlyOneWayTransitions_Fails()
        {
            var fixture = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Plot(1, 30, 10, 40, 20)
                .Transition(0, TransitionType.Cliff, 0, 1)
                .Transition(1, TransitionType.Blocked, 0, 1);
            var result = Validator.EveryPlotHasTwoWayTransition(fixture.Level);
            Assert.IsFalse(result.Passed);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, result.Items);

            fixture.Transition(2, TransitionType.Bank, 0, 1);
            Assert.IsTrue(Validator.EveryPlotHasTwoWayTransition(fixture.Level).Passed);
        }

        [Test]
        public void UnreachableTransitionSurface_Fails()
        {
            var fixture = new Fixture().Plot(0, 10, 10, 20, 20).Transition(0, TransitionType.Ramp, 0, 0);
            fixture.Level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Ramp, Shape = PrimitiveShape.Box,
                Position = new Vector3(15f, 3f, 15f), Size = new Vector3(2f, 0.3f, 2f),
                OwnerKind = OwnerKind.Transition, OwnerId = 0,
            });
            var noStuck = Fixture.Constraint(fixture.Measure(), Constraint.NoOneGetsStuck);
            Assert.IsFalse(noStuck.Passed);
            StringAssert.Contains("transition surfaces not reachable: 0", noStuck.Detail);
        }

        [Test]
        public void LowBridge_FailsHeadroom()
        {
            var report = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Box(PrimitiveRole.Platform, 14f, 10f, 16f, 20f, 1.5f, 1.8f)
                .Measure();
            Assert.IsFalse(Fixture.Constraint(report, Constraint.HeadroomAndSizes).Passed);
        }
    }
}
