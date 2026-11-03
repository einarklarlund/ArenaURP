using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>Two primitives that overlap: by volume, or by coplanar faces facing the same way.</summary>
    public struct Overlap
    {
        /// <summary>Indices into <see cref="LevelDescription.Primitives"/>.</summary>
        public int A, B;
        /// <summary>The volume they share (0 for a face overlap).</summary>
        public double Volume;
        /// <summary>The area of their coplanar faces that overlaps (0 for a volume overlap).</summary>
        public double Area;
        /// <summary>The overlap's centre, for overlays and reports.</summary>
        public Vector3 Where;
    }

    /// <summary>
    /// Hard constraint 7 (see the design's "No overlapping geometry"): no two solids interpenetrate, and no two lie
    /// face to face in one plane facing the same way. Every primitive is a convex solid (a flight of stairs is one per
    /// step). Solids are bucketed on the XZ grid, and each pair sharing a bucket is tested: their intersection's volume
    /// is found by clipping one by the other's planes (it counts above <see cref="MaxVolume"/> and
    /// <see cref="MinThickness"/>), and their faces are compared plane by plane.
    /// </summary>
    public static class OverlapCheck
    {
        public const double MaxVolume = 1e-6;
        /// <summary>
        /// Solids that share a face can still cross it by the rounding of their float corners (about 4e-6 at 50 units
        /// out), which over a face of a few square metres adds up to more than <see cref="MaxVolume"/>. So a volume
        /// overlap must also be thicker than this: twice its volume over its surface area, its thickness for a slab.
        /// </summary>
        public const double MinThickness = 1e-4;
        public const double PlaneTolerance = 1e-4;
        public const double MaxArea = 1e-4;
        private const float BucketSize = 4f;

        public static List<Overlap> Find(LevelDescription level)
        {
            var solids = new List<Solid>();
            for (int i = 0; i < level.Primitives.Count; i++) AddSolids(level, i, solids);

            var buckets = new Dictionary<(int, int), List<int>>();
            for (int s = 0; s < solids.Count; s++)
            {
                var b = solids[s].Bounds;
                for (int x = Mathf.FloorToInt((float)b.minX / BucketSize); x <= Mathf.FloorToInt((float)b.maxX / BucketSize); x++)
                    for (int z = Mathf.FloorToInt((float)b.minZ / BucketSize); z <= Mathf.FloorToInt((float)b.maxZ / BucketSize); z++)
                    {
                        if (!buckets.TryGetValue((x, z), out var list)) buckets[(x, z)] = list = new List<int>();
                        list.Add(s);
                    }
            }

            var found = new List<Overlap>();
            var tested = new HashSet<long>();
            var reported = new HashSet<long>();
            foreach (var list in buckets.Values)
                for (int i = 0; i < list.Count; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        int a = Math.Min(list[i], list[j]), b = Math.Max(list[i], list[j]);
                        var sa = solids[a];
                        var sb = solids[b];
                        if (sa.Primitive == sb.Primitive) continue;
                        if (!tested.Add(((long)a << 32) | (uint)b)) continue;
                        if (!sa.Bounds.Overlaps(sb.Bounds, PlaneTolerance)) continue;
                        if (!Test(sa, sb, out var overlap)) continue;
                        int pa = Math.Min(sa.Primitive, sb.Primitive), pb = Math.Max(sa.Primitive, sb.Primitive);
                        if (!reported.Add(((long)pa << 32) | (uint)pb)) continue;
                        overlap.A = pa;
                        overlap.B = pb;
                        found.Add(overlap);
                    }
            found.Sort((x, y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));
            return found;
        }

#if LEVELGEN_PROFILE
        /// <summary>The profiling runner's look at every check's overlaps.</summary>
        public static Action<LevelDescription, List<Overlap>> Observed;
#endif

        /// <summary>The check as a constraint result: each pair, with its owners and where it is.</summary>
        public static ConstraintResult Check(LevelDescription level)
        {
            var result = new ConstraintResult { Constraint = Constraint.NoOverlap };
            var overlaps = Find(level);
#if LEVELGEN_PROFILE
            Observed?.Invoke(level, overlaps);
#endif
            result.Passed = overlaps.Count == 0;
            if (result.Passed)
            {
                result.Detail = "No overlapping geometry.";
                return result;
            }
            var examples = new List<string>();
            foreach (var o in overlaps)
            {
                if (!result.Items.Contains(o.A)) result.Items.Add(o.A);
                if (!result.Items.Contains(o.B)) result.Items.Add(o.B);
                if (examples.Count < 4) examples.Add($"{Describe(level, o.A)} and {Describe(level, o.B)} at {o.Where:F1}");
            }
            result.Detail = $"{overlaps.Count} overlapping pairs, e.g. {string.Join("; ", examples)}";
            return result;
        }

        public static string Describe(LevelDescription level, int index)
        {
            var p = level.Primitives[index];
            return $"{p.OwnerKind} {p.OwnerId} {p.Role}";
        }

        // ------------------------------------------------------------------ solids

        private struct Bounds3
        {
            public double minX, minY, minZ, maxX, maxY, maxZ;

            /// <summary>Overlapping or touching, within <paramref name="margin"/>: faces lying in one plane need testing too.</summary>
            public bool Overlaps(in Bounds3 o, double margin) =>
                minX <= o.maxX + margin && o.minX <= maxX + margin && minY <= o.maxY + margin && o.minY <= maxY + margin
                && minZ <= o.maxZ + margin && o.minZ <= maxZ + margin;
        }

        /// <summary>A convex polyhedron: its faces, each a convex polygon wound counter-clockwise seen from outside.</summary>
        private sealed class Solid
        {
            public int Primitive;
            public List<Face> Faces = new();
            public Bounds3 Bounds;
        }

        private sealed class Face
        {
            public List<Vector3d> Points;
            public Vector3d Normal;
            public double Offset;
            public double Area;
        }

        private static void AddSolids(LevelDescription level, int index, List<Solid> solids)
        {
            var p = level.Primitives[index];
            if (p.Shape == PrimitiveShape.Prism)
            {
                var polygon = level.Polygons[p.Polygon];
                if (polygon.Count < 3) return;
                var tops = new Vector3d[polygon.Count];
                var bottoms = new Vector3d[polygon.Count];
                for (int i = 0; i < polygon.Count; i++)
                {
                    var v = polygon[i];
                    tops[i] = new Vector3d(v.x, p.PrismTop(v.x, v.y), v.y);
                    bottoms[i] = new Vector3d(v.x, p.Position.y, v.y);
                }
                var faces = new List<List<Vector3d>> { new(tops), new(bottoms) };
                for (int i = 0; i < polygon.Count; i++)
                {
                    int j = (i + 1) % polygon.Count;
                    faces.Add(new List<Vector3d> { bottoms[i], bottoms[j], tops[j], tops[i] });
                }
                Add(index, faces, solids);
                return;
            }

            var rotation = Quaternion.Euler(0f, p.YawDegrees, 0f);
            Vector3d W(float x, float y, float z) => new(p.Position + rotation * new Vector3(x, y, z));
            float hx = p.Size.x * 0.5f, hz = p.Size.z * 0.5f, h = p.Size.y;
            switch (p.Shape)
            {
                case PrimitiveShape.Ramp:
                    var b0 = W(-hx, 0f, -hz);
                    var b1 = W(hx, 0f, -hz);
                    var b2 = W(hx, 0f, hz);
                    var b3 = W(-hx, 0f, hz);
                    var t2 = W(hx, h, hz);
                    var t3 = W(-hx, h, hz);
                    Add(index, new List<List<Vector3d>>
                    {
                        new() { b0, b1, b2, b3 }, new() { b3, b2, t2, t3 }, new() { b0, t3, t2, b1 },
                        new() { b0, b3, t3 }, new() { b1, t2, b2 },
                    }, solids);
                    break;
                case PrimitiveShape.Stairs:
                    int steps = Mathf.Max(1, p.Steps);
                    float run = p.Size.z / steps;
                    for (int i = 0; i < steps; i++)
                    {
                        float z0 = -hz + i * run;
                        AddBox(index, W, -hx, hx, 0f, h * (i + 1) / steps, z0, z0 + run, solids);
                    }
                    break;
                default:
                    AddBox(index, W, -hx, hx, 0f, h, -hz, hz, solids);
                    break;
            }
        }

        private static void AddBox(int index, Func<float, float, float, Vector3d> w, float x0, float x1, float y0, float y1,
            float z0, float z1, List<Solid> solids)
        {
            var c = new[]
            {
                w(x0, y0, z0), w(x1, y0, z0), w(x1, y0, z1), w(x0, y0, z1),
                w(x0, y1, z0), w(x1, y1, z0), w(x1, y1, z1), w(x0, y1, z1),
            };
            Add(index, new List<List<Vector3d>>
            {
                new() { c[0], c[1], c[2], c[3] }, new() { c[4], c[5], c[6], c[7] },
                new() { c[0], c[1], c[5], c[4] }, new() { c[1], c[2], c[6], c[5] },
                new() { c[2], c[3], c[7], c[6] }, new() { c[3], c[0], c[4], c[7] },
            }, solids);
        }

        /// <summary>Faces in any winding: each is turned to face away from the solid's centre.</summary>
        private static void Add(int index, List<List<Vector3d>> faces, List<Solid> solids)
        {
            var centre = new Vector3d();
            int count = 0;
            foreach (var f in faces)
                foreach (var v in f)
                {
                    centre += v;
                    count++;
                }
            centre /= count;

            var solid = new Solid { Primitive = index };
            var b = new Bounds3
            {
                minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue,
                maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue,
            };
            foreach (var points in faces)
            {
                var face = MakeFace(points, centre);
                if (face == null) continue;
                solid.Faces.Add(face);
                foreach (var v in face.Points)
                {
                    b.minX = Math.Min(b.minX, v.x); b.maxX = Math.Max(b.maxX, v.x);
                    b.minY = Math.Min(b.minY, v.y); b.maxY = Math.Max(b.maxY, v.y);
                    b.minZ = Math.Min(b.minZ, v.z); b.maxZ = Math.Max(b.maxZ, v.z);
                }
            }
            // A sliver can't overlap anything by more than its own volume, and its faces' planes are mostly rounding.
            if (solid.Faces.Count < 4 || Volume(solid.Faces.ConvertAll(f => f.Points), out _) <= MaxVolume) return;
            solid.Bounds = b;
            solids.Add(solid);
        }

        /// <summary>A face's plane by Newell's method, facing away from <paramref name="inside"/>; null if degenerate.</summary>
        private static Face MakeFace(List<Vector3d> points, Vector3d inside)
        {
            var normal = new Vector3d();
            var centre = new Vector3d();
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
                centre += a;
            }
            centre /= points.Count;
            double length = normal.Length;
            if (length < 1e-12) return null;
            normal /= length;
            var ordered = new List<Vector3d>(points);
            if (Vector3d.Dot(normal, centre - inside) < 0)
            {
                normal = -normal;
                ordered.Reverse();
            }
            return new Face { Points = ordered, Normal = normal, Offset = Vector3d.Dot(normal, centre), Area = length * 0.5 };
        }

        // ------------------------------------------------------------------ the tests

        private static bool Test(Solid a, Solid b, out Overlap overlap)
        {
            overlap = default;
            // Volume: clip a by each of b's planes.
            var clipped = a.Faces.ConvertAll(f => f.Points);
            foreach (var plane in b.Faces)
            {
                clipped = Clip(clipped, plane.Normal, plane.Offset);
                if (clipped.Count < 4) break;
            }
            if (clipped.Count >= 4)
            {
                double volume = Volume(clipped, out var where);
                if (volume > MaxVolume && 2 * volume / SurfaceArea(clipped) > MinThickness)
                {
                    overlap.Volume = volume;
                    overlap.Where = where.ToVector3();
                    return true;
                }
            }

            // Faces in one plane, facing the same way.
            foreach (var fa in a.Faces)
                foreach (var fb in b.Faces)
                {
                    if (fa.Area <= MaxArea || fb.Area <= MaxArea) continue;
                    if (Vector3d.Dot(fa.Normal, fb.Normal) < 1 - 1e-6) continue;
                    if (Math.Abs(fa.Offset - fb.Offset) > PlaneTolerance) continue;
                    // Every corner of each within the tolerance of the other's plane: a sliver's far side can lie
                    // within it of a neighbour's plane at one end only.
                    if (!Within(fb.Points, fa) || !Within(fa.Points, fb)) continue;
                    double area = OverlapArea(fa, fb, out var where);
                    if (area <= MaxArea) continue;
                    overlap.Area = area;
                    overlap.Where = where.ToVector3();
                    return true;
                }
            return false;
        }

        /// <summary>The part of a closed convex polyhedron (its faces) behind a plane, closed with a cap face.</summary>
        private static List<List<Vector3d>> Clip(List<List<Vector3d>> faces, Vector3d normal, double offset)
        {
            // Nothing beyond the plane: the solid is whole. Capping it anyway would add a second face over any face of
            // it lying in the plane, and count that face's part of the volume twice.
            bool beyond = false;
            foreach (var face in faces)
                foreach (var p in face)
                    beyond |= Vector3d.Dot(normal, p) - offset > 0;
            if (!beyond) return faces;
            var result = new List<List<Vector3d>>();
            var cap = new List<Vector3d>();
            foreach (var face in faces)
            {
                var kept = new List<Vector3d>();
                for (int i = 0; i < face.Count; i++)
                {
                    var p = face[i];
                    var q = face[(i + 1) % face.Count];
                    double dp = Vector3d.Dot(normal, p) - offset, dq = Vector3d.Dot(normal, q) - offset;
                    if (dp <= 0) kept.Add(p);
                    if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
                    {
                        var x = p + (q - p) * (dp / (dp - dq));
                        kept.Add(x);
                        cap.Add(x);
                    }
                    else if (dp == 0) cap.Add(p);
                }
                if (kept.Count >= 3) result.Add(kept);
            }
            if (cap.Count >= 3) result.Add(OrderOnPlane(cap, normal));
            return result;
        }

        /// <summary>Points on a plane, in order round their centre (duplicates and all: they only add zero area).</summary>
        private static List<Vector3d> OrderOnPlane(List<Vector3d> points, Vector3d normal)
        {
            var centre = new Vector3d();
            foreach (var p in points) centre += p;
            centre /= points.Count;
            var u = Vector3d.Cross(normal, Math.Abs(normal.y) < 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0)).Normalized;
            var v = Vector3d.Cross(normal, u);
            var sorted = new List<Vector3d>(points);
            sorted.Sort((p, q) => Math.Atan2(Vector3d.Dot(p - centre, v), Vector3d.Dot(p - centre, u))
                .CompareTo(Math.Atan2(Vector3d.Dot(q - centre, v), Vector3d.Dot(q - centre, u))));
            return sorted;
        }

        /// <summary>A closed polyhedron's volume (faces in any consistent-or-not winding: each is a fan from an inner
        /// point, so the sign of each tetrahedron is made positive) and its centre.</summary>
        private static double Volume(List<List<Vector3d>> faces, out Vector3d centre)
        {
            centre = new Vector3d();
            int count = 0;
            foreach (var f in faces)
                foreach (var p in f)
                {
                    centre += p;
                    count++;
                }
            centre /= count;
            double volume = 0;
            foreach (var f in faces)
                for (int i = 1; i + 1 < f.Count; i++)
                    volume += Math.Abs(Vector3d.Dot(f[0] - centre, Vector3d.Cross(f[i] - centre, f[i + 1] - centre))) / 6.0;
            return volume;
        }

        private static bool Within(List<Vector3d> points, Face plane)
        {
            foreach (var p in points)
                if (Math.Abs(Vector3d.Dot(plane.Normal, p) - plane.Offset) > PlaneTolerance) return false;
            return true;
        }

        private static double SurfaceArea(List<List<Vector3d>> faces)
        {
            double area = 0;
            foreach (var f in faces)
            {
                var sum = new Vector3d();
                for (int i = 0; i < f.Count; i++) sum += Vector3d.Cross(f[i], f[(i + 1) % f.Count]);
                area += sum.Length * 0.5;
            }
            return Math.Max(area, 1e-12);
        }

        /// <summary>The area two coplanar convex faces share, found by clipping one by the other's edges.</summary>
        private static double OverlapArea(Face a, Face b, out Vector3d centre)
        {
            var n = a.Normal;
            var shared = new List<Vector3d>(a.Points);
            for (int i = 0; i < b.Points.Count && shared.Count >= 3; i++)
            {
                var p = b.Points[i];
                var q = b.Points[(i + 1) % b.Points.Count];
                // b is counter-clockwise seen from outside, so its inside is to the left of each edge.
                var outward = Vector3d.Cross(q - p, n).Normalized;
                double offset = Vector3d.Dot(outward, p);
                var kept = new List<Vector3d>();
                for (int k = 0; k < shared.Count; k++)
                {
                    var s = shared[k];
                    var t = shared[(k + 1) % shared.Count];
                    double ds = Vector3d.Dot(outward, s) - offset, dt = Vector3d.Dot(outward, t) - offset;
                    if (ds <= 0) kept.Add(s);
                    if ((ds < 0 && dt > 0) || (ds > 0 && dt < 0)) kept.Add(s + (t - s) * (ds / (ds - dt)));
                }
                shared = kept;
            }
            centre = new Vector3d();
            if (shared.Count < 3) return 0;
            foreach (var p in shared) centre += p;
            centre /= shared.Count;
            var sum = new Vector3d();
            double perimeter = 0;
            for (int i = 0; i < shared.Count; i++)
            {
                sum += Vector3d.Cross(shared[i], shared[(i + 1) % shared.Count]);
                perimeter += (shared[(i + 1) % shared.Count] - shared[i]).Length;
            }
            double area = Math.Abs(Vector3d.Dot(sum, n)) * 0.5;
            // As for volumes: an overlap thinner than MinThickness (twice its area over its perimeter, for a strip) is
            // the rounding along a shared edge.
            return 2 * area / Math.Max(perimeter, 1e-12) > MinThickness ? area : 0;
        }

        private struct Vector3d
        {
            public double x, y, z;

            public Vector3d(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public Vector3d(Vector3 v) : this(v.x, v.y, v.z) { }

            public double Length => Math.Sqrt(x * x + y * y + z * z);
            public Vector3d Normalized => this / Length;
            public Vector3 ToVector3() => new((float)x, (float)y, (float)z);

            public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
            public static Vector3d operator -(Vector3d a) => new(-a.x, -a.y, -a.z);
            public static Vector3d operator *(Vector3d a, double s) => new(a.x * s, a.y * s, a.z * s);
            public static Vector3d operator /(Vector3d a, double s) => new(a.x / s, a.y / s, a.z / s);
            public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

            public static Vector3d Cross(Vector3d a, Vector3d b) =>
                new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }
    }
}
