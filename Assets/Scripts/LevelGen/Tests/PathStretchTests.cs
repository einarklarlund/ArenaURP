using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PathStretchTests
    {
        [Test]
        public void EmptyFlatLevel_Scores1()
        {
            var report = new Fixture().Plot(0, 0f, 0f, 25f, 50f).Plot(1, 25f, 0f, 50f, 50f).Measure();
            var stretch = report.PathStretch;
            Assert.Greater(stretch.Pairs, 1000);
            Assert.AreEqual(0, stretch.Unreachable);
            Assert.AreEqual(1f, stretch.Mean, 1e-4f);
            Assert.AreEqual(1f, stretch.Worst, 1e-4f);
        }

        [Test]
        public void PairsInTheSamePlot_AreNotMeasured()
        {
            var report = new Fixture().Plot(0, 0f, 0f, 50f, 50f).Measure();
            Assert.Greater(report.PathStretch.Samples.Count, 0);
            Assert.AreEqual(0, report.PathStretch.Pairs);
            Assert.AreEqual(1f, report.PathStretch.Mean);
        }

        [Test]
        public void WallWithOneGap_ScoresTheKnownDetour()
        {
            // A wall one cell thick along z = 24.75 from x = 0 to 40 and 44 to 50: the only way across is the gap.
            var fixture = new Fixture()
                .Plot(0, 0f, 0f, 50f, 25f)
                .Plot(1, 0f, 25f, 50f, 50f)
                .Box(PrimitiveRole.Wall, 0f, 24.6f, 40f, 25.1f, 0f, 3f)
                .Box(PrimitiveRole.Wall, 44f, 24.6f, 50f, 25.1f, 0f, 3f);
            var report = fixture.Measure();
            int a = Fixture.TopSurfaceAt(report, 5.25f, 15.25f);
            int b = Fixture.TopSurfaceAt(report, 5.25f, 35.25f);
            float d = Distance(report, a, b);

            // Through the nearest gap cell (80, 49), in cells: (10, 30) -> (80, 49) -> (10, 70).
            float viaGap = PathStretch.Octile(70, 19, 0.5f) + PathStretch.Octile(70, 21, 0.5f);
            Assert.GreaterOrEqual(d, viaGap - 1e-3f);
            Assert.LessOrEqual(d, viaGap + 1f, "at most a corner's worth over the octile route through the gap");
            Assert.AreEqual(d, Distance(report, b, a), 1e-3f, "a wall is the same both ways");
            Assert.Greater(report.PathStretch.Mean, 1.2f);
            Assert.Greater(report.PathStretch.Worst, 3f);
            Assert.Greater(report.PathStretch.WorstPath.Count, 2);
        }

        [Test]
        public void Cliff_GivesAsymmetricDistances()
        {
            // A plateau at 2 north of z = 25: drop off it anywhere, climb it only by the ramp at its east end.
            var fixture = new Fixture()
                .Plot(0, 0f, 0f, 50f, 25f)
                .Plot(1, 0f, 25f, 50f, 50f, 2f)
                .Box(PrimitiveRole.Floor, 0f, 25f, 50f, 50f, 0f, 2f, plot: 1);
            fixture.Level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Ramp, Shape = PrimitiveShape.Ramp,
                Position = new Vector3(46.5f, 0f, 21.5f), Size = new Vector3(3f, 2f, 7f), OwnerKind = OwnerKind.Plot,
            });
            var report = fixture.Measure();
            int low = Fixture.TopSurfaceAt(report, 5.25f, 15.25f);
            int high = Fixture.TopSurfaceAt(report, 5.25f, 35.25f);
            float down = Distance(report, high, low), up = Distance(report, low, high);
            Assert.AreEqual(20f, down, 1e-3f, "straight off the edge");
            Assert.Greater(up, down + 50f, "round by the ramp");
        }

        [Test]
        public void BuildingWithSpine_AddsLessThanSolidBuilding()
        {
            float solid = BuildingAcrossTheLevel(spine: false).PathStretch.Mean;
            var report = BuildingAcrossTheLevel(spine: true);
            Assert.Less(report.PathStretch.Mean, solid);
            Assert.Greater(solid, 1.05f);

            // The lobby is routable but never a sample.
            foreach (int s in report.PathStretch.Samples)
            {
                var surface = report.Debug.Surfaces[s];
                float x = (surface.X + 0.5f) * 0.5f, z = (surface.Z + 0.5f) * 0.5f;
                Assert.IsFalse(x > 20f && x < 30f && z > 5f && z < 45f, $"sample at {x}, {z} is inside the building");
            }
        }

        [Test]
        public void DiagonalStep_DoesNotCutABlockedCorner()
        {
            // Pillars in cells (10, 11) and (11, 10) touch at a corner: (10, 10) -> (11, 11) must go round.
            var report = new Fixture()
                .Plot(0, 0f, 0f, 50f, 50f)
                .Box(PrimitiveRole.Pillar, 5f, 5.5f, 5.5f, 6f, 0f, 3f)
                .Box(PrimitiveRole.Pillar, 5.5f, 5f, 6f, 5.5f, 0f, 3f)
                .Measure();
            int a = Fixture.TopSurfaceAt(report, 5.25f, 5.25f);
            int b = Fixture.TopSurfaceAt(report, 5.75f, 5.75f);
            Assert.Greater(Distance(report, a, b), 1f);

            int open = Fixture.TopSurfaceAt(report, 20.25f, 20.25f);
            int diagonal = Fixture.TopSurfaceAt(report, 20.75f, 20.75f);
            Assert.AreEqual(0.5f * Mathf.Sqrt(2f), Distance(report, open, diagonal), 1e-4f);
        }

        private static GenerationReport BuildingAcrossTheLevel(bool spine)
        {
            // A 10 × 40 block in the middle; walking round it takes the strips at z < 5 and z > 45.
            var fixture = new Fixture().Plot(0, 0f, 0f, 25f, 50f).Plot(1, 25f, 0f, 50f, 50f);
            if (!spine)
            {
                AddBuilding(fixture, PrimitiveRole.Wall, 20f, 5f, 30f, 45f, 0f, 4f);
                return fixture.Measure();
            }
            AddBuilding(fixture, PrimitiveRole.Floor, 20f, 5f, 30f, 45f, -0.5f, 0.2f);
            AddBuilding(fixture, PrimitiveRole.Wall, 20f, 5f, 30f, 5.5f, 0f, 4f);
            AddBuilding(fixture, PrimitiveRole.Wall, 20f, 44.5f, 30f, 45f, 0f, 4f);
            // West and east walls, each with a door 2 wide at z 24..26.
            AddBuilding(fixture, PrimitiveRole.Wall, 20f, 5f, 20.5f, 24f, 0f, 4f);
            AddBuilding(fixture, PrimitiveRole.Wall, 20f, 26f, 20.5f, 45f, 0f, 4f);
            AddBuilding(fixture, PrimitiveRole.Wall, 29.5f, 5f, 30f, 24f, 0f, 4f);
            AddBuilding(fixture, PrimitiveRole.Wall, 29.5f, 26f, 30f, 45f, 0f, 4f);
            return fixture.Measure();
        }

        private static void AddBuilding(Fixture fixture, PrimitiveRole role, float x0, float z0, float x1, float z1,
            float y0, float y1)
        {
            fixture.Level.Primitives.Add(new Primitive
            {
                Role = role, Shape = PrimitiveShape.Box,
                Position = new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f), Size = new Vector3(x1 - x0, y1 - y0, z1 - z0),
                OwnerKind = OwnerKind.Building,
            });
        }

        private static float Distance(GenerationReport report, int from, int to)
        {
            var distance = new float[report.Debug.Surfaces.Count];
            report.Debug.Graph.Distances(from, distance);
            return distance[to];
        }
    }
}
