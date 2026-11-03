using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    public enum DecalType : byte
    {
        Ledge = 0,
        DoorFrame = 1,
        WindowFrame = 2,
        WallToCeiling = 3,
        Roofline = 4,
        WallCorner = 5,
        FloorSkirting = 6,
        GroundSkirting = 7,
        TreeBase = 8,
        CarSurround = 9,
        DipEdge = 10,
        MoundEdge = 11,
    }

    public static class DecalTypes
    {
        public const int Count = 12;

        /// <summary>A building's decal: its material comes from the building's profile, not the theme.</summary>
        public static bool IsBuilding(DecalType t) => t is >= DecalType.DoorFrame and <= DecalType.GroundSkirting;
    }

    /// <summary>What the decal pass reads besides the description: the theme's widths and toggles. Visual only.</summary>
    [Serializable]
    public struct DecalSettings
    {
        /// <summary>Width per <see cref="DecalType"/>. A type with width 0 makes no strips. A tree base's width is
        /// <see cref="TreeBaseSize"/>.</summary>
        public float[] Widths;
        /// <summary>Ledge decals on cliff tops too, not just on ledges and sinkhole rims.</summary>
        public bool CliffTops;
        public float TreeBaseSize;
        /// <summary>How far outside a car's footprint its surround starts.</summary>
        public float CarMargin;
        /// <summary>The ground is drawn blended (<see cref="TerrainBlendStage"/>), which makes the dip and mound edges,
        /// the sinkhole rims and the ledge and cliff lips: they get no strips.</summary>
        public bool BlendedGround;

        public float Width(DecalType t) =>
            t == DecalType.TreeBase ? TreeBaseSize : Widths != null && (int)t < Widths.Length ? Widths[(int)t] : 0f;

        public static DecalSettings Default => new()
        {
            Widths = new[] { 0.3f, 0.15f, 0.12f, 0.2f, 0.3f, 0.2f, 0.12f, 0.4f, 0f, 0.25f, 0.3f, 0.3f },
            CliffTops = false,
            TreeBaseSize = 1.6f,
            CarMargin = 0.3f,
        };
    }

    /// <summary>
    /// One decal strip: a band on a surface. <see cref="Left"/> and <see cref="Right"/> are its two edges, point for
    /// point; <see cref="Points"/> is the polyline down its middle. The mesher lifts it 0.01 along <see cref="Normal"/>;
    /// The mesher maps it in world units: u is the distance along it, and v the distance across it, from
    /// <see cref="V0"/> × <see cref="Width"/> at the left edge to <see cref="V1"/> × <see cref="Width"/> at the right.
    /// </summary>
    public sealed class DecalStrip
    {
        public DecalType Type;
        /// <summary>The building whose profile supplies the material, or -1.</summary>
        public int Building = -1;
        public Vector3 Normal;
        public float Width;
        public float V0;
        public float V1 = 1f;
        public List<Vector3> Points = new();
        public List<Vector3> Left = new();
        public List<Vector3> Right = new();
    }

    /// <summary>
    /// The decal pass: every decal strip of a finished description, from the description alone. The builder runs it on
    /// every peer after building geometry; it is no part of the description or its hash. Strips on outdoor ground are
    /// cut every <see cref="GroundStep"/> and take the ground's height (the plot, terrain and slope prisms) at each
    /// point; strips stop at doors, windows, entrances and stairs.
    /// </summary>
    public static class DecalStage
    {
        public const float GroundStep = 0.5f;
        /// <summary>Ground samples are taken this far into the strip from its edges, so an edge on a drop-off reads the
        /// strip's own side.</summary>
        private const float SampleInset = 0.02f;
        private const float Shortest = 0.05f;

        public static List<DecalStrip> Build(LevelDescription level, in DecalSettings settings)
        {
            var strips = new List<DecalStrip>();
            var ground = new GroundSampler(level);
            var context = new Context { Level = level, Ground = ground, Strips = strips, Settings = settings };

            Ledges(context);
            SubRoomEdges(context);
            foreach (var building in level.Buildings) BuildingDecals(context, building);
            PropDecals(context);
            return strips;
        }

        private sealed class Context
        {
            public LevelDescription Level;
            public GroundSampler Ground;
            public List<DecalStrip> Strips;
            public DecalSettings Settings;

            public float Width(DecalType t) => Settings.Width(t);
        }

        // ---------------------------------------------------------------- outdoor ground

        /// <summary>The top edge of every ledge (and cliff, with <c>cliffTops</c>), on the higher plot.</summary>
        private static void Ledges(Context c)
        {
            float w = c.Width(DecalType.Ledge);
            // Blended ground draws every edge here as a layer instead: ledge and cliff lips, and sinkhole rims.
            if (w <= 0f || c.Settings.BlendedGround) return;
            foreach (var t in c.Level.Transitions)
            {
                if (!(t.Type == TransitionType.Ledge || (c.Settings.CliffTops && t.Type == TransitionType.Cliff))) continue;
                var dir = (t.To - t.From).normalized;
                var normal = new Vector2(-dir.y, dir.x);
                float Rise(Vector2 at, float off = 0.3f) => c.Ground.Height(at + normal * off) - c.Ground.Height(at - normal * off);
                // A sloped plot or a ramp beside the ledge can swap which side is higher along it: split it where they
                // cross, so each piece lies on its own higher side.
                // Close to the ledge, so a steep ramp beside it puts the cut where the two sides actually meet.
                float RiseAt(float f) => Rise(Vector2.Lerp(t.From, t.To, f), 0.05f);
                int samples = Mathf.Max(2, Mathf.CeilToInt((t.To - t.From).magnitude / GroundStep));
                var cuts = new List<float> { 0f };
                float last = RiseAt(0.5f / samples);
                for (int i = 1; i < samples; i++)
                {
                    float f = (i + 0.5f) / samples, rise = RiseAt(f);
                    if (last * rise < 0f)
                    {
                        float lo = f - 1f / samples, hi = f;
                        for (int k = 0; k < 12; k++)
                        {
                            float m = (lo + hi) * 0.5f;
                            if (RiseAt(m) * last < 0f) hi = m;
                            else lo = m;
                        }
                        cuts.Add((lo + hi) * 0.5f);
                    }
                    if (!float.IsNaN(rise)) last = rise;
                }
                cuts.Add(1f);
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    var from = Vector2.Lerp(t.From, t.To, cuts[i]);
                    var to = Vector2.Lerp(t.From, t.To, cuts[i + 1]);
                    float rise = Rise((from + to) * 0.5f);
                    if (float.IsNaN(rise)) continue;
                    var up = rise >= 0f ? normal : -normal;
                    GroundBand(c, DecalType.Ledge, -1, w, from, to, from + up * w, to + up * w);
                }
            }
            // Sinkhole rims: round each sinkhole's outline, on the higher ground outside it.
            foreach (var room in c.Level.SubRooms)
                if (room.Surface == SurfaceTag.GroundSinkhole)
                    GroundLoop(c, DecalType.Ledge, -1, w, c.Level.Polygons[room.Polygon], 0f, w);
        }

        /// <summary>Just inside every dip's and mound's outline.</summary>
        private static void SubRoomEdges(Context c)
        {
            if (c.Settings.BlendedGround) return;
            foreach (var room in c.Level.SubRooms)
            {
                var type = room.Surface switch
                {
                    SurfaceTag.GroundDip => DecalType.DipEdge,
                    SurfaceTag.GroundMound => DecalType.MoundEdge,
                    _ => (DecalType)255,
                };
                if ((int)type == 255) continue;
                var outline = c.Level.Polygons[room.Polygon];
                // No wider than a fifth of the sub-room, so the inner edge never crosses itself.
                float w = Mathf.Min(c.Width(type), 0.2f * Polygon.MinWidth(outline));
                if (w <= 0f) continue;
                GroundLoop(c, type, -1, w, outline, 0f, -w);
            }
        }

        private static void PropDecals(Context c)
        {
            float size = c.Settings.TreeBaseSize;
            float carWidth = c.Width(DecalType.CarSurround);
            foreach (var prop in c.Level.Props)
            {
                var centre = new Vector2(prop.Position.x, prop.Position.z);
                float yaw = prop.YawDegrees * Mathf.Deg2Rad;
                var along = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                var across = new Vector2(along.y, -along.x);
                if (prop.Kind == PropKind.Tree && size > 0f)
                {
                    // A square, as parallel bands no wider than a ground step, so it follows the ground across too.
                    int bands = Mathf.Max(1, Mathf.CeilToInt(size / GroundStep));
                    for (int j = 0; j < bands; j++)
                    {
                        float o0 = -size * 0.5f + size * j / bands, o1 = -size * 0.5f + size * (j + 1) / bands;
                        var back = centre - along * (size * 0.5f);
                        var front = centre + along * (size * 0.5f);
                        GroundBand(c, DecalType.TreeBase, -1, size, back + across * o0, front + across * o0,
                            back + across * o1, front + across * o1, (float)j / bands, (float)(j + 1) / bands);
                    }
                }
                else if (prop.Kind == PropKind.Car && carWidth > 0f)
                {
                    var body = c.Level.Primitives.Find(p => p.OwnerKind == OwnerKind.Prop && p.OwnerId == prop.Id);
                    var l = along * (body.Size.z * 0.5f);
                    var w = across * (body.Size.x * 0.5f);
                    var rect = Ccw(new List<Vector2> { centre - l - w, centre + l - w, centre + l + w, centre - l + w });
                    GroundLoop(c, DecalType.CarSurround, -1, carWidth, rect, c.Settings.CarMargin, c.Settings.CarMargin + carWidth);
                }
            }
        }

        // ---------------------------------------------------------------- buildings

        private static void BuildingDecals(Context c, BuildingDescription b)
        {
            float h = BuildingStage.WallThickness * 0.5f;
            float hi = BuildingStage.InteriorWallThickness * 0.5f;
            float slab = LevelGenSettings.SlabThickness;
            float plotGround = c.Level.Plots[b.Plot].BaseHeight;
            var core = b.HasCore ? BuildingStage.CoreRect(b.Core) : null;

            for (int k = 0; k < b.Floors; k++)
            {
                var footprint = c.Level.Polygons[b.FloorFootprints[k]];
                var outside = Offset(footprint, h);
                var inside = Offset(footprint, -h);
                var skirtingEdge = Offset(footprint, -h - c.Width(DecalType.FloorSkirting));
                float y = b.FloorY(k);
                float ceiling = b.FloorY(k + 1) - slab;
                float wallBottom = k > 0 ? y : b.Placement == FirstFloor.Stilts ? y - slab : plotGround;
                float wallTop = b.FloorY(k + 1);
                int n = footprint.Count;

                for (int i = 0; i < n; i++)
                {
                    var a = footprint[i];
                    var e = footprint[(i + 1) % n];
                    float length = (e - a).magnitude;
                    if (length < 1e-4f) continue;
                    var dir = (e - a) / length;
                    var outward = new Vector2(dir.y, -dir.x);
                    var openings = b.Openings.FindAll(o => o.Floor == k && o.Side == i);

                    foreach (var o in openings)
                        foreach (float face in new[] { h, -h })
                        {
                            var normal = Up(outward * Mathf.Sign(face));
                            Vector3 At(float t, float height) => On(a + dir * t + outward * face, height);
                            Frame(c, b.Id, o.Door ? DecalType.DoorFrame : DecalType.WindowFrame, normal, At, o.T, o.HalfWidth,
                                y + o.Bottom, y + o.Top, !o.Door);
                        }

                    // Along the inside face: wall-to-ceiling at the top, skirting on the floor at the foot.
                    var i0 = inside[i];
                    var i1 = inside[(i + 1) % n];
                    float insideLength = Vector2.Dot(i1 - i0, dir);
                    float insideStart = Vector2.Dot(i0 - a, dir);
                    var inwardNormal = Up(-outward);
                    float wc = c.Width(DecalType.WallToCeiling);
                    if (wc > 0f)
                    {
                        var blocked = new List<Vector2>();
                        foreach (var o in openings)
                            if (y + o.Top > ceiling - wc) blocked.Add(new Vector2(o.T - o.HalfWidth - insideStart, o.T + o.HalfWidth - insideStart));
                        foreach (var run in Free(insideLength, blocked))
                            WallBand(c, DecalType.WallToCeiling, b.Id, inwardNormal, wc, Lerp(i0, i1, run, insideLength), ceiling, ceiling - wc);
                    }

                    float fs = c.Width(DecalType.FloorSkirting);
                    if (fs > 0f)
                    {
                        var blocked = new List<Vector2>();
                        foreach (var o in openings)
                            if (o.Door) blocked.Add(new Vector2(o.T - o.HalfWidth - insideStart, o.T + o.HalfWidth - insideStart));
                        if (core != null) Blocked(blocked, i0, i1, core, fs);
                        var j0 = skirtingEdge[i];
                        var j1 = skirtingEdge[(i + 1) % n];
                        foreach (var run in Free(insideLength, blocked))
                            FlatBand(c, DecalType.FloorSkirting, b.Id, fs, y, i0, i1, j0, j1, run, insideLength);
                    }

                    // Roofline: the top floor's outside faces, where they meet the roof slab.
                    float rl = c.Width(DecalType.Roofline);
                    if (k == b.Floors - 1 && rl > 0f)
                        WallBand(c, DecalType.Roofline, b.Id, Up(outward), rl, (outside[i], outside[(i + 1) % n]), b.RoofY, b.RoofY - rl);
                }

                // Corners, outside and inside, a half-width on each face.
                float wcw = c.Width(DecalType.WallCorner);
                if (wcw > 0f)
                    for (int i = 0; i < n; i++)
                    {
                        var prev = footprint[i] - footprint[(i + n - 1) % n];
                        var next = footprint[(i + 1) % n] - footprint[i];
                        if (prev.sqrMagnitude < 1e-8f || next.sqrMagnitude < 1e-8f) continue;
                        prev.Normalize();
                        next.Normalize();
                        if (Mathf.Abs(prev.x * next.y - prev.y * next.x) < 1e-3f) continue;
                        var np = new Vector2(prev.y, -prev.x);
                        var nn = new Vector2(next.y, -next.x);
                        Corner(c, b.Id, wcw, outside[i], -prev, Up(np), wallBottom, wallTop);
                        Corner(c, b.Id, wcw, outside[i], next, Up(nn), wallBottom, wallTop);
                        Corner(c, b.Id, wcw, inside[i], -prev, Up(-np), y, ceiling);
                        Corner(c, b.Id, wcw, inside[i], next, Up(-nn), y, ceiling);
                    }

                foreach (var wall in b.Walls)
                    if (wall.Floor == k) InteriorWallDecals(c, b, wall, footprint, y, ceiling, hi, wcw);
            }

            GroundSkirting(c, b, plotGround);
        }

        private static void InteriorWallDecals(Context c, BuildingDescription b, InteriorWallDescription wall,
            List<Vector2> footprint, float y, float ceiling, float hi, float cornerWidth)
        {
            float length = (wall.To - wall.From).magnitude;
            if (length < 1e-3f) return;
            var dir = (wall.To - wall.From) / length;
            var m = new Vector2(-dir.y, dir.x);
            float fs = c.Width(DecalType.FloorSkirting);
            float wc = c.Width(DecalType.WallToCeiling);
            foreach (float s in new[] { 1f, -1f })
            {
                var normal = Up(m * s);
                var offset = m * (s * hi);
                Vector3 At(float t, float height) => On(wall.From + dir * t + offset, height);
                foreach (var gap in wall.Gaps)
                    Frame(c, b.Id, DecalType.DoorFrame, normal, At, (gap.x + gap.y) * 0.5f, (gap.y - gap.x) * 0.5f, y, y + wall.GapTop,
                        false, 0f, length);

                var blocked = new List<Vector2>(wall.Gaps);
                if (wc > 0f)
                {
                    var high = wall.GapTop + y > ceiling - wc ? blocked : new List<Vector2>();
                    foreach (var run in Free(length, high))
                        WallBand(c, DecalType.WallToCeiling, b.Id, normal, wc,
                            (wall.From + offset + dir * run.x, wall.From + offset + dir * run.y), ceiling, ceiling - wc);
                }
                if (fs > 0f)
                {
                    var a0 = wall.From + offset;
                    var a1 = wall.To + offset;
                    foreach (var run in Free(length, blocked))
                        FlatBand(c, DecalType.FloorSkirting, b.Id, fs, y, a0, a1, a0 + m * (s * fs), a1 + m * (s * fs), run, length);
                }

                // Where each end meets an outer wall's inside face: a half-width on each face.
                if (cornerWidth > 0f)
                    foreach (var (end, into) in new[] { (wall.From, dir), (wall.To, -dir) })
                    {
                        var corner = end + offset;
                        Corner(c, b.Id, cornerWidth, corner, into, normal, y, ceiling);
                        int side = NearestSide(footprint, end);
                        var sideDir = (footprint[(side + 1) % footprint.Count] - footprint[side]).normalized;
                        var away = Vector2.Dot(sideDir, m * s) > 0f ? sideDir : -sideDir;
                        Corner(c, b.Id, cornerWidth, corner, away, Up(new Vector2(-sideDir.y, sideDir.x)), y, ceiling);
                    }
            }
        }

        /// <summary>Round the building's ground outline (the plinth or ground-floor walls, or each stilt), outside the
        /// walls' faces, broken at doors on the ground and wherever stairs stand on it.</summary>
        private static void GroundSkirting(Context c, BuildingDescription b, float plotGround)
        {
            float w = c.Width(DecalType.GroundSkirting);
            if (w <= 0f) return;
            if (b.Placement == FirstFloor.Stilts)
            {
                foreach (var p in c.Level.Primitives)
                    if (p.OwnerKind == OwnerKind.Building && p.OwnerId == b.Id && p.Role == PrimitiveRole.Pillar && p.Shape == PrimitiveShape.Prism)
                        GroundLoop(c, DecalType.GroundSkirting, b.Id, w, c.Level.Polygons[p.Polygon], 0f, w);
                return;
            }

            float h = BuildingStage.WallThickness * 0.5f;
            var footprint = c.Level.Polygons[b.FloorFootprints[0]];
            var inner = Offset(footprint, h);
            var outer = Offset(footprint, h + w);
            var stairs = new List<List<Vector2>>();
            foreach (var p in c.Level.Primitives)
                if (p.OwnerKind == OwnerKind.Building && p.OwnerId == b.Id && p.Shape == PrimitiveShape.Stairs
                    && Mathf.Abs(p.Position.y - plotGround) < 0.2f)
                    stairs.Add(Footprint(p));
            int n = footprint.Count;
            for (int i = 0; i < n; i++)
            {
                var a = footprint[i];
                var dir = (footprint[(i + 1) % n] - a).normalized;
                var i0 = inner[i];
                var i1 = inner[(i + 1) % n];
                float length = Vector2.Dot(i1 - i0, dir);
                if (length < Shortest) continue;
                float start = Vector2.Dot(i0 - a, dir);
                var blocked = new List<Vector2>();
                foreach (var o in b.Openings)
                    if (o.Floor == 0 && o.Side == i && o.Door && b.Placement == FirstFloor.Ground)
                        blocked.Add(new Vector2(o.T - o.HalfWidth - start, o.T + o.HalfWidth - start));
                foreach (var flight in stairs) Blocked(blocked, i0, i1, flight, w);
                foreach (var run in Free(length, blocked))
                {
                    var (l0, l1, r0, r1) = Cut(i0, i1, outer[i], outer[(i + 1) % n], run, length, w);
                    GroundBand(c, DecalType.GroundSkirting, b.Id, w, l0, l1, r0, r1);
                }
            }
        }

        /// <summary>
        /// A door's or window's frame on one wall face: a jamb each side and a head, and a sill under a window. Parts
        /// beyond the wall's ends (<paramref name="min"/>, <paramref name="max"/>) are left out.
        /// </summary>
        private static void Frame(Context c, int building, DecalType type, Vector3 normal, Func<float, float, Vector3> at,
            float t, float half, float bottom, float top, bool sill, float min = float.NegativeInfinity,
            float max = float.PositiveInfinity)
        {
            float w = c.Width(type);
            if (w <= 0f) return;
            float left = t - half - w, right = t + half + w;
            // Jambs run up the wall; the head and sill along it.
            if (left >= min) Emit(c, type, building, normal, w, at(left, bottom), at(left, top), at(left + w, bottom), at(left + w, top));
            if (right <= max) Emit(c, type, building, normal, w, at(right - w, bottom), at(right - w, top), at(right, bottom), at(right, top));
            float l = Mathf.Max(left, min), r = Mathf.Min(right, max);
            Emit(c, type, building, normal, w, at(l, top + w), at(r, top + w), at(l, top), at(r, top));
            if (sill) Emit(c, type, building, normal, w, at(l, bottom), at(r, bottom), at(l, bottom - w), at(r, bottom - w));
        }

        /// <summary>A vertical half-width strip on one face at a wall corner, from the corner along <paramref name="along"/>.</summary>
        private static void Corner(Context c, int building, float w, Vector2 corner, Vector2 along, Vector3 normal,
            float bottom, float top)
        {
            // Half the decal's width on each face, v running from 0 at the corner outward, so the two halves meet there.
            float half = w * 0.5f;
            var away = corner + along * half;
            Emit(c, DecalType.WallCorner, building, normal, half, On(corner, bottom), On(corner, top), On(away, bottom), On(away, top));
        }

        /// <summary>A horizontal band on a wall face between two plan points, from <paramref name="left"/> height down
        /// (or up) to <paramref name="right"/>.</summary>
        private static void WallBand(Context c, DecalType type, int building, Vector3 normal, float w, (Vector2 a, Vector2 b) span,
            float left, float right)
        {
            if ((span.b - span.a).magnitude < Shortest) return;
            Emit(c, type, building, normal, w, On(span.a, left), On(span.b, left), On(span.a, right), On(span.b, right));
        }

        /// <summary>A band on a level floor at height <paramref name="y"/>: the part <paramref name="run"/> (distances
        /// along a side of <paramref name="length"/>) of the quad between rails l0–l1 and r0–r1.</summary>
        private static void FlatBand(Context c, DecalType type, int building, float w, float y, Vector2 l0, Vector2 l1,
            Vector2 r0, Vector2 r1, Vector2 run, float length)
        {
            var (a, b, ra, rb) = Cut(l0, l1, r0, r1, run, length, w);
            Emit(c, type, building, Vector3.up, w, On(a, y), On(b, y), On(ra, y), On(rb, y));
        }

        /// <summary>
        /// The part <paramref name="run"/> of a mitred band between rails l0–l1 and r0–r1: its ends keep the mitre at
        /// the side's own ends, and are cut square across the band anywhere else (at a door, say), so the band never
        /// leans into what it stops at.
        /// </summary>
        private static (Vector2 l0, Vector2 l1, Vector2 r0, Vector2 r1) Cut(Vector2 l0, Vector2 l1, Vector2 r0, Vector2 r1,
            Vector2 run, float length, float w)
        {
            var dir = (l1 - l0) / length;
            var across = new Vector2(-dir.y, dir.x);
            if (Vector2.Dot(across, r0 - l0) < 0f) across = -across;
            var a = l0 + dir * run.x;
            var b = l0 + dir * run.y;
            var ra = run.x <= 1e-4f ? r0 : a + across * w;
            var rb = run.y >= length - 1e-4f ? r1 : b + across * w;
            return (a, b, ra, rb);
        }

        private static void Emit(Context c, DecalType type, int building, Vector3 normal, float w, Vector3 l0, Vector3 l1,
            Vector3 r0, Vector3 r1)
        {
            if ((l1 - l0).magnitude < Shortest) return;
            var strip = new DecalStrip { Type = type, Building = building, Normal = normal.normalized, Width = w };
            strip.Left.Add(l0);
            strip.Left.Add(l1);
            strip.Right.Add(r0);
            strip.Right.Add(r1);
            strip.Points.Add((l0 + r0) * 0.5f);
            strip.Points.Add((l1 + r1) * 0.5f);
            c.Strips.Add(strip);
        }

        // ---------------------------------------------------------------- ground bands

        /// <summary>
        /// A band on the ground between rails l0–l1 and r0–r1 (plan points), cut every <see cref="GroundStep"/>, each
        /// point at the ground's height. Where the ground is missing under a point, the band breaks there.
        /// </summary>
        private static void GroundBand(Context c, DecalType type, int building, float w, Vector2 l0, Vector2 l1, Vector2 r0,
            Vector2 r1, float v0 = 0f, float v1 = 1f)
        {
            float length = Mathf.Max((l1 - l0).magnitude, (r1 - r0).magnitude);
            if (length < Shortest) return;
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / GroundStep));
            DecalStrip strip = null;
            for (int i = 0; i <= steps; i++)
            {
                float f = (float)i / steps;
                var l = Vector2.Lerp(l0, l1, f);
                var r = Vector2.Lerp(r0, r1, f);
                var across = r - l;
                var inward = across.sqrMagnitude > 1e-10f ? across.normalized * SampleInset : Vector2.zero;
                float hl = c.Ground.Height(l + inward), hr = c.Ground.Height(r - inward);
                if (float.IsNaN(hl) || float.IsNaN(hr))
                {
                    Close(c, ref strip);
                    continue;
                }
                strip ??= new DecalStrip { Type = type, Building = building, Normal = Vector3.up, Width = w, V0 = v0, V1 = v1 };
                strip.Left.Add(On(l, hl));
                strip.Right.Add(On(r, hr));
                strip.Points.Add((On(l, hl) + On(r, hr)) * 0.5f);
            }
            Close(c, ref strip);
        }

        private static void Close(Context c, ref DecalStrip strip)
        {
            if (strip != null && strip.Points.Count >= 2) c.Strips.Add(strip);
            strip = null;
        }

        /// <summary>A band round a convex outline, between its offsets by <paramref name="from"/> and <paramref name="to"/>
        /// (positive is outward), one strip per side, mitred at the corners.</summary>
        private static void GroundLoop(Context c, DecalType type, int building, float w, List<Vector2> outline, float from, float to)
        {
            var a = Offset(outline, from);
            var b = Offset(outline, to);
            for (int i = 0; i < a.Count; i++)
            {
                int j = (i + 1) % a.Count;
                // Left is the edge nearer the outline, so the ground is read on the strip's side of it.
                GroundBand(c, type, building, w, a[i], a[j], b[i], b[j]);
            }
        }

        // ---------------------------------------------------------------- geometry helpers

        /// <summary>
        /// A convex counter-clockwise polygon with every edge moved <paramref name="d"/> outward (inward when negative),
        /// vertex for vertex, mitred. Repeated vertices are dropped first.
        /// </summary>
        public static List<Vector2> Offset(List<Vector2> polygon, float d)
        {
            var points = new List<Vector2>();
            foreach (var p in polygon)
                if (points.Count == 0 || (p - points[^1]).sqrMagnitude > 1e-10f) points.Add(p);
            if (points.Count > 1 && (points[0] - points[^1]).sqrMagnitude <= 1e-10f) points.RemoveAt(points.Count - 1);
            if (d == 0f) return points;
            int n = points.Count;
            var result = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var prev = (points[i] - points[(i + n - 1) % n]).normalized;
                var next = (points[(i + 1) % n] - points[i]).normalized;
                var n1 = new Vector2(prev.y, -prev.x);
                var n2 = new Vector2(next.y, -next.x);
                float denominator = 1f + Vector2.Dot(n1, n2);
                result.Add(points[i] + (n1 + n2) * (d / Mathf.Max(denominator, 0.1f)));
            }
            return result;
        }

        /// <summary>Intervals of [0, length] outside every blocked interval, each at least <see cref="Shortest"/>.</summary>
        private static List<Vector2> Free(float length, List<Vector2> blocked)
        {
            var sorted = new List<Vector2>(blocked);
            sorted.Sort((p, q) => p.x.CompareTo(q.x));
            var runs = new List<Vector2>();
            float start = 0f;
            foreach (var b in sorted)
            {
                if (b.x - start >= Shortest) runs.Add(new Vector2(start, Mathf.Min(b.x, length)));
                start = Mathf.Max(start, b.y);
            }
            if (length - start >= Shortest) runs.Add(new Vector2(start, length));
            runs.RemoveAll(r => r.y - r.x < Shortest);
            return runs;
        }

        /// <summary>Adds the stretch of segment a–b (as distances from a) within <paramref name="margin"/> of a convex polygon.</summary>
        private static void Blocked(List<Vector2> blocked, Vector2 a, Vector2 b, List<Vector2> polygon, float margin)
        {
            var grown = Offset(polygon, margin);
            float length = (b - a).magnitude;
            if (length < 1e-6f) return;
            var dir = (b - a) / length;
            float t0 = 0f, t1 = length;
            for (int i = 0; i < grown.Count; i++)
            {
                var p = grown[i];
                var e = grown[(i + 1) % grown.Count] - p;
                var outward = new Vector2(e.y, -e.x);
                float num = Vector2.Dot(outward, p - a), den = Vector2.Dot(outward, dir);
                if (Mathf.Abs(den) < 1e-9f)
                {
                    if (num < 0f) return;
                    continue;
                }
                float t = num / den;
                if (den > 0f) t1 = Mathf.Min(t1, t);
                else t0 = Mathf.Max(t0, t);
            }
            if (t1 > t0) blocked.Add(new Vector2(t0, t1));
        }

        private static (Vector2, Vector2) Lerp(Vector2 a, Vector2 b, Vector2 run, float length) =>
            (Vector2.Lerp(a, b, run.x / length), Vector2.Lerp(a, b, run.y / length));

        private static int NearestSide(List<Vector2> polygon, Vector2 p)
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var ab = polygon[(i + 1) % polygon.Count] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>A box-like primitive's plan outline, counter-clockwise.</summary>
        private static List<Vector2> Footprint(in Primitive p)
        {
            float yaw = p.YawDegrees * Mathf.Deg2Rad;
            var along = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * (p.Size.z * 0.5f);
            var across = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)) * (p.Size.x * 0.5f);
            var c = new Vector2(p.Position.x, p.Position.z);
            return Ccw(new List<Vector2> { c - along - across, c + along - across, c + along + across, c - along + across });
        }

        private static List<Vector2> Ccw(List<Vector2> polygon)
        {
            if (Polygon.SignedArea(polygon) < 0f) polygon.Reverse();
            return polygon;
        }

        private static Vector3 On(Vector2 p, float y) => new(p.x, y, p.y);
        private static Vector3 Up(Vector2 horizontal) => new(horizontal.x, 0f, horizontal.y);

        /// <summary>The outdoor ground's height: the highest top of the plot, terrain and slope prisms over a point.</summary>
        private sealed class GroundSampler
        {
            private const float Bucket = 2f;
            private readonly LevelDescription _level;
            private readonly List<int>[] _buckets;
            private readonly int _columns, _rows;

            public GroundSampler(LevelDescription level)
            {
                _level = level;
                _columns = Mathf.Max(1, Mathf.CeilToInt(level.Size.x / Bucket));
                _rows = Mathf.Max(1, Mathf.CeilToInt(level.Size.z / Bucket));
                _buckets = new List<int>[_columns * _rows];
                for (int i = 0; i < level.Primitives.Count; i++)
                {
                    var p = level.Primitives[i];
                    if (p.Shape != PrimitiveShape.Prism || !(p.OwnerKind is OwnerKind.Plot or OwnerKind.Transition)) continue;
                    var polygon = level.Polygons[p.Polygon];
                    Polygon.Extent(polygon, Vector2.right, out float x0, out float x1);
                    Polygon.Extent(polygon, Vector2.up, out float z0, out float z1);
                    for (int x = Column(x0); x <= Column(x1); x++)
                        for (int z = Row(z0); z <= Row(z1); z++)
                            (_buckets[z * _columns + x] ??= new List<int>()).Add(i);
                }
            }

            private int Column(float x) => Mathf.Clamp(Mathf.FloorToInt(x / Bucket), 0, _columns - 1);
            private int Row(float z) => Mathf.Clamp(Mathf.FloorToInt(z / Bucket), 0, _rows - 1);

            /// <summary>NaN where no ground prism covers the point.</summary>
            public float Height(Vector2 p)
            {
                var bucket = _buckets[Row(p.y) * _columns + Column(p.x)];
                if (bucket == null) return float.NaN;
                float best = float.NegativeInfinity;
                foreach (int i in bucket)
                {
                    var prim = _level.Primitives[i];
                    if (!Inside(_level.Polygons[prim.Polygon], p)) continue;
                    best = Mathf.Max(best, prim.PrismTop(p.x, p.y));
                }
                return float.IsNegativeInfinity(best) ? float.NaN : best;
            }

            /// <summary>Inside a convex counter-clockwise polygon, edges included (within 1e-4).</summary>
            private static bool Inside(List<Vector2> polygon, Vector2 p)
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i];
                    var e = polygon[(i + 1) % polygon.Count] - a;
                    float length = e.magnitude;
                    if (length < 1e-9f) continue;
                    if ((e.x * (p.y - a.y) - e.y * (p.x - a.x)) / length < -1e-4f) return false;
                }
                return true;
            }
        }
    }
}
