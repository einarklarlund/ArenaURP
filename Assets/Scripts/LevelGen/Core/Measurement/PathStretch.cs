using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>What path stretch measured: the level value, its spread, and the worst pair with its path.</summary>
    public sealed class PathStretchResult
    {
        /// <summary>Mean over pairs: the level value. 1 when there are no pairs.</summary>
        public float Mean = 1f;
        public float P95 = 1f;
        public float Worst = 1f;
        /// <summary>Ordered pairs measured.</summary>
        public int Pairs;
        /// <summary>Ordered pairs with no path at all. They are left out of the mean; the no-stuck rule reports them.</summary>
        public int Unreachable;
        /// <summary>Sample surfaces, in lattice order.</summary>
        public List<int> Samples = new();
        /// <summary>Per sample: the mean stretch of the pairs it is either end of, or 0 when it is in none.</summary>
        public List<float> SampleStretch = new();
        public int WorstFrom = -1;
        public int WorstTo = -1;
        /// <summary>Surfaces along a shortest path of the worst pair, from first to last.</summary>
        public List<int> WorstPath = new();
    }

    /// <summary>
    /// How much detour the level forces compared with walking in a straight line (see the design doc). Samples are
    /// walkable outdoor surfaces in R on every k-th cell of a lattice offset by k/2; outdoor means the top of a ground,
    /// plot or transition primitive. For each ordered pair in different top-level plots and at least
    /// <see cref="MinPairDistance"/> apart, the stretch is the 8-neighbour walking distance ÷ the octile distance.
    /// </summary>
    public static class PathStretch
    {
        /// <summary>Pairs closer than this (in units, horizontally) aren't measured.</summary>
        public const float MinPairDistance = 8f;

        public static PathStretchResult Measure(SurfaceSet surfaces, PlotMap plots, MovementGraph graph, bool[] outdoor,
            bool[] inR, int stride)
        {
            var result = new PathStretchResult();
            var grid = surfaces.Grid;
            stride = Mathf.Max(1, stride);
            int offset = stride / 2;

            for (int s = 0; s < surfaces.Count; s++)
            {
                var surface = surfaces[s];
                if (surface.X % stride != offset || surface.Z % stride != offset) continue;
                if (!outdoor[s] || !inR[s] || plots.SurfaceTopPlot[s] < 0) continue;
                result.Samples.Add(s);
            }

            int n = result.Samples.Count;
            var sums = new double[n];
            var counts = new int[n];
            var values = new List<float>();
            var distance = new float[surfaces.Count];
            float minCells = MinPairDistance / grid.CellSize;
            for (int i = 0; i < n; i++)
            {
                var a = surfaces[result.Samples[i]];
                bool ran = false;
                for (int j = 0; j < n; j++)
                {
                    var b = surfaces[result.Samples[j]];
                    if (plots.SurfaceTopPlot[result.Samples[i]] == plots.SurfaceTopPlot[result.Samples[j]]) continue;
                    int dx = Mathf.Abs(a.X - b.X), dz = Mathf.Abs(a.Z - b.Z);
                    if (dx * dx + dz * dz < minCells * minCells) continue;
                    if (!ran)
                    {
                        graph.Distances(result.Samples[i], distance);
                        ran = true;
                    }

                    float d = distance[result.Samples[j]];
                    if (float.IsPositiveInfinity(d))
                    {
                        result.Unreachable++;
                        continue;
                    }
                    float stretch = d / Octile(dx, dz, grid.CellSize);
                    values.Add(stretch);
                    sums[i] += stretch;
                    sums[j] += stretch;
                    counts[i]++;
                    counts[j]++;
                    if (result.WorstFrom < 0 || stretch > result.Worst)
                    {
                        result.Worst = stretch;
                        result.WorstFrom = result.Samples[i];
                        result.WorstTo = result.Samples[j];
                    }
                }
            }

            for (int i = 0; i < n; i++) result.SampleStretch.Add(counts[i] > 0 ? (float)(sums[i] / counts[i]) : 0f);
            result.Pairs = values.Count;
            if (values.Count == 0) return result;

            double total = 0;
            foreach (float v in values) total += v;
            result.Mean = (float)(total / values.Count);
            values.Sort();
            result.P95 = values[Mathf.Min(values.Count - 1, Mathf.CeilToInt(0.95f * values.Count) - 1)];

            var parent = new int[surfaces.Count];
            graph.Distances(result.WorstFrom, distance, parent);
            for (int s = result.WorstTo; s >= 0; s = parent[s]) result.WorstPath.Add(s);
            result.WorstPath.Reverse();
            return result;
        }

        /// <summary>Walking distance on open 8-neighbour ground between cells <paramref name="dx"/>, <paramref name="dz"/> apart.</summary>
        public static float Octile(int dx, int dz, float cellSize)
        {
            int lo = Mathf.Min(dx, dz), hi = Mathf.Max(dx, dz);
            return (hi + (1.41421356f - 1f) * lo) * cellSize;
        }

        /// <summary>
        /// Surfaces formed by the top of a walkable primitive the level, a plot or a transition owns. Buildings and
        /// props are routable but never samples.
        /// </summary>
        public static bool[] OutdoorSurfaces(LevelDescription description, SurfaceSet surfaces)
        {
            var outdoor = new bool[surfaces.Count];
            var cells = new List<CoveredCell>();
            foreach (var p in description.Primitives)
            {
                if (!PrimitiveRoles.IsWalkable(p.Role)) continue;
                if (p.OwnerKind is not (OwnerKind.Level or OwnerKind.Plot or OwnerKind.Transition)) continue;
                Rasterizer.CoveredCells(p, description, surfaces.Grid, cells);
                foreach (var c in cells)
                {
                    int s = surfaces.FindAt(c.X, c.Z, c.Top);
                    if (s >= 0) outdoor[s] = true;
                }
            }
            return outdoor;
        }
    }
}
