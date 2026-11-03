using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class MetricFixtureTests
    {
        private const float Epsilon = 1e-5f;

        [Test]
        public void FlatFloor_ScoresElevationFrequency0()
        {
            var report = new Fixture().Plot(0, 10, 10, 20, 20).Measure();
            Assert.AreEqual(0f, Fixture.Plot0(report, PlotMetric.ElevationFrequency));
            Assert.AreEqual(0f, Fixture.Plot0(report, PlotMetric.ElevationRange));
        }

        [Test]
        public void RaisedPlatform_ProducesElevationRangeAndFrequency()
        {
            var report = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Box(PrimitiveRole.Platform, 12f, 12f, 16f, 16f, 0f, 1.2f)
                .Measure();
            Assert.AreEqual(1.2f / 12f, Fixture.Plot0(report, PlotMetric.ElevationRange), Epsilon);
            Assert.Greater(Fixture.Plot0(report, PlotMetric.ElevationFrequency), 0f);
        }

        [Test]
        public void LevelElevationRange_UsesTopLevelPlotsOnly()
        {
            var fixture = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Plot(1, 30, 30, 40, 40, baseHeight: 6f);
            Assert.AreEqual(0.5f, Metrics.LevelElevationRange(fixture.Level, 12f), Epsilon);

            fixture.Plot(2, 30, 30, 35, 35, baseHeight: 9f);
            fixture.Level.Plots[2].ParentId = 1;
            Assert.AreEqual(0.5f, Metrics.LevelElevationRange(fixture.Level, 12f), Epsilon);
        }

        [Test]
        public void Error_IsZeroWithinToleranceAndWeightedOutside()
        {
            Assert.AreEqual(0f, MetricResult.Of(0.53f, 0.5f, 0.05f, 2f).Error);
            Assert.AreEqual(2f * 0.15f, MetricResult.Of(0.7f, 0.5f, 0.05f, 2f).Error, Epsilon);
            Assert.AreEqual(2f * 0.15f, MetricResult.Of(0.3f, 0.5f, 0.05f, 2f).Error, Epsilon);
            // Continuous at the tolerance edge.
            Assert.AreEqual(0f, MetricResult.Of(0.5501f, 0.5f, 0.05f, 2f).Error, 1e-3f);
        }

        /// <summary>A 10 × 10 plot whose ground is raised to <paramref name="floorHeight"/>, with a 2 × 2 hole down to the ground.</summary>
        internal static Fixture RaisedPlotWithPit(float floorHeight)
        {
            return new Fixture()
                .Plot(0, 10, 10, 20, 20, floorHeight)
                .Box(PrimitiveRole.Floor, 10f, 10f, 20f, 14f, 0f, floorHeight)
                .Box(PrimitiveRole.Floor, 10f, 16f, 20f, 20f, 0f, floorHeight)
                .Box(PrimitiveRole.Floor, 10f, 14f, 14f, 16f, 0f, floorHeight)
                .Box(PrimitiveRole.Floor, 16f, 14f, 20f, 16f, 0f, floorHeight);
        }
    }
}
