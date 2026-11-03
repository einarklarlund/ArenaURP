using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>The shared boundary of two neighbouring plots, counter-clockwise around plot <see cref="A"/>.</summary>
    public struct PlotEdge
    {
        public int A;
        public int B;
        public Vector2 From;
        public Vector2 To;

        public float Length => (To - From).magnitude;
    }

    /// <summary>
    /// Stage 2's output: convex plots inside the level rectangle; which of them are neighbours; the cells of the
    /// diagram that were cut away; and the outline of the playable area, where the boundary walls go.
    /// </summary>
    public sealed class PlotPartition
    {
        /// <summary>Per plot, its site.</summary>
        public List<Vector2> Sites = new();
        /// <summary>Per plot, a convex counter-clockwise polygon. Neighbours share their edge's endpoints bit for bit.</summary>
        public List<List<Vector2>> Polygons = new();
        /// <summary>
        /// Every other cell of the diagram, unclipped: those crossing the level rectangle and the extra ones cut from
        /// inside it. With <see cref="Polygons"/> they tile the padded box the diagram was drawn in.
        /// </summary>
        public List<List<Vector2>> CutPolygons = new();
        /// <summary>The padded box the diagram was drawn in: min corner and max corner.</summary>
        public Vector2 BoxMin, BoxMax;
        /// <summary>Neighbour pairs (A &lt; B) whose shared edge is at least <c>minSharedEdge</c> long.</summary>
        public List<PlotEdge> Edges = new();
        /// <summary>
        /// Edges of the playable area: plot edges against a cut cell. <see cref="PlotEdge.A"/> is the plot (on the left,
        /// as the edges run counter-clockwise around it) and <see cref="PlotEdge.B"/> is -1.
        /// </summary>
        public List<PlotEdge> Outline = new();
        /// <summary>Draws made, including the one kept.</summary>
        public int Attempts;

        /// <summary>A plot's id in the description: its index.</summary>
        public int IdOf(int polygon) => polygon;
    }

    /// <summary>
    /// Stage 2. The power diagram is drawn in a box padded by one site spacing on every side, so the level rectangle
    /// never shapes a plot: only cells that lie wholly inside the rectangle can become plots, and the rest are cut
    /// away. The site spacing is set so that about <c>plotCount + </c><see cref="ExtraInside"/> cells fit inside.
    /// <list type="number">
    /// <item>Scatter sites over the padded box with a minimum spacing, weight them by <c>sizeSpread</c>, build the
    /// power diagram and relax it with <c>regularity × 2</c> Lloyd iterations.</item>
    /// <item>Cells wholly inside the rectangle are candidates. Redraw from the next sub-seed if there are fewer than
    /// <c>plotCount</c> or any is narrower than <c>minPlotSide</c>.</item>
    /// <item>Cut the extra candidates from the outline (cells next to a cut cell), keeping the plots left joined by
    /// shared edges of at least <c>minSharedEdge</c>.</item>
    /// </list>
    /// <para>
    /// Every edge remembers which line it lies on (a side of the box or the bisector of two sites). After clipping,
    /// each vertex is recomputed from its two lines, canonically ordered by the sites involved, so every cell meeting
    /// at a vertex computes exactly the same point. That is what makes the plots tile the span grid exactly.
    /// </para>
    /// </summary>
    public static class PartitionStage
    {
        public const int MaxAttempts = 32;

        /// <summary>Cells beyond <c>plotCount</c> the spacing aims to fit inside the rectangle; the surplus is cut from
        /// the outline, which varies it.</summary>
        public const float ExtraInside = 1.5f;

        /// <summary>
        /// How far, in site spacings, a cell reaches from its site, as seen by the rectangle: a cell fits inside when
        /// its site is about this far from every side. Calibrated by sweeping seeds.
        /// </summary>
        private const float CellReach = 0.6f;

        /// <summary>Weights shift a bisector by up to this many site spacings; more than about 0.3 empties cells.</summary>
        private const double WeightScale = 0.5;

        /// <summary>Site labels pack two indices: the bisector of sites a &lt; b is a·LabelBase + b.</summary>
        private const int LabelBase = 1024;

        /// <summary>Returns null when no draw within <see cref="MaxAttempts"/> works.</summary>
        public static PlotPartition Build(in LevelGenSettings s, ulong seed, out string failure)
        {
            failure = null;
            float spacing = Spacing(s.Width, s.Depth, s.PlotCount + ExtraInside);
            var min = new Vector2(-spacing, -spacing);
            var max = new Vector2(s.Width + spacing, s.Depth + spacing);
            int n = Mathf.Max(s.PlotCount + 1, Mathf.RoundToInt((max.x - min.x) * (max.y - min.y) / (spacing * spacing)));
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var rng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Partition, attempt), GenStage.Partition);
                var sites = ScatterSites(min, max, n, spacing, ref rng);

                var weightRng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.PlotWeights, attempt), GenStage.PlotWeights);
                var spread = new MetricTarget { Target = 0.5f, Spread = s.Plots.SizeSpread };
                var allocated = TargetAllocation.Allocate(spread, n, ref weightRng);
                var weights = new float[n];
                for (int i = 0; i < n; i++) weights[i] = (float)((allocated[i] - 0.5) * WeightScale * spacing * spacing);

                var box = new Box
                {
                    Min = min, Max = max, Sites = sites, Weights = weights,
                    Outline = new List<Vector2> { min, new(max.x, min.y), max, new(min.x, max.y) },
                };
                var polygons = Cells(box, out var labels);
                int lloyd = Mathf.RoundToInt(s.Plots.Regularity * 2f);
                for (int k = 0; k < lloyd; k++)
                {
                    for (int i = 0; i < n; i++)
                        if (polygons[i] != null) sites[i] = Polygon.Centroid(polygons[i]);
                    polygons = Cells(box, out labels);
                }

                var inside = new bool[n];
                int candidates = 0;
                float narrowest = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    inside[i] = polygons[i] != null && Inside(polygons[i], s.Width, s.Depth);
                    if (!inside[i]) continue;
                    candidates++;
                    narrowest = Mathf.Min(narrowest, Polygon.MinWidth(polygons[i]));
                }
                if (candidates < s.PlotCount)
                {
                    failure = $"only {candidates} cells fit inside the level, fewer than the {s.PlotCount} plots";
                    continue;
                }
                if (narrowest < s.Plots.MinPlotSide)
                {
                    failure = $"the narrowest plot was {narrowest:0.#} units wide, under the min plot side {s.Plots.MinPlotSide:0.#}";
                    continue;
                }

                var cutRng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Cut, attempt), GenStage.Cut);
                var partition = Cut(box, polygons, labels, inside, s, ref cutRng);
                if (partition == null)
                {
                    failure = "no choice of cells to cut kept the remaining plots joined";
                    continue;
                }
                partition.Attempts = attempt + 1;
                failure = null;
                return partition;
            }
            failure = $"no partition in {MaxAttempts} draws worked (the last: {failure})";
            return null;
        }

        /// <summary>
        /// The site spacing at which about <paramref name="target"/> cells fit wholly inside a width × depth rectangle:
        /// solves <c>(W − 2r·s)(D − 2r·s) = target·s²</c> for s, with r = <see cref="CellReach"/>.
        /// </summary>
        private static float Spacing(float width, float depth, float target)
        {
            double c = 2.0 * CellReach;
            double a = c * c - target, b = -c * (width + depth), k = (double)width * depth;
            return (float)((-b - Math.Sqrt(b * b - 4 * a * k)) / (2 * a));
        }

        private static bool Inside(List<Vector2> polygon, float width, float depth)
        {
            foreach (var v in polygon)
                if (v.x < 0f || v.y < 0f || v.x > width || v.y > depth) return false;
            return true;
        }

        /// <summary>
        /// Dart throwing over the box, at least 0.6 spacings apart. The spacing shrinks by a tenth whenever 200 darts
        /// in a row miss, so it always finishes.
        /// </summary>
        private static List<Vector2> ScatterSites(Vector2 min, Vector2 max, int n, float spacing, ref Pcg32 rng)
        {
            float minDistance = 0.6f * spacing;
            var sites = new List<Vector2>(n);
            int misses = 0;
            while (sites.Count < n)
            {
                var p = new Vector2(min.x + rng.NextFloat() * (max.x - min.x), min.y + rng.NextFloat() * (max.y - min.y));
                bool clear = true;
                foreach (var q in sites) clear &= (p - q).sqrMagnitude >= minDistance * minDistance;
                if (clear)
                {
                    sites.Add(p);
                    misses = 0;
                }
                else if (++misses >= 200)
                {
                    minDistance *= 0.9f;
                    misses = 0;
                }
            }
            return sites;
        }

        // ---------------------------------------------------------------- power diagram

        // Line labels: the outline's edge k (from vertex k to k + 1) is -1 - k; the bisector of sites a < b is
        // a·LabelBase + b.
        private static int Bisector(int a, int b) => Math.Min(a, b) * LabelBase + Math.Max(a, b);

        private sealed class Box
        {
            public Vector2 Min, Max;
            /// <summary>The convex counter-clockwise outline the cells are clipped to.</summary>
            public List<Vector2> Outline;
            public List<Vector2> Sites;
            public float[] Weights;
        }

        private struct Line
        {
            public double Nx, Nz, C;
        }

        private struct Vertex
        {
            public double X, Z;
            /// <summary>The line the edge from this vertex to the next lies on.</summary>
            public int Edge;
        }

        /// <summary>
        /// The Voronoi cells of <paramref name="sites"/> within a convex counter-clockwise <paramref name="outline"/>,
        /// computed as the partition does, so cells share their vertices bit for bit and the outline's own vertices are
        /// kept exactly. Null where a cell is empty.
        /// </summary>
        public static List<List<Vector2>> Voronoi(List<Vector2> outline, List<Vector2> sites)
        {
            var box = new Box { Outline = outline, Sites = sites, Weights = new float[sites.Count] };
            return Cells(box, out _);
        }

        /// <summary>The power cell of each site within the outline (null where a cell is empty). <paramref name="labels"/>
        /// gets each polygon's edge labels.</summary>
        private static List<List<Vector2>> Cells(Box box, out List<int[]> labels)
        {
            labels = new List<int[]>();
            var cells = new List<List<Vector2>>();
            for (int i = 0; i < box.Sites.Count; i++)
            {
                var cell = new List<Vertex>(box.Outline.Count);
                for (int k = 0; k < box.Outline.Count; k++)
                    cell.Add(new Vertex { X = box.Outline[k].x, Z = box.Outline[k].y, Edge = -1 - k });
                for (int j = 0; j < box.Sites.Count && cell.Count > 0; j++)
                    if (j != i) cell = Clip(cell, i, j, box);

                var polygon = new List<Vector2>(cell.Count);
                var edges = new List<int>(cell.Count);
                for (int k = 0; k < cell.Count; k++)
                {
                    int incoming = cell[(k + cell.Count - 1) % cell.Count].Edge;
                    var p = Corner(incoming, cell[k].Edge, box);
                    if (polygon.Count > 0 && polygon[^1] == p)
                    {
                        edges[^1] = cell[k].Edge;
                        continue;
                    }
                    polygon.Add(p);
                    edges.Add(cell[k].Edge);
                }
                if (polygon.Count > 1 && polygon[0] == polygon[^1])
                {
                    polygon.RemoveAt(polygon.Count - 1);
                    edges.RemoveAt(edges.Count - 1);
                }
                bool empty = polygon.Count < 3 || Polygon.Area(polygon) < 1e-3f;
                cells.Add(empty ? null : polygon);
                labels.Add(empty ? null : edges.ToArray());
            }
            return cells;
        }

        /// <summary>Keeps the part of cell <paramref name="i"/> at least as close (in power) to site i as to site j.</summary>
        private static List<Vertex> Clip(List<Vertex> cell, int i, int j, Box box)
        {
            int label = Bisector(i, j);
            var line = LineOf(label, box);
            // LineOf orients the bisector of a < b toward a's side being negative; flip it for the larger site.
            double sign = i < j ? 1.0 : -1.0;
            var result = new List<Vertex>(cell.Count + 1);
            for (int k = 0; k < cell.Count; k++)
            {
                var a = cell[k];
                var b = cell[(k + 1) % cell.Count];
                double fa = sign * (line.Nx * a.X + line.Nz * a.Z - line.C);
                double fb = sign * (line.Nx * b.X + line.Nz * b.Z - line.C);
                bool ina = fa <= 0, inb = fb <= 0;
                if (ina) result.Add(a);
                if (ina == inb) continue;
                double t = fa / (fa - fb);
                result.Add(new Vertex { X = a.X + (b.X - a.X) * t, Z = a.Z + (b.Z - a.Z) * t, Edge = ina ? label : a.Edge });
            }
            return result;
        }

        /// <summary><c>N·p = C</c>: an edge of the outline, or the power bisector of two sites (negative on the lower
        /// site's side).</summary>
        private static Line LineOf(int label, Box box)
        {
            if (label < 0)
            {
                int k = -1 - label;
                var e0 = box.Outline[k];
                var e1 = box.Outline[(k + 1) % box.Outline.Count];
                double nx = (double)e1.y - e0.y, nz = (double)e0.x - e1.x;
                return new Line { Nx = nx, Nz = nz, C = nx * e0.x + nz * e0.y };
            }
            int a = label / LabelBase, b = label % LabelBase;
            var sites = box.Sites;
            double ax = sites[a].x, az = sites[a].y, bx = sites[b].x, bz = sites[b].y;
            return new Line
            {
                Nx = 2 * (bx - ax),
                Nz = 2 * (bz - az),
                C = bx * bx + bz * bz - (ax * ax + az * az) - box.Weights[b] + box.Weights[a],
            };
        }

        /// <summary>
        /// The vertex where two edges meet, computed the same way by every cell that has it: two bisectors stand for
        /// their three sites, whose two lowest-numbered bisectors are intersected; any other pair is intersected in
        /// label order.
        /// </summary>
        private static Vector2 Corner(int first, int second, Box box)
        {
            int l1 = Math.Min(first, second), l2 = Math.Max(first, second);
            if (l2 < 0)
            {
                // Two outline edges meet at an outline vertex: keep it exactly.
                int n = box.Outline.Count, k1 = -1 - l1, k2 = -1 - l2;
                if ((k1 + 1) % n == k2) return box.Outline[k2];
                if ((k2 + 1) % n == k1) return box.Outline[k1];
            }
            if (l1 >= 0)
            {
                var ids = new SortedSet<int> { l1 / LabelBase, l1 % LabelBase, l2 / LabelBase, l2 % LabelBase };
                if (ids.Count == 3)
                {
                    var e = ids.GetEnumerator();
                    e.MoveNext(); int a = e.Current;
                    e.MoveNext(); int b = e.Current;
                    e.MoveNext(); int c = e.Current;
                    l1 = Bisector(a, b);
                    l2 = Bisector(a, c);
                }
            }
            var p = LineOf(l1, box);
            var q = LineOf(l2, box);
            double det = p.Nx * q.Nz - p.Nz * q.Nx;
            if (Math.Abs(det) < 1e-12) return Vector2.zero;
            return new Vector2((float)((p.C * q.Nz - q.C * p.Nz) / det), (float)((p.Nx * q.C - q.Nx * p.C) / det));
        }

        // ---------------------------------------------------------------- cut

        /// <summary>
        /// Cuts every cell not wholly inside the rectangle, then <c>candidates − plotCount</c> more from the outline,
        /// trying random choices until the plots left are joined by shared edges at least <c>minSharedEdge</c> long.
        /// Renumbers the plots that stay. Null when no choice in a fixed number of tries works.
        /// </summary>
        private static PlotPartition Cut(Box box, List<List<Vector2>> polygons, List<int[]> labels, bool[] inside,
            in LevelGenSettings s, ref Pcg32 rng)
        {
            int n = polygons.Count;
            int Other(int a, int label) => label < 0 ? -1 : label / LabelBase == a ? label % LabelBase : label / LabelBase;

            var links = new List<(int a, int b)>();
            var onOutline = new List<int>();
            int candidates = 0;
            for (int a = 0; a < n; a++)
            {
                if (!inside[a]) continue;
                candidates++;
                bool outline = false;
                for (int k = 0; k < polygons[a].Count; k++)
                {
                    int b = Other(a, labels[a][k]);
                    if (b < 0 || !inside[b])
                    {
                        outline = true;
                        continue;
                    }
                    var length = (polygons[a][(k + 1) % polygons[a].Count] - polygons[a][k]).magnitude;
                    if (b > a && length >= s.Plots.MinSharedEdge) links.Add((a, b));
                }
                if (outline) onOutline.Add(a);
            }

            int extra = candidates - s.PlotCount;
            var removed = new bool[n];
            bool found = false;
            for (int tries = 0; tries < 24 && !found && onOutline.Count >= extra; tries++)
            {
                for (int i = onOutline.Count - 1; i > 0; i--)
                {
                    int j = rng.Range(0, i + 1);
                    (onOutline[i], onOutline[j]) = (onOutline[j], onOutline[i]);
                }
                for (int a = 0; a < n; a++) removed[a] = !inside[a];
                for (int i = 0; i < extra; i++) removed[onOutline[i]] = true;
                found = Joined(n, links, removed);
                if (extra == 0) break;
            }
            if (!found) return null;

            var partition = new PlotPartition { BoxMin = box.Min, BoxMax = box.Max };
            var index = new int[n];
            for (int a = 0; a < n; a++)
            {
                if (removed[a])
                {
                    index[a] = -1;
                    if (polygons[a] != null) partition.CutPolygons.Add(polygons[a]);
                    continue;
                }
                index[a] = partition.Polygons.Count;
                partition.Polygons.Add(polygons[a]);
                partition.Sites.Add(box.Sites[a]);
            }

            for (int a = 0; a < n; a++)
            {
                if (removed[a]) continue;
                var polygon = polygons[a];
                for (int k = 0; k < polygon.Count; k++)
                {
                    int other = Other(a, labels[a][k]);
                    var edge = new PlotEdge { A = index[a], B = -1, From = polygon[k], To = polygon[(k + 1) % polygon.Count] };
                    if (other < 0 || removed[other])
                    {
                        partition.Outline.Add(edge);
                        continue;
                    }
                    // Each kept pair once, from its lower-numbered site's side, which keeps A < B after renumbering.
                    if (other < a || edge.Length < s.Plots.MinSharedEdge) continue;
                    edge.B = index[other];
                    partition.Edges.Add(edge);
                }
            }
            return partition;
        }

        private static bool Joined(int n, List<(int a, int b)> links, bool[] removed)
        {
            int start = Array.IndexOf(removed, false);
            if (start < 0) return false;
            var seen = new bool[n];
            var stack = new Stack<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                foreach (var (a, b) in links)
                {
                    int q = a == p ? b : b == p ? a : -1;
                    if (q < 0 || removed[q] || seen[q]) continue;
                    seen[q] = true;
                    stack.Push(q);
                }
            }
            for (int i = 0; i < n; i++)
                if (!removed[i] && !seen[i]) return false;
            return true;
        }
    }
}
