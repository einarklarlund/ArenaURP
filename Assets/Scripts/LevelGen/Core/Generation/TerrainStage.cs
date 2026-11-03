using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    public enum TerrainOperation : byte
    {
        Raise = 0,
        Tilt = 1,
        RaiseAndTilt = 2,
        Sinkhole = 3,
        Nothing = 4,
    }

    /// <summary>One Voronoi sub-room of a room: a convex cell with a plane of its own.</summary>
    public sealed class SubRoom
    {
        public List<Vector2> Polygon;
        public Vector2 Site;
        /// <summary>The plane's height at the site, and its gradient.</summary>
        public float Height;
        public Vector2 Gradient;
        /// <summary>Overlaps a ramp or bank (or its run-out and flares): it keeps the room's plane and no operation
        /// picks it. The slopes are made after the terrain and fitted to it exactly either way; pinning keeps the ground
        /// round them plain (no hill or valley blend meeting a slope's foot) and their grade the one the plan fitted.</summary>
        public bool Pinned;
        /// <summary>Touches the room's edge, so it can't be a sinkhole.</summary>
        public bool OnEdge;
        public bool Sinkhole;
        /// <summary>A raise, tilt or raise-and-tilt operation picked it (and stuck). Only these can be mounds or dips.</summary>
        public bool Picked;

        public float PlaneAt(Vector2 p) => Height + Vector2.Dot(Gradient, p - Site);
    }

    /// <summary>A room's uneven ground: its sub-rooms and the triangles they are drawn as.</summary>
    public sealed class TerrainPlan
    {
        public int Plot;
        public PlotGround Base;
        public List<SubRoom> Cells = new();
        /// <summary>Fan triangles, counter-clockwise on XZ; y is the ground height at each corner.</summary>
        public List<(Vector3 a, Vector3 b, Vector3 c)> Triangles = new();
        /// <summary>Per triangle, the index of the sub-room it belongs to.</summary>
        public List<int> TriangleCells = new();
        public List<TerrainOperation> Applied = new();
        /// <summary>Operations undone because they made a triangle too steep or too high.</summary>
        public int Undone;

        /// <summary>Ground height at a point of the room (the triangle under it; the base plane if none is).</summary>
        public float HeightAt(Vector2 p)
        {
            foreach (var (a, b, c) in Triangles)
            {
                var pa = new Vector2(a.x, a.z);
                var pb = new Vector2(b.x, b.z);
                var pc = new Vector2(c.x, c.z);
                float d = Cross(pb - pa, pc - pa);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float u = Cross(p - pa, pc - pa) / d, v = Cross(pb - pa, p - pa) / d;
                if (u < -1e-4f || v < -1e-4f || u + v > 1f + 1e-4f) continue;
                return a.y + u * (b.y - a.y) + v * (c.y - a.y);
            }
            return Base.At(p);
        }

        /// <summary>Above this bulge a picked sub-room is a mound; below its negative, a dip.</summary>
        public const float BulgeThreshold = 0.05f;

        /// <summary>
        /// Legacy sub-room tags retained for description compatibility and diagnostics. Hill/valley rendering is
        /// assigned independently by TerrainBlendStage from final height relative to the original plot plane.
        /// A sinkhole is always tagged as one; a sub-room an operation picked is a mound or a dip when its
        /// bulge (its site's height minus the mean of its corners') is past ±<see cref="BulgeThreshold"/>; anything
        /// else is ground. Its triangles all start at the site, so the first gives the site's height.
        /// </summary>
        public SurfaceTag SurfaceOf(int cell)
        {
            var subRoom = Cells[cell];
            if (subRoom.Sinkhole) return SurfaceTag.GroundSinkhole;
            if (!subRoom.Picked) return SurfaceTag.Ground;
            float site = float.NaN, corners = 0f;
            int count = 0;
            for (int i = 0; i < Triangles.Count; i++)
            {
                if (TriangleCells[i] != cell) continue;
                site = Triangles[i].a.y;
                corners += Triangles[i].b.y;
                count++;
            }
            if (count == 0) return SurfaceTag.Ground;
            float bulge = site - corners / count;
            return bulge > BulgeThreshold ? SurfaceTag.GroundMound
                : bulge < -BulgeThreshold ? SurfaceTag.GroundDip : SurfaceTag.Ground;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// The ground as it ends up: each plot's terrain where it has one, else its plane. Ramps and banks are fitted to it.
    /// </summary>
    public sealed class GroundSurface
    {
        private readonly PlotPlan _plan;
        private readonly Dictionary<int, TerrainPlan> _terrains;

        public GroundSurface(PlotPlan plan, Dictionary<int, TerrainPlan> terrains)
        {
            _plan = plan;
            _terrains = terrains;
        }

        public float At(int plot, Vector2 p) =>
            _terrains.TryGetValue(plot, out var terrain) ? terrain.HeightAt(p) : _plan.Ground[plot].At(p);

        /// <summary>Where segment <paramref name="a"/>–<paramref name="b"/> crosses an edge of the plot's terrain
        /// triangles, as fractions of the way along it (strictly between 0 and 1, unsorted, possibly repeated). Between
        /// two of them the ground under the segment is linear.</summary>
        public void Crossings(int plot, Vector2 a, Vector2 b, List<float> into)
        {
            if (!_terrains.TryGetValue(plot, out var terrain)) return;
            var ab = b - a;
            foreach (var (t0, t1, t2) in terrain.Triangles)
            {
                var corners = new[] { new Vector2(t0.x, t0.z), new Vector2(t1.x, t1.z), new Vector2(t2.x, t2.z) };
                for (int k = 0; k < 3; k++)
                {
                    var p = corners[k];
                    var pq = corners[(k + 1) % 3] - p;
                    float denominator = Cross(ab, pq);
                    if (Mathf.Abs(denominator) < 1e-9f) continue;
                    float x = Cross(p - a, pq) / denominator;
                    float y = Cross(p - a, ab) / denominator;
                    if (x > 1e-5f && x < 1f - 1e-5f && y >= -1e-5f && y <= 1f + 1e-5f) into.Add(x);
                }
            }
        }

        /// <summary>
        /// Whether the ground under a convex polygon is level, and its height there: the same, within
        /// <see cref="LevelTolerance"/>, at the polygon's corners, at every terrain corner inside it and wherever its
        /// edges cross a terrain edge. The ground is linear between those, so a flat-bottomed solid standing at that
        /// height meets it face to face.
        /// </summary>
        public bool Level(int plot, List<Vector2> polygon, out float height)
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            void Sample(Vector2 p)
            {
                float h = At(plot, p);
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
            foreach (var v in polygon) Sample(v);
            if (_terrains.TryGetValue(plot, out var terrain))
            {
                foreach (var (a, b, c) in terrain.Triangles)
                    foreach (var corner in new[] { a, b, c })
                    {
                        var p = new Vector2(corner.x, corner.z);
                        if (Polygon.Contains(polygon, p)) Sample(p);
                    }
                var crossings = new List<float>();
                for (int k = 0; k < polygon.Count; k++)
                {
                    var from = polygon[k];
                    var to = polygon[(k + 1) % polygon.Count];
                    crossings.Clear();
                    Crossings(plot, from, to, crossings);
                    foreach (float x in crossings) Sample(Vector2.Lerp(from, to, x));
                }
            }
            height = high;
            return high - low <= LevelTolerance;
        }

        /// <summary>How far from level the ground under a prop may be.</summary>
        public const float LevelTolerance = 1e-4f;

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// Stage 5, terrain: the ground inside each room without a building is split into Voronoi sub-rooms (about one per
    /// <c>subRoomArea</c>) and reshaped by a few operations. Each picks a sub-room and raises or lowers it, tilts it,
    /// does both, makes it a sinkhole, or does nothing. The ground stays continuous: every sub-room has a plane, each
    /// shared corner takes the mean of the planes meeting there, and each sub-room is drawn as a fan of triangles from
    /// its site, so a raised or tilted sub-room bends its neighbours with it. A sinkhole is lowered by
    /// <c>sinkholeDepth</c> with corners of its own, so its rim is a ledge all round.
    /// <para>
    /// The room's edges keep the room's plane, and so do the sub-rooms under its ramps and banks, so the transitions
    /// stage 3 chose between rooms still fit. An operation that makes any triangle steeper than <c>maxGrade</c>, or
    /// lifts it over <c>maxHeight</c>, is undone.
    /// </para>
    /// </summary>
    public static class TerrainStage
    {
        public static List<TerrainPlan> Build(PlotPartition partition, PlotPlan plan, in LevelGenSettings s, ulong seed)
        {
            var terrains = new List<TerrainPlan>();
            var t = s.Terrain;
            for (int room = 0; room < partition.Polygons.Count; room++)
            {
                if (plan.BuildingPlots.Contains(room)) continue;
                var rng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Terrain, room), GenStage.Terrain);
                var polygon = partition.Polygons[room];
                float area = Polygon.Area(polygon);
                int count = Mathf.Max(1, Mathf.RoundToInt(area / t.SubRoomArea));
                var sites = Sites(polygon, count, Mathf.Sqrt(area / count), ref rng);
                var cells = PartitionStage.Voronoi(polygon, sites);

                var terrain = new TerrainPlan { Plot = room, Base = plan.Ground[room] };
                var zones = new List<List<Vector2>>();
                foreach (var segment in plan.Segments)
                    if (segment.Lower == room && TransitionStage.IsSlope(segment.Type))
                    {
                        var zone = TransitionStage.SlopeOutline(partition, segment, 1f, s.TransitionSizes.RampWidth,
                            TransitionStage.FlareReach(segment, s) + 1f);
                        if (zone.Count >= 3) zones.Add(zone);
                    }
                for (int i = 0; i < cells.Count; i++)
                {
                    if (cells[i] == null) continue;
                    var cell = new SubRoom
                    {
                        Polygon = cells[i], Site = sites[i], Height = terrain.Base.At(sites[i]), Gradient = terrain.Base.Slope,
                    };
                    foreach (var zone in zones) cell.Pinned |= Polygon.Area(Polygon.ClipConvex(cell.Polygon, zone)) > 1e-3f;
                    foreach (var v in cell.Polygon) cell.OnEdge |= OnEdge(polygon, v);
                    terrain.Cells.Add(cell);
                }
                terrains.Add(terrain);
            }

            // Carve-line ends and the like: every cell corner on a room's edge goes into the neighbouring polygons too,
            // so all ground polygons share their edges' endpoints exactly.
            var polygons = new List<List<Vector2>>();
            var terrainRooms = new HashSet<int>();
            foreach (var terrain in terrains)
            {
                terrainRooms.Add(terrain.Plot);
                foreach (var cell in terrain.Cells) polygons.Add(cell.Polygon);
            }
            for (int room = 0; room < partition.Polygons.Count; room++) polygons.Add(partition.Polygons[room]);
            InsertJunctions(polygons);
            int index = 0;
            foreach (var terrain in terrains)
                foreach (var cell in terrain.Cells)
                    cell.Polygon = polygons[index++];
            for (int room = 0; room < partition.Polygons.Count; room++) partition.Polygons[room] = polygons[index++];

            foreach (var terrain in terrains)
            {
                var rng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Terrain, 1000 + terrain.Plot), GenStage.Terrain);
                Shape(terrain, partition.Polygons[terrain.Plot], s, ref rng);
            }
            return terrains;
        }

        /// <summary>Dart throwing inside the room (inset a little), at least 0.6 spacings apart, shrinking as needed.</summary>
        private static List<Vector2> Sites(List<Vector2> polygon, int count, float spacing, ref Pcg32 rng)
        {
            var room = Polygon.Inset(polygon, 0.5f);
            if (room.Count < 3) room = polygon;
            Polygon.Extent(room, Vector2.right, out float x0, out float x1);
            Polygon.Extent(room, Vector2.up, out float z0, out float z1);
            float minDistance = 0.6f * spacing;
            var sites = new List<Vector2>(count);
            int misses = 0;
            while (sites.Count < count)
            {
                var p = new Vector2(rng.Range(x0, x1), rng.Range(z0, z1));
                if (!Polygon.Contains(room, p)) continue;
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

        /// <summary>The operations, each undone if it makes the ground too steep or too high.</summary>
        private static void Shape(TerrainPlan terrain, List<Vector2> room, in LevelGenSettings s, ref Pcg32 rng)
        {
            var t = s.Terrain;
            float maxGrade = Mathf.Tan(t.MaxGradeDegrees * Mathf.Deg2Rad);
            float total = t.RaiseWeight + t.TiltWeight + t.RaiseAndTiltWeight + t.SinkholeWeight + t.NothingWeight;
            int operations = rng.Range(t.MinOperations, t.MaxOperations + 1);
            Triangulate(terrain, room, s);

            for (int op = 0; op < operations; op++)
            {
                // Draw every number up front, so one operation's choice doesn't shift the next one's.
                float pick = rng.NextFloat() * total;
                float cellPick = rng.NextFloat();
                float heightChange = rng.Range(t.MinHeightChange, t.MaxHeightChange) * (rng.Chance(0.5f) ? 1f : -1f);
                float tiltAngle = rng.Range(0f, 2f * Mathf.PI);
                float tilt = Mathf.Tan(rng.Range(t.MinTiltDegrees, t.MaxTiltDegrees) * Mathf.Deg2Rad);
                if (total <= 0f) break;

                var kind = Choose(pick, t);
                var candidates = terrain.Cells.FindAll(c => !c.Pinned && !(kind == TerrainOperation.Sinkhole && (c.OnEdge || c.Sinkhole)));
                if (kind == TerrainOperation.Nothing || candidates.Count == 0)
                {
                    terrain.Applied.Add(TerrainOperation.Nothing);
                    continue;
                }
                var cell = candidates[Mathf.Min(candidates.Count - 1, (int)(cellPick * candidates.Count))];
                float height = cell.Height;
                var gradient = cell.Gradient;
                if (kind is TerrainOperation.Raise or TerrainOperation.RaiseAndTilt) cell.Height += heightChange;
                if (kind is TerrainOperation.Tilt or TerrainOperation.RaiseAndTilt)
                    cell.Gradient += new Vector2(Mathf.Cos(tiltAngle), Mathf.Sin(tiltAngle)) * tilt;
                if (kind == TerrainOperation.Sinkhole) cell.Sinkhole = true;

                Triangulate(terrain, room, s);
                if (Fits(terrain, maxGrade, s.MaxHeight))
                {
                    cell.Picked |= kind is TerrainOperation.Raise or TerrainOperation.Tilt or TerrainOperation.RaiseAndTilt;
                    terrain.Applied.Add(kind);
                    continue;
                }
                cell.Height = height;
                cell.Gradient = gradient;
                if (kind == TerrainOperation.Sinkhole) cell.Sinkhole = false;
                terrain.Undone++;
                Triangulate(terrain, room, s);
            }
        }

        private static TerrainOperation Choose(float pick, in TerrainSettings t)
        {
            if ((pick -= t.RaiseWeight) < 0f) return TerrainOperation.Raise;
            if ((pick -= t.TiltWeight) < 0f) return TerrainOperation.Tilt;
            if ((pick -= t.RaiseAndTiltWeight) < 0f) return TerrainOperation.RaiseAndTilt;
            if ((pick -= t.SinkholeWeight) < 0f) return TerrainOperation.Sinkhole;
            return TerrainOperation.Nothing;
        }

        /// <summary>
        /// Corner heights and the triangles. A corner on the room's edge, or of a pinned sub-room, takes the room's
        /// plane; any other the mean of the planes of the sub-rooms meeting there. A sinkhole's own triangles sit
        /// <c>sinkholeDepth</c> lower.
        /// </summary>
        private static void Triangulate(TerrainPlan terrain, List<Vector2> room, in LevelGenSettings s)
        {
            var sums = new Dictionary<Vector2, (float sum, int count, bool pinned)>();
            foreach (var cell in terrain.Cells)
                foreach (var v in cell.Polygon)
                {
                    sums.TryGetValue(v, out var entry);
                    bool pinned = entry.pinned || cell.Pinned || OnEdge(room, v);
                    sums[v] = (entry.sum + cell.PlaneAt(v), entry.count + 1, pinned);
                }
            float depth = s.Terrain.SinkholeDepth;
            terrain.Triangles.Clear();
            terrain.TriangleCells.Clear();
            for (int index = 0; index < terrain.Cells.Count; index++)
            {
                var cell = terrain.Cells[index];
                float drop = cell.Sinkhole ? depth : 0f;
                var site = new Vector3(cell.Site.x, cell.PlaneAt(cell.Site) - drop, cell.Site.y);
                for (int k = 0; k < cell.Polygon.Count; k++)
                {
                    var a = cell.Polygon[k];
                    var b = cell.Polygon[(k + 1) % cell.Polygon.Count];
                    if ((b - a).sqrMagnitude < 1e-10f) continue;
                    terrain.Triangles.Add((site, Corner(a, sums, terrain.Base, drop), Corner(b, sums, terrain.Base, drop)));
                    terrain.TriangleCells.Add(index);
                }
            }
        }

        private static Vector3 Corner(Vector2 v, Dictionary<Vector2, (float sum, int count, bool pinned)> sums, PlotGround ground,
            float drop)
        {
            var (sum, count, pinned) = sums[v];
            float height = pinned ? ground.At(v) : sum / count;
            return new Vector3(v.x, height - drop, v.y);
        }

        private static bool Fits(TerrainPlan terrain, float maxGrade, float maxHeight)
        {
            foreach (var triangle in terrain.Triangles)
            {
                if (Gradient(triangle, out _).magnitude > maxGrade) return false;
                if (triangle.a.y > maxHeight || triangle.b.y > maxHeight || triangle.c.y > maxHeight) return false;
            }
            return true;
        }

        /// <summary>The plane through a triangle's corners: its gradient, and its height at the centroid.</summary>
        public static Vector2 Gradient((Vector3 a, Vector3 b, Vector3 c) t, out float centroidHeight)
        {
            var ab = t.b - t.a;
            var ac = t.c - t.a;
            float det = ab.x * ac.z - ab.z * ac.x;
            centroidHeight = (t.a.y + t.b.y + t.c.y) / 3f;
            if (Mathf.Abs(det) < 1e-9f) return Vector2.zero;
            float gx = (ab.y * ac.z - ab.z * ac.y) / det;
            float gz = (ab.x * ac.y - ab.y * ac.x) / det;
            return new Vector2(gx, gz);
        }

        private static bool OnEdge(List<Vector2> polygon, Vector2 p)
        {
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var b = polygon[(k + 1) % polygon.Count];
                var ab = b - a;
                float length = ab.magnitude;
                if (length < 1e-6f) continue;
                if (Mathf.Abs(ab.x * (p - a).y - ab.y * (p - a).x) / length > 1e-3f) continue;
                float t = Vector2.Dot(p - a, ab) / (length * length);
                if (t >= -1e-4f && t <= 1f + 1e-4f) return true;
            }
            return false;
        }

        /// <summary>Inserts into each polygon every vertex of all of them that lies inside one of its edges, in order.</summary>
        private static void InsertJunctions(List<List<Vector2>> polygons)
        {
            var points = new HashSet<Vector2>();
            foreach (var polygon in polygons) points.UnionWith(polygon);
            for (int i = 0; i < polygons.Count; i++)
            {
                var polygon = polygons[i];
                Polygon.Extent(polygon, Vector2.right, out float x0, out float x1);
                Polygon.Extent(polygon, Vector2.up, out float z0, out float z1);
                var near = new List<Vector2>();
                foreach (var p in points)
                    if (p.x >= x0 - 1e-3f && p.x <= x1 + 1e-3f && p.y >= z0 - 1e-3f && p.y <= z1 + 1e-3f) near.Add(p);
                var result = new List<Vector2>();
                for (int k = 0; k < polygon.Count; k++)
                {
                    var a = polygon[k];
                    var b = polygon[(k + 1) % polygon.Count];
                    result.Add(a);
                    var ab = b - a;
                    float length = ab.magnitude;
                    if (length < 1e-6f) continue;
                    var dir = ab / length;
                    var inner = new List<(float t, Vector2 p)>();
                    foreach (var p in near)
                    {
                        float t = Vector2.Dot(p - a, dir);
                        if (t <= 1e-3f || t >= length - 1e-3f) continue;
                        if (Mathf.Abs(dir.x * (p - a).y - dir.y * (p - a).x) > 1e-3f) continue;
                        inner.Add((t, p));
                    }
                    inner.Sort((x, y) => x.t.CompareTo(y.t));
                    foreach (var (_, p) in inner) result.Add(p);
                }
                polygons[i] = result;
            }
        }

        /// <summary>Each triangle as a prism of its own, solid from below the ground up to its plane.</summary>
        public static void Emit(LevelDescription level, List<TerrainPlan> terrains, PlotPartition partition)
        {
            foreach (var terrain in terrains)
            {
                int id = partition.IdOf(terrain.Plot);
                var tops = new SurfaceTag[terrain.Cells.Count];
                for (int cell = 0; cell < tops.Length; cell++)
                {
                    tops[cell] = terrain.SurfaceOf(cell);
                    level.SubRooms.Add(new SubRoomDescription
                    {
                        Plot = id, Polygon = level.AddPolygon(terrain.Cells[cell].Polygon), Surface = tops[cell],
                        Site = terrain.Cells[cell].Site,
                    });
                }
                // All cells share a foundation below the room's lowest point. A shallow upper prism would otherwise
                // end above a neighboring sinkhole's floor, leaving the lower part of its cliff open to the void.
                float bottom = TransitionStage.GroundBottom;
                foreach (var (a, b, c) in terrain.Triangles)
                    bottom = Mathf.Min(bottom, Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - LevelGenSettings.SlabThickness);
                for (int i = 0; i < terrain.Triangles.Count; i++)
                {
                    var triangle = terrain.Triangles[i];
                    var gradient = Gradient(triangle, out float centroidHeight);
                    var outline = new List<Vector2> { new(triangle.a.x, triangle.a.z), new(triangle.b.x, triangle.b.z), new(triangle.c.x, triangle.c.z) };
                    if (Polygon.SignedArea(outline) < 0f) outline.Reverse();
                    level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(outline), bottom, centroidHeight, gradient, OwnerKind.Plot, id,
                        Surfaces.Of(tops[terrain.TriangleCells[i]], SurfaceTag.CliffFace, SurfaceTag.CliffFace));
                }
            }
        }
    }
}
