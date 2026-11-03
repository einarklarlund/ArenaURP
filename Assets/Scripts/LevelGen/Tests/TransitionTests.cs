using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class TransitionTests
    {
        [Test]
        public void PlotGraph_IsSoundAfterTheStage([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19)] int seed)
        {
            var s = LevelGenSettings.Default.Sanitized();
            var partition = PartitionStage.Build(s, (ulong)seed, out _);
            var targets = Targets(s);
            var plan = TransitionStage.Build(partition, targets, s, (ulong)seed);
            if (plan.Failure != null)
            {
                CollectionAssert.IsNotEmpty(TransitionStage.FailingPlots(plan, partition.Polygons.Count),
                    "a stage that gives up has a plot it couldn't join");
                return;
            }
            CollectionAssert.IsEmpty(TransitionStage.FailingPlots(plan, partition.Polygons.Count));

            float maxGrade = Mathf.Tan(s.TransitionSizes.RampMaxSlopeDegrees * Mathf.Deg2Rad) + 1e-4f;
            for (int i = 0; i < plan.Ground.Length; i++)
            {
                Assert.LessOrEqual(plan.Ground[i].Slope.magnitude, maxGrade);
                if (plan.BuildingPlots.Contains(i)) Assert.IsTrue(plan.Ground[i].Flat, $"building plot {i}");
            }
        }

        [Test]
        public void Segments_CoverEachEdgeAndAreLongEnough([Values(0, 1, 2, 3, 4)] int seed)
        {
            var s = LevelGenSettings.Default.Sanitized();
            var partition = PartitionStage.Build(s, (ulong)seed, out _);
            var plan = TransitionStage.Build(partition, Targets(s), s, (ulong)seed);
            float minLength = s.Buildings.DoorWidth + 2f;
            foreach (var edge in partition.Edges)
            {
                var segments = plan.Segments.FindAll(g => g.PlotA == edge.A && g.PlotB == edge.B);
                Assert.That(segments.Count, Is.InRange(1, 3));
                float total = 0f;
                foreach (var g in segments)
                {
                    total += g.Length;
                    if (segments.Count > 1) Assert.GreaterOrEqual(g.Length, minLength - 1e-3f);
                }
                Assert.AreEqual(edge.Length, total, 1e-3f);
                Assert.AreEqual(edge.From, segments[0].From);
                Assert.AreEqual(edge.To, segments[^1].To);
            }
        }

        [Test]
        public void TwoPlots_AreJoinedByAtMostOneSlope([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int seed)
        {
            var s = LevelGenSettings.Default;
            s.TransitionWeights.Ramp = 10f;
            s.TransitionWeights.Bank = 10f;
            var level = LevelGenerator.Generate(s, (ulong)seed).Level;
            var pairs = new HashSet<(int, int)>();
            foreach (var t in level.Transitions)
                if (TransitionStage.IsSlope(t.Type))
                    Assert.IsTrue(pairs.Add((t.PlotA, t.PlotB)), $"plots {t.PlotA} and {t.PlotB} have a second ramp or bank");
            Assert.Greater(pairs.Count, 0);
        }

        [Test]
        public void FailingPlots_FindsOneWayAndStrandedPlots()
        {
            SegmentPlan Segment(int a, int b, TransitionType type, float delta) => new()
            {
                PlotA = a, PlotB = b, Type = type, DeltaFrom = delta, DeltaTo = delta, Lower = delta >= 0f ? b : a,
            };
            var plan = new PlotPlan();
            plan.Segments.Add(Segment(0, 1, TransitionType.Flat, 0f));
            plan.Segments.Add(Segment(1, 2, TransitionType.Cliff, 3f));
            CollectionAssert.AreEquivalent(new[] { 2 }, TransitionStage.FailingPlots(plan, 3), "2 can be dropped into, not left");

            plan.Segments[1].Type = TransitionType.Blocked;
            CollectionAssert.AreEquivalent(new[] { 2 }, TransitionStage.FailingPlots(plan, 3));

            plan.Segments[1].Type = TransitionType.Ramp;
            CollectionAssert.IsEmpty(TransitionStage.FailingPlots(plan, 3));
        }

        internal static PlotTargets[] Targets(in LevelGenSettings s)
        {
            var targets = new PlotTargets[s.PlotCount];
            for (int i = 0; i < targets.Length; i++)
                targets[i] = new PlotTargets { ElevationRange = s.ElevationRange.Target, ElevationFrequency = s.ElevationFrequency.Target };
            return targets;
        }
    }
}
