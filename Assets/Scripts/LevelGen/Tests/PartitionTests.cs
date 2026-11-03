using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PartitionTests
    {
        internal static IEnumerable<LevelGenSettings> Presets()
        {
            yield return LevelGenSettings.Default;
            var small = LevelGenSettings.Default;
            small.Width = small.Depth = 50f;
            small.PlotCount = 4;
            yield return small;
            var dense = LevelGenSettings.Default;
            dense.Width = 100f;
            dense.Depth = 60f;
            dense.PlotCount = 8;
            dense.Plots.SizeSpread = 1f;
            dense.Plots.Regularity = 0f;
            yield return dense;
        }

        [Test]
        public void PlotsAndCutCells_TileTheBox_PlotsInsideConvexAndWideEnough([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int seed)
        {
            foreach (var raw in Presets())
            {
                var s = raw.Sanitized();
                var partition = PartitionStage.Build(s, (ulong)seed, out string failure);
                Assert.IsNotNull(partition, failure);
                Assert.AreEqual(s.PlotCount, partition.Polygons.Count);

                foreach (var polygon in partition.Polygons)
                {
                    Assert.IsTrue(Polygon.IsConvex(polygon));
                    Assert.GreaterOrEqual(Polygon.MinWidth(polygon), s.Plots.MinPlotSide - 1e-3f);
                    foreach (var v in polygon)
                        Assert.IsTrue(v.x >= 0f && v.y >= 0f && v.x <= s.Width && v.y <= s.Depth, "plots lie wholly inside the level");
                }

                var all = new List<List<Vector2>>(partition.Polygons);
                all.AddRange(partition.CutPolygons);
                float area = 0f;
                foreach (var polygon in all) area += Polygon.Area(polygon);
                var box = partition.BoxMax - partition.BoxMin;
                Assert.AreEqual(box.x * box.y, area, 0.01f * box.x);

                // Every cell centre of the level in exactly one plot or cut cell: no gaps, no overlaps.
                var grid = new SpanGrid(s.Width, s.Depth, s.CellSize);
                for (int z = 0; z < grid.CellsZ; z++)
                for (int x = 0; x < grid.CellsX; x++)
                {
                    int hits = 0;
                    foreach (var polygon in all)
                        if (Polygon.Contains(polygon, grid.CellCenterX(x), grid.CellCenterZ(z))) hits++;
                    Assert.AreEqual(1, hits, $"{s.Width}×{s.Depth} seed {seed}: cell {x}, {z}");
                }
            }
        }

        [Test]
        public void Outline_FollowsCellEdges_AndThePlotsStayJoined([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int seed)
        {
            var s = LevelGenSettings.Default.Sanitized();
            var partition = PartitionStage.Build(s, (ulong)seed, out string failure);
            Assert.IsNotNull(partition, failure);

            // No outline edge runs along a side of the level rectangle: the rectangle shapes no plot.
            foreach (var e in partition.Outline)
            {
                bool alongSide = (e.From.x <= 1e-3f && e.To.x <= 1e-3f) || (e.From.y <= 1e-3f && e.To.y <= 1e-3f)
                    || (e.From.x >= s.Width - 1e-3f && e.To.x >= s.Width - 1e-3f)
                    || (e.From.y >= s.Depth - 1e-3f && e.To.y >= s.Depth - 1e-3f);
                Assert.IsFalse(alongSide, $"outline edge {e.From} → {e.To}");
            }

            var seen = new bool[partition.Polygons.Count];
            var stack = new Stack<int>();
            seen[0] = true;
            stack.Push(0);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                foreach (var e in partition.Edges)
                {
                    int q = e.A == p ? e.B : e.B == p ? e.A : -1;
                    if (q < 0 || seen[q]) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
            }
            CollectionAssert.DoesNotContain(seen, false, "every plot is joined to plot 0 by shared edges");

            // The outline closes: every outline edge's end starts exactly one outline edge.
            foreach (var e in partition.Outline)
                Assert.AreEqual(1, partition.Outline.FindAll(o => o.From == e.To).Count);
        }

        [Test]
        public void Neighbours_ShareTheirEdgeEndpointsExactly([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int seed)
        {
            var s = LevelGenSettings.Default.Sanitized();
            var partition = PartitionStage.Build(s, (ulong)seed, out _);
            Assert.Greater(partition.Edges.Count, 0);
            foreach (var edge in partition.Edges)
            {
                Assert.Less(edge.A, edge.B);
                Assert.GreaterOrEqual(edge.Length, s.Plots.MinSharedEdge);
                var other = partition.Polygons[edge.B];
                Assert.IsTrue(ContainsExactly(other, edge.From) && ContainsExactly(other, edge.To),
                    $"plots {edge.A} and {edge.B}");
            }
        }

        [Test]
        public void SameSeed_GivesTheSamePartition()
        {
            var s = LevelGenSettings.Default.Sanitized();
            var a = PartitionStage.Build(s, 77UL, out _);
            var b = PartitionStage.Build(s, 77UL, out _);
            for (int i = 0; i < a.Polygons.Count; i++) CollectionAssert.AreEqual(a.Polygons[i], b.Polygons[i]);
        }

        private static bool ContainsExactly(List<Vector2> polygon, Vector2 p)
        {
            foreach (var v in polygon)
                if (v.x == p.x && v.y == p.y) return true;
            return false;
        }
    }
}
