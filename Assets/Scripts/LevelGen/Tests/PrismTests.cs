using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PrismTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void Prism_CoversExactlyTheCellsWhoseCentresAreInside()
        {
            // A diamond, |dx| + |dz| < 5 around a centre placed so that no cell centre lies on its edges.
            var c = new Vector2(20.07f, 20.13f);
            var fixture = new Fixture().Prism(PrimitiveRole.Platform, 3f, 4.5f, Vector2.zero,
                c + new Vector2(0f, -5f), c + new Vector2(5f, 0f), c + new Vector2(0f, 5f), c + new Vector2(-5f, 0f));
            var grid = new SpanGrid(Fixture.Size, Fixture.Size, 0.5f);
            var cells = new List<CoveredCell>();
            Rasterizer.CoveredCells(fixture.Level.Primitives[1], fixture.Level, grid, cells);

            int expected = 0;
            for (int z = 0; z < grid.CellsZ; z++)
            for (int x = 0; x < grid.CellsX; x++)
                if (Mathf.Abs(grid.CellCenterX(x) - c.x) + Mathf.Abs(grid.CellCenterZ(z) - c.y) < 5f) expected++;
            Assert.AreEqual(expected, cells.Count);
            foreach (var cell in cells)
            {
                Assert.Less(Mathf.Abs(grid.CellCenterX(cell.X) - c.x) + Mathf.Abs(grid.CellCenterZ(cell.Z) - c.y), 5f);
                Assert.AreEqual(4.5f, cell.Top, Epsilon);
            }

            var column = Rasterizer.Rasterize(fixture.Level, 0.5f).GetColumn(40, 40);
            Assert.AreEqual(2, column.Count, "ground, then the prism above a gap");
            Assert.AreEqual(3f, column[1].Bottom, Epsilon);
            Assert.AreEqual(4.5f, column[1].Top, Epsilon);
        }

        [Test]
        public void SlopedPrism_TopIsThePlaneAtEachCellCentre()
        {
            var outline = new List<Vector2> { new(10f, 10f), new(30f, 12f), new(26f, 30f), new(12f, 24f) };
            PolygonTests.Rotate(outline, 21f);
            var slope = new Vector2(0.2f, -0.1f);
            var fixture = new Fixture().Prism(PrimitiveRole.Floor, -0.5f, 4f, slope, outline.ToArray());
            var prism = fixture.Level.Primitives[1];
            var centroid = Polygon.Centroid(outline);
            var grid = new SpanGrid(Fixture.Size, Fixture.Size, 0.5f);
            var cells = new List<CoveredCell>();
            Rasterizer.CoveredCells(prism, fixture.Level, grid, cells);

            Assert.AreEqual(Polygon.Area(outline) / 0.25f, cells.Count, 0.05f * cells.Count);
            foreach (var cell in cells)
            {
                var p = new Vector2(grid.CellCenterX(cell.X), grid.CellCenterZ(cell.Z));
                Assert.IsTrue(Polygon.Contains(outline, p));
                Assert.AreEqual(4f + Vector2.Dot(slope, p - centroid), cell.Top, Epsilon);
            }
        }

        [Test]
        public void PlotsCutByChords_CoverEveryCellExactlyOnce()
        {
            // Chords at 45° through cell centres put many centres exactly on shared edges.
            var pieces = new List<List<Vector2>> { new() { new(0f, 0f), new(50f, 0f), new(50f, 50f), new(0f, 50f) } };
            var chords = new (Vector2 point, float degrees)[]
            {
                (new(10.25f, 10.25f), 45f), (new(25f, 25f), 100f), (new(30.75f, 12.25f), -45f), (new(5f, 40f), 7f),
            };
            foreach (var (point, degrees) in chords)
            {
                float r = degrees * Mathf.Deg2Rad;
                var next = new List<List<Vector2>>();
                foreach (var piece in pieces)
                {
                    Polygon.SplitByChord(piece, point, new Vector2(Mathf.Cos(r), Mathf.Sin(r)), out var left, out var right);
                    if (left.Count > 0) next.Add(left);
                    if (right.Count > 0) next.Add(right);
                }
                pieces = next;
            }
            Assert.Greater(pieces.Count, 6);

            var level = new LevelDescription { Size = new Vector3(50f, 12f, 50f) };
            for (int i = 0; i < pieces.Count; i++)
                level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(pieces[i]), -0.5f, 0.1f * i, Vector2.zero, OwnerKind.Plot, i,
                    TransitionStage.GroundSurfaces);
            var grid = new SpanGrid(50f, 50f, 0.5f);
            var cells = new List<CoveredCell>();
            var hits = new int[grid.CellCount];
            foreach (var p in level.Primitives)
            {
                Rasterizer.CoveredCells(p, level, grid, cells);
                foreach (var c in cells) hits[c.X + c.Z * grid.CellsX]++;
            }
            for (int i = 0; i < hits.Length; i++)
                Assert.AreEqual(1, hits[i], $"cell {i % grid.CellsX}, {i / grid.CellsX}");
        }

        [Test]
        public void Bank30_IsWalkableEverywhereAlongIt([Values(0f, 25f, 45f, 70f)] float yaw)
        {
            var bank = Bank(30f, yaw, out var foot, out var top);
            var walk = WalkReach(bank, foot);
            Assert.IsTrue(walk.reached[top], "the top is reached by walking");
            foreach (int s in walk.bankSurfaces) Assert.IsTrue(walk.reached[s], "every bank surface is reached by walking");
        }

        [Test]
        public void Bank35_IsNotWalkable()
        {
            var bank = Bank(35f, 0f, out var foot, out var top);
            var walk = WalkReach(bank, foot);
            Assert.IsFalse(walk.reached[top]);
            // It is still two-way by jumping: each cell rises 0.35, within a jump.
            var report = bank.Measure();
            var graph = report.Debug.Graph;
            Assert.IsTrue(graph.Reach(foot)[top]);
            Assert.IsTrue(graph.Reach(top)[foot]);
        }

        [Test]
        public void PrismMesh_HasFansAndSidesOnThePlane()
        {
            var outline = new List<Vector2> { new(0f, 0f), new(4f, -1f), new(6f, 2f), new(3f, 5f), new(-1f, 3f) };
            var slope = new Vector2(0.3f, 0.1f);
            var fixture = new Fixture().Prism(PrimitiveRole.Floor, -0.5f, 2f, slope, outline.ToArray());
            var prism = fixture.Level.Primitives[1];
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            PrimitiveMesher.Append(prism, fixture.Level, vertices, normals, triangles);

            int n = outline.Count;
            Assert.AreEqual(3 * (2 * (n - 2) + 2 * n), triangles.Count, "top and bottom fans, two triangles per side");
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                bool onTop = Mathf.Abs(v.y - prism.PrismTop(v.x, v.z)) < Epsilon;
                Assert.IsTrue(onTop || Mathf.Abs(v.y + 0.5f) < Epsilon, $"vertex {v} is on the top plane or the bottom");
            }
            for (int t = 0; t < triangles.Count; t += 3)
            {
                var a = vertices[triangles[t]];
                var b = vertices[triangles[t + 1]];
                var c = vertices[triangles[t + 2]];
                Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), normals[triangles[t]]), 0f, "winding matches the normal");
            }
        }

        [Test]
        public void PrismFixture_IsSoundConvexAndTilesTheLevel()
        {
            var settings = LevelGenSettings.Default.Sanitized();
            var level = PrismFixture.Build(settings);
            Assert.AreEqual(PrismFixture.PlotCount, level.Plots.Count);
            float area = 0f;
            foreach (var plot in level.Plots)
            {
                Assert.IsTrue(Polygon.IsConvex(level.PolygonOf(plot)), $"plot {plot.Id}");
                area += Polygon.Area(level.PolygonOf(plot));
            }
            Assert.AreEqual(settings.Width * settings.Depth, area, 0.01f);
            Assert.IsTrue(level.Transitions.Exists(t => t.Type == TransitionType.Bank));
            Assert.IsTrue(level.Transitions.Exists(t => t.Type == TransitionType.Ramp));
            Assert.IsTrue(level.Primitives.Exists(p => p.Role == PrimitiveRole.Railing));

            var options = MeasureOptions.Default;
            var report = LevelEvaluator.Evaluate(level, settings, options);
            foreach (var c in report.Constraints) Assert.IsTrue(c.Passed, $"{c.Constraint}: {c.Detail}");
        }

        /// <summary>
        /// Ground at 0 and a plateau at 2, with the boundary between them a chord through the level centre at
        /// <paramref name="yaw"/>, joined by a bank of <paramref name="degrees"/> cut from the low side.
        /// </summary>
        private static Fixture Bank(float degrees, float yaw, out int foot, out int top)
        {
            const float rise = 2f;
            var fixture = new Fixture().Plot(0, 0f, 0f, Fixture.Size, Fixture.Size);
            float r = yaw * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            var uphill = new Vector2(-along.y, along.x);
            var centre = new Vector2(Fixture.Size, Fixture.Size) * 0.5f;
            var rect = new List<Vector2> { new(0f, 0f), new(Fixture.Size, 0f), new(Fixture.Size, Fixture.Size), new(0f, Fixture.Size) };
            Polygon.SplitByChord(rect, centre, along, out var high, out var low);

            float depth = rise / Mathf.Tan(degrees * Mathf.Deg2Rad);
            var strip = Polygon.ClipHalfPlane(low, -uphill, -(Vector2.Dot(uphill, centre) - depth));
            float grade = rise / depth;
            float stripTop = rise - Vector2.Dot(uphill, centre - Polygon.Centroid(strip)) * grade;
            fixture.Prism(PrimitiveRole.Floor, -0.5f, rise, Vector2.zero, high.ToArray());
            fixture.Prism(PrimitiveRole.Ramp, -0.5f, stripTop, uphill * grade, strip.ToArray());

            var probe = fixture.Measure();
            var lowPoint = centre - uphill * (depth + 6f);
            var highPoint = centre + uphill * 6f;
            foot = Fixture.TopSurfaceAt(probe, lowPoint.x, lowPoint.y);
            top = Fixture.TopSurfaceAt(probe, highPoint.x, highPoint.y);
            Assert.AreEqual(0f, probe.Debug.Surfaces[foot].Height, Epsilon);
            Assert.AreEqual(rise, probe.Debug.Surfaces[top].Height, Epsilon);
            return fixture;
        }

        /// <summary>Surfaces reachable from <paramref name="start"/> by walk edges alone, and the bank's surfaces.</summary>
        private static (bool[] reached, List<int> bankSurfaces) WalkReach(Fixture fixture, int start)
        {
            var report = fixture.Measure();
            var surfaces = report.Debug.Surfaces;
            var walkOut = new List<int>[surfaces.Count];
            for (int i = 0; i < walkOut.Length; i++) walkOut[i] = new List<int>();
            foreach (var e in report.Debug.Graph.Edges)
                if (e.Kind == MoveKind.Walk) walkOut[e.From].Add(e.To);

            var reached = new bool[surfaces.Count];
            var queue = new Queue<int>();
            reached[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
                foreach (int t in walkOut[queue.Dequeue()])
                    if (!reached[t])
                    {
                        reached[t] = true;
                        queue.Enqueue(t);
                    }

            var bank = new List<int>();
            for (int s = 0; s < surfaces.Count; s++)
                if (surfaces[s].Role == PrimitiveRole.Ramp) bank.Add(s);
            Assert.Greater(bank.Count, 0);
            return (reached, bank);
        }
    }
}
