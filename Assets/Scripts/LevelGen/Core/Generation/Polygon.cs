using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Operations on convex polygons on the XZ plane (x = level x, y = level z), counter-clockwise seen from above.
    /// Plots, sub-plots, building footprints and Prism outlines are all these. Clipping and splitting keep a convex
    /// polygon convex, which everything downstream relies on.
    /// </summary>
    public static class Polygon
    {
        /// <summary>Points closer than this count as the same, so clipping drops sliver edges.</summary>
        public const float Epsilon = 1e-4f;
        /// <summary>
        /// The tolerance of exact clipping, for carving solids out of each other: points this close are one. It is above
        /// a float's spacing across a level (7.6e-6 at 100 units): closer points are rounding, and the edge between them
        /// points anywhere.
        /// </summary>
        public const float ExactEpsilon = 1e-5f;
        /// <summary>
        /// Exact clipping drops what is narrower than this (<see cref="MinWidth"/>): a hairline along an edge two
        /// solids share, left by rounding. Anything wider is kept, however small its area, so carving takes out every
        /// overlap the overlap check would see (its thickness limit is the same).
        /// </summary>
        public const float SliverWidth = 1e-4f;
        /// <summary>
        /// No solid is narrower than this. A narrower piece is a needle or hairline that clipping left along an edge it
        /// and a neighbour reached by different roundings: it lies a hair over the neighbour, face on face. Leaving it
        /// out leaves a crack too narrow to see or to fall through.
        /// </summary>
        public const float MinSolidWidth = 0.01f;

        /// <summary>Signed area: positive for counter-clockwise.</summary>
        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            double sum = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                sum += (double)polygon[j].x * polygon[i].y - (double)polygon[i].x * polygon[j].y;
            return (float)(sum * 0.5);
        }

        public static float Area(IReadOnlyList<Vector2> polygon) => Mathf.Abs(SignedArea(polygon));

        /// <summary>Area centroid. Falls back to the vertex mean for a degenerate polygon.</summary>
        public static Vector2 Centroid(IReadOnlyList<Vector2> polygon)
        {
            double cx = 0, cy = 0, twiceArea = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double cross = (double)polygon[j].x * polygon[i].y - (double)polygon[i].x * polygon[j].y;
                twiceArea += cross;
                cx += (polygon[j].x + polygon[i].x) * cross;
                cy += (polygon[j].y + polygon[i].y) * cross;
            }
            if (System.Math.Abs(twiceArea) < 1e-9)
            {
                var mean = Vector2.zero;
                foreach (var p in polygon) mean += p;
                return polygon.Count > 0 ? mean / polygon.Count : mean;
            }
            return new Vector2((float)(cx / (3.0 * twiceArea)), (float)(cy / (3.0 * twiceArea)));
        }

        /// <summary>
        /// Even-odd point-in-polygon. Each edge is evaluated with its endpoints in a fixed order, so two polygons that
        /// share an edge give the same answer on it: a point on the shared edge is in exactly one of them, and polygons
        /// that tile a region cover every cell centre once.
        /// </summary>
        public static bool Contains(IReadOnlyList<Vector2> polygon, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (a.y > b.y || (a.y == b.y && a.x > b.x)) (a, b) = (b, a);
                if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p) => Contains(polygon, p.x, p.y);

        /// <summary>
        /// The part of a convex polygon where <c>dot(normal, p) ≤ offset</c> (Sutherland–Hodgman against one line).
        /// Empty when nothing is left; near-duplicate points are dropped.
        /// </summary>
        public static List<Vector2> ClipHalfPlane(IReadOnlyList<Vector2> polygon, Vector2 normal, float offset,
            bool exact = false)
        {
            float epsilon = exact ? ExactEpsilon : Epsilon;
            var result = new List<Vector2>(polygon.Count + 1);
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                float da = Vector2.Dot(normal, a) - offset;
                float db = Vector2.Dot(normal, b) - offset;
                if (da <= 0f) AddPoint(result, a, epsilon);
                if ((da < 0f && db > 0f) || (da > 0f && db < 0f)) AddPoint(result, a + (b - a) * (da / (da - db)), epsilon);
            }
            if (result.Count > 1 && (result[0] - result[^1]).sqrMagnitude < epsilon * epsilon) result.RemoveAt(result.Count - 1);
            if (result.Count < 3) return new List<Vector2>();
            return (exact ? MinWidth(result) > SliverWidth : Area(result) > Epsilon) ? result : new List<Vector2>();
        }

        /// <summary>The intersection of a polygon with a convex counter-clockwise <paramref name="clipper"/>. The
        /// clipper's noise edges are left out (<see cref="IsNoise"/>).</summary>
        public static List<Vector2> ClipConvex(IReadOnlyList<Vector2> polygon, IReadOnlyList<Vector2> clipper,
            bool exact = false)
        {
            var result = new List<Vector2>(polygon);
            for (int i = 0; i < clipper.Count && result.Count > 0; i++)
            {
                if (IsNoise(clipper, i)) continue;
                var (normal, offset) = OutwardEdge(clipper, i);
                result = ClipHalfPlane(result, normal, offset, exact);
            }
            return result;
        }

        /// <summary>
        /// A convex polygon less a convex <paramref name="hole"/>, as convex pieces that tile the rest: piece k is what
        /// lies outside the hole's edge k but inside its edges before k. The pieces share the hole's edges, so a solid
        /// put in the hole meets them face to face. Returns the polygon itself when the hole misses it. Clipping is
        /// exact (<see cref="ExactEpsilon"/>), so the smallest overlap is still taken out. The hole's noise edges
        /// (<see cref="IsNoise"/>) are left out, which takes out at most a sliver more.
        /// </summary>
        public static List<List<Vector2>> Subtract(IReadOnlyList<Vector2> polygon, IReadOnlyList<Vector2> hole)
        {
            var pieces = new List<List<Vector2>>();
            if (ClipConvex(polygon, hole, true).Count == 0)
            {
                pieces.Add(new List<Vector2>(polygon));
                return pieces;
            }
            var rest = new List<Vector2>(polygon);
            for (int i = 0; i < hole.Count && rest.Count > 0; i++)
            {
                if (IsNoise(hole, i)) continue;
                var (normal, offset) = OutwardEdge(hole, i);
                var outside = ClipHalfPlane(rest, -normal, -offset, true);
                if (outside.Count > 0) pieces.Add(outside);
                rest = ClipHalfPlane(rest, normal, offset, true);
            }
            return pieces;
        }

        /// <summary>
        /// A convex counter-clockwise polygon with every edge moved <paramref name="distance"/> inward. Empty when the
        /// polygon is narrower than twice the distance.
        /// </summary>
        public static List<Vector2> Inset(IReadOnlyList<Vector2> polygon, float distance)
        {
            var result = new List<Vector2>(polygon);
            for (int i = 0; i < polygon.Count && result.Count > 0; i++)
            {
                var (normal, offset) = OutwardEdge(polygon, i);
                result = ClipHalfPlane(result, normal, offset - distance);
            }
            return result;
        }

        /// <summary>
        /// A convex counter-clockwise polygon with every edge moved <paramref name="distance"/> outward (mitred
        /// corners): a large square clipped by each edge's half-plane, shifted out.
        /// </summary>
        public static List<Vector2> Outset(IReadOnlyList<Vector2> polygon, float distance)
        {
            var c = Centroid(polygon);
            float r = 0f;
            foreach (var p in polygon) r = Mathf.Max(r, (p - c).magnitude);
            r = 4f * (r + distance + 1f);
            var result = new List<Vector2> { c + new Vector2(-r, -r), c + new Vector2(r, -r), c + new Vector2(r, r), c + new Vector2(-r, r) };
            for (int i = 0; i < polygon.Count && result.Count > 0; i++)
            {
                var (normal, offset) = OutwardEdge(polygon, i);
                result = ClipHalfPlane(result, normal, offset + distance);
            }
            return result;
        }

        /// <summary>
        /// Cuts a convex polygon along the line through <paramref name="point"/> with <paramref name="direction"/>.
        /// <paramref name="left"/> is the side to the left of the direction. Either side may be empty when the line
        /// misses the polygon.
        /// </summary>
        public static void SplitByChord(IReadOnlyList<Vector2> polygon, Vector2 point, Vector2 direction,
            out List<Vector2> left, out List<Vector2> right)
        {
            var normal = new Vector2(direction.y, -direction.x).normalized;
            float offset = Vector2.Dot(normal, point);
            left = ClipHalfPlane(polygon, normal, offset);
            right = ClipHalfPlane(polygon, -normal, -offset);
        }

        /// <summary>
        /// Width of a convex polygon: the smallest distance between two parallel lines that enclose it. For a convex
        /// polygon one of the lines lies along an edge, so it is the least, over edges, of the farthest vertex from it.
        /// </summary>
        public static float MinWidth(IReadOnlyList<Vector2> polygon)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var edge = polygon[(i + 1) % polygon.Count] - a;
                float length = edge.magnitude;
                if (length < Epsilon) continue;
                float farthest = 0f;
                foreach (var v in polygon) farthest = Mathf.Max(farthest, Mathf.Abs(Cross(edge, v - a)) / length);
                best = Mathf.Min(best, farthest);
            }
            return float.IsPositiveInfinity(best) ? 0f : best;
        }

        /// <summary>The polygon's extent along a unit <paramref name="direction"/>: min and max of dot(direction, v).</summary>
        public static void Extent(IReadOnlyList<Vector2> polygon, Vector2 direction, out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            foreach (var v in polygon)
            {
                float d = Vector2.Dot(direction, v);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }

        /// <summary>The convex hull of a set of points, counter-clockwise, without collinear vertices (Andrew's
        /// monotone chain). Fewer than three points when they don't span an area.</summary>
        public static List<Vector2> Hull(IEnumerable<Vector2> points)
        {
            var sorted = new List<Vector2>(points);
            sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (sorted.Count < 3) return sorted;
            var hull = new List<Vector2>(2 * sorted.Count);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < sorted.Count; i++)
                {
                    var p = pass == 0 ? sorted[i] : sorted[sorted.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[^1] - hull[^2], p - hull[^1]) <= 1e-9f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                // Each chain's last point is the next one's first.
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        /// <summary>Whether the polygon is convex and counter-clockwise (collinear vertices allowed).</summary>
        public static bool IsConvex(IReadOnlyList<Vector2> polygon)
        {
            if (polygon.Count < 3) return false;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var c = polygon[(i + 2) % polygon.Count];
                if (Cross(b - a, c - b) < -Epsilon) return false;
            }
            return SignedArea(polygon) > 0f;
        }

        /// <summary>Edge i (from vertex i to i + 1) as the line <c>dot(normal, p) = offset</c>, normal pointing out
        /// of a counter-clockwise polygon.</summary>
        private static (Vector2 normal, float offset) OutwardEdge(IReadOnlyList<Vector2> polygon, int i)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            var normal = new Vector2(b.y - a.y, a.x - b.x).normalized;
            return (normal, Vector2.Dot(normal, a));
        }

        /// <summary>
        /// Whether edge i is too short to clip by: under <see cref="NoiseEdge"/>, its direction is mostly the rounding of
        /// its ends, and one that doubles back (a near-duplicate corner) would put the whole polygon outside it. Its
        /// neighbours' lines meet within a hair of it, so leaving it out moves the boundary by at most that.
        /// </summary>
        private static bool IsNoise(IReadOnlyList<Vector2> polygon, int i) =>
            polygon.Count > 3 && (polygon[(i + 1) % polygon.Count] - polygon[i]).sqrMagnitude < NoiseEdge * NoiseEdge;

        /// <summary>Clippers' and holes' edges shorter than this are rounding (<see cref="IsNoise"/>).</summary>
        private const float NoiseEdge = 1e-3f;

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void AddPoint(List<Vector2> points, Vector2 p, float epsilon)
        {
            if (points.Count > 0 && (points[^1] - p).sqrMagnitude < epsilon * epsilon) return;
            points.Add(p);
        }
    }
}
