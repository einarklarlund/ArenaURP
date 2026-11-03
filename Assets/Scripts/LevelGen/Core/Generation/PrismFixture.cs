using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// A hand-written level of angled prisms, the milestone 1 stand-in for the generator. Angled chords cut the level
    /// rectangle into six convex plots at different heights, one with a planar slope. Their edges are flat, ledge,
    /// cliff and blocked, plus a 30° bank (walkable), a 35° bank (only jumpable) and a yawed ramp against a cliff.
    /// A yawed wall, a railing along the blocked cliff top and the boundary walls complete it. Positions are fractions
    /// of the level size, so it fits any width and depth.
    /// </summary>
    public static class PrismFixture
    {
        public const int PlotCount = 6;

        /// <summary>Plot ground runs from here up to its top, as the design's ground slab may extend below y = 0.</summary>
        private const float GroundBottom = -LevelGenSettings.SlabThickness;

        private struct Plot
        {
            public List<Vector2> Polygon;
            public float Height;
            public Vector2 Slope;
        }

        public static LevelDescription Build(in LevelGenSettings s)
        {
            var level = new LevelDescription { Size = new Vector3(s.Width, s.MaxHeight, s.Depth) };
            float width = s.Width, depth = s.Depth;
            Vector2 At(float fx, float fz) => new(fx * width, fz * depth);

            var rect = new List<Vector2> { At(0f, 0f), At(1f, 0f), At(1f, 1f), At(0f, 1f) };
            Polygon.SplitByChord(rect, At(0.5f, 0.5f), Direction(75f), out var west, out var east);
            Polygon.SplitByChord(west, At(0.25f, 0.55f), Direction(15f), out var northWest, out var southWest);
            Polygon.SplitByChord(east, At(0.75f, 0.4f), Direction(-20f), out var northEast, out var southEast);
            Polygon.SplitByChord(northWest, At(0.22f, 0.8f), Direction(100f), out var farWest, out var nearWest);
            Polygon.SplitByChord(northEast, At(0.8f, 0.72f), Direction(160f), out var midEast, out var farEast);

            float slope5 = Mathf.Tan(5f * Mathf.Deg2Rad);
            var plots = new[]
            {
                new Plot { Polygon = southWest, Height = 0f },
                new Plot { Polygon = nearWest, Height = 0.25f },
                new Plot { Polygon = farWest, Height = 2f, Slope = new Vector2(0f, slope5) },
                new Plot { Polygon = southEast, Height = 1f },
                new Plot { Polygon = midEast, Height = 3f },
                new Plot { Polygon = farEast, Height = 5f },
            };

            for (int i = 0; i < plots.Length; i++)
            {
                int polygon = level.AddPolygon(plots[i].Polygon);
                level.Plots.Add(new PlotDescription
                {
                    Id = i, Polygon = polygon, BaseHeight = plots[i].Height, Slope = plots[i].Slope,
                });
            }

            // Each plot's ground, less the banks cut from it (carve, as the generator does).
            var carved = new List<List<Vector2>>[plots.Length];
            for (int i = 0; i < plots.Length; i++) carved[i] = new List<List<Vector2>> { plots[i].Polygon };

            var player = s.Player;
            for (int a = 0; a < plots.Length; a++)
            for (int b = a + 1; b < plots.Length; b++)
            {
                if (!SharedSegment(plots[a].Polygon, plots[b].Polygon, out var from, out var to)) continue;
                int id = level.Transitions.Count;
                var mid = (from + to) * 0.5f;
                float ha = HeightAt(plots[a], mid), hb = HeightAt(plots[b], mid);
                var type = Classify(Mathf.Abs(ha - hb), player);
                float transitionWidth = (to - from).magnitude;
                int low = ha <= hb ? a : b, high = ha <= hb ? b : a;

                if ((a, b) == (3, 4) || (a, b) == (4, 5))
                {
                    // Banks: 30° between the south-east and mid-east plots, 35° (too steep to walk) above that.
                    type = TransitionType.Bank;
                    float degrees = (a, b) == (3, 4) ? 30f : 35f;
                    var strip = AddBank(level, id, plots[low].Polygon, from, to, plots[low].Height, plots[high].Height, degrees);
                    if (strip != null)
                    {
                        var rest = new List<List<Vector2>>();
                        foreach (var piece in carved[low]) rest.AddRange(Polygon.Subtract(piece, strip));
                        carved[low] = rest;
                    }
                }
                else if ((a, b) == (1, 4))
                {
                    type = TransitionType.Ramp;
                    transitionWidth = s.TransitionSizes.RampWidth;
                    AddRamp(level, id, plots[low].Polygon, from, to, plots[low].Height, plots[high].Height,
                        s.TransitionSizes.RampMaxSlopeDegrees, transitionWidth);
                }
                else if (type == TransitionType.Blocked)
                {
                    AddRailing(level, id, plots[high].Polygon, from, to, plots[high].Height, s.Buildings.RailingHeight);
                }

                level.Transitions.Add(new TransitionDescription
                {
                    Id = id, Type = type, PlotA = a, PlotB = b, From = from, To = to, Width = transitionWidth,
                });
            }

            for (int i = 0; i < plots.Length; i++)
                foreach (var piece in carved[i])
                    level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(piece), GroundBottom, HeightAt(plots[i], Polygon.Centroid(piece)),
                        plots[i].Slope, OwnerKind.Plot, i, TransitionStage.GroundSurfaces);

            // A free-standing wall at an angle on the south-west plot.
            level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Wall,
                Shape = PrimitiveShape.Box,
                Position = new Vector3(0.2f * width, 0f, 0.22f * depth),
                Size = new Vector3(0.5f, 3f, 8f),
                YawDegrees = 25f,
                OwnerKind = OwnerKind.Plot,
                OwnerId = 0,
                Surfaces = Surfaces.All(SurfaceTag.CliffFace),
            });

            BoundaryStage.Emit(level, s.Boundary, BoundaryStage.Rectangle(width, depth));
            return level;
        }

        /// <summary>Unit vector at <paramref name="degrees"/> counter-clockwise from +x on the XZ plane.</summary>
        private static Vector2 Direction(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static float HeightAt(in Plot plot, Vector2 p) =>
            plot.Height + Vector2.Dot(plot.Slope, p - Polygon.Centroid(plot.Polygon));

        private static TransitionType Classify(float delta, in PlayerConstants player) =>
            delta <= player.StepHeight ? TransitionType.Flat
            : delta <= player.JumpHeight ? TransitionType.Ledge
            : delta <= player.MaxSafeDrop ? TransitionType.Cliff
            : TransitionType.Blocked;

        /// <summary>
        /// The overlap of two convex polygons' collinear edges, if it is longer than a cell. Plots cut by the same chord
        /// share it exactly, so the tolerance only absorbs float noise.
        /// </summary>
        private static bool SharedSegment(List<Vector2> a, List<Vector2> b, out Vector2 from, out Vector2 to)
        {
            const float tolerance = 1e-3f;
            for (int i = 0; i < a.Count; i++)
            {
                var p0 = a[i];
                var p1 = a[(i + 1) % a.Count];
                float length = (p1 - p0).magnitude;
                var dir = (p1 - p0) / length;
                for (int j = 0; j < b.Count; j++)
                {
                    var q0 = b[j];
                    var q1 = b[(j + 1) % b.Count];
                    if (Mathf.Abs(Cross(dir, q0 - p0)) > tolerance || Mathf.Abs(Cross(dir, q1 - p0)) > tolerance) continue;
                    float t0 = Vector2.Dot(q0 - p0, dir), t1 = Vector2.Dot(q1 - p0, dir);
                    float lo = Mathf.Max(0f, Mathf.Min(t0, t1)), hi = Mathf.Min(length, Mathf.Max(t0, t1));
                    if (hi - lo < 0.5f) continue;
                    from = p0 + dir * lo;
                    to = p0 + dir * hi;
                    return true;
                }
            }
            from = to = default;
            return false;
        }

        /// <summary>Unit normal of the segment pointing into <paramref name="polygon"/>.</summary>
        private static Vector2 Into(List<Vector2> polygon, Vector2 from, Vector2 to)
        {
            var dir = (to - from).normalized;
            var normal = new Vector2(-dir.y, dir.x);
            return Vector2.Dot(Polygon.Centroid(polygon) - from, normal) < 0f ? -normal : normal;
        }

        /// <summary>
        /// A sloped strip cut from the lower plot along the segment, rising from the lower height at its foot to the
        /// higher height at the edge, from the ground's bottom. Returns the strip, for the lower plot's ground to be carved
        /// by (null if there is none).
        /// </summary>
        private static List<Vector2> AddBank(LevelDescription level, int id, List<Vector2> lower, Vector2 from, Vector2 to,
            float low, float high, float degrees)
        {
            var into = Into(lower, from, to);
            var dir = (to - from).normalized;
            float depth = (high - low) / Mathf.Tan(degrees * Mathf.Deg2Rad);
            var strip = Polygon.ClipHalfPlane(lower, into, Vector2.Dot(into, from) + depth);
            strip = Polygon.ClipHalfPlane(strip, -dir, -Vector2.Dot(dir, from));
            strip = Polygon.ClipHalfPlane(strip, dir, Vector2.Dot(dir, to));
            if (strip.Count == 0) return null;

            float grade = (high - low) / depth;
            int polygon = level.AddPolygon(strip);
            float top = high - Vector2.Dot(into, Polygon.Centroid(strip) - from) * grade;
            level.AddPrism(PrimitiveRole.Ramp, polygon, GroundBottom, top, -into * grade, OwnerKind.Transition, id,
                Surfaces.Of(SurfaceTag.Bank, SurfaceTag.RampSide, SurfaceTag.RampSide));
            return strip;
        }

        /// <summary>A yawed ramp on the lower side, its high end against the face at the segment's middle.</summary>
        private static void AddRamp(LevelDescription level, int id, List<Vector2> lower, Vector2 from, Vector2 to,
            float low, float high, float degrees, float rampWidth)
        {
            var into = Into(lower, from, to);
            float run = (high - low) / Mathf.Tan(degrees * Mathf.Deg2Rad);
            var centre = (from + to) * 0.5f + into * (run * 0.5f);
            level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Ramp,
                Shape = PrimitiveShape.Ramp,
                Position = new Vector3(centre.x, low, centre.y),
                Size = new Vector3(rampWidth, high - low, run),
                YawDegrees = Yaw(-into),
                OwnerKind = OwnerKind.Transition,
                OwnerId = id,
                Surfaces = Surfaces.Of(SurfaceTag.Ramp, SurfaceTag.RampSide, SurfaceTag.RampSide),
            });
        }

        /// <summary>A thin railing along the segment on the higher plot's top, kept 0.5 off each end.</summary>
        private static void AddRailing(LevelDescription level, int id, List<Vector2> higher, Vector2 from, Vector2 to,
            float top, float railingHeight)
        {
            const float thickness = 0.1f;
            var into = Into(higher, from, to);
            var centre = (from + to) * 0.5f + into * thickness;
            level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Railing,
                Shape = PrimitiveShape.Box,
                Position = new Vector3(centre.x, top, centre.y),
                Size = new Vector3(thickness, railingHeight, (to - from).magnitude - 1f),
                YawDegrees = Yaw(to - from),
                OwnerKind = OwnerKind.Transition,
                OwnerId = id,
                Surfaces = Surfaces.All(SurfaceTag.Railing),
            });
        }

        /// <summary>Yaw that turns local +Z toward <paramref name="d"/> on the XZ plane.</summary>
        private static float Yaw(Vector2 d) => Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
