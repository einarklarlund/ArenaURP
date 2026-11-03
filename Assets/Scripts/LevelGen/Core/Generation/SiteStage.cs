using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// The room graph's links, chest sites and pickup lines, read off a validated level. It draws no random numbers:
    /// everything comes from the description in a fixed order, so it is deterministic and leaves every seed stream as
    /// it was.
    /// <list type="bullet">
    /// <item>Every site and line point passes the probe: a walkable surface (not stairs) in R, not a sink, with ground
    /// within a step all round it for <see cref="ClearCells"/>. When a feature's own point fails, the probe takes the
    /// nearest passing cell within a few cells (sites only; lines stay straight).</item>
    /// <item>Chest sites by feature: sinkhole floors, hilltops and valley bottoms (the fan sites of sinkhole, mound and dip
    /// sub-rooms), convex corners of the boundary, every building room and balcony, behind each fence run (the side away
    /// from the room's middle first), and beside each car. Sites keep <see cref="MinSiteGap"/> from each other and
    /// <see cref="SiteSpacing"/> from sites of their kind in their room. A room left with fewer than
    /// <see cref="MinSitesPerRoom"/> is topped up with open ground, by farthest-point sampling.</item>
    /// <item>Pickup lines: <see cref="LinePoints"/> points along building walls, boundary walls, fence runs, balcony
    /// railings, sinkhole rims, ledge tops and up ramps and banks. A line is kept only if every point passes.</item>
    /// </list>
    /// </summary>
    public static class SiteStage
    {
        public const int ClearCells = 1;
        /// <summary>Cells round a feature's point the probe searches for a site that passes.</summary>
        public const int SearchCells = 4;
        public const float MinSiteGap = 1.5f;
        public const float SiteSpacing = 4f;
        public const int MinSitesPerRoom = 3;
        public const int LinePoints = 5;
        public const float LineSpacing = 1.75f;
        public const float MinLineSpacing = 1f;
        /// <summary>Lines whose middles are closer than this keep only the first.</summary>
        public const float LineGap = 4f;

        /// <summary>Corner sites sit this far from both walls; boundary lines this far in.</summary>
        private const float WallInset = 1.5f;
        /// <summary>How far off a fence, a building wall or a car a site or line stands.</summary>
        private const float Offset = 1f;
        /// <summary>Height tolerance when only the plot's plane says where the ground is (terrain moves it).</summary>
        private const float GroundTolerance = 4f;
        /// <summary>Height tolerance on a known slab, and between one line point and the next.</summary>
        private const float SlabTolerance = 0.6f;
        private const float FollowTolerance = 1.2f;

        public static void Emit(LevelDescription level, ValidationResult check, in LevelGenSettings s, List<PlotEdge> outline)
        {
            level.RoomLinks.AddRange(Links(level));
            var probe = new Probe(level, check, s.Player.StepHeight);
            var sites = new Sites(level);

            // Sinkholes, hilltops and valley bottoms.
            foreach (var sub in level.SubRooms)
            {
                ChestSiteKind kind;
                if (sub.Surface == SurfaceTag.GroundSinkhole) kind = ChestSiteKind.SinkholeMiddle;
                else if (sub.Surface == SurfaceTag.GroundMound) kind = ChestSiteKind.Hilltop;
                else if (sub.Surface == SurfaceTag.GroundDip) kind = ChestSiteKind.ValleyBottom;
                else continue;
                if (probe.Find(sub.Site, GroundAt(level, sub.Plot, sub.Site), GroundTolerance, SearchCells, false, out var at))
                    sites.Add(kind, at);
            }

            // Convex corners of the boundary.
            foreach (var (corner, inward) in ConvexCorners(outline))
            {
                var p = corner + inward;
                int plot = RoomGraph.Locate(level, p);
                if (plot >= 0 && probe.Find(p, GroundAt(level, plot, p), GroundTolerance, SearchCells, false, out var at))
                    sites.Add(ChestSiteKind.BorderCorner, at);
            }

            // Building rooms, upper floors and balconies.
            foreach (var b in level.Buildings)
            {
                foreach (var room in b.Rooms)
                {
                    var centre = Polygon.Centroid(level.Polygons[room.Polygon]);
                    if (probe.Find(centre, room.Height, SlabTolerance, SearchCells, false, out var at))
                        sites.Add(room.Floor == 0 ? ChestSiteKind.BuildingGround : ChestSiteKind.BuildingUpper, at);
                }
                foreach (var balcony in b.Balconies)
                {
                    // A terrace's polygon is the larger floor below, so its middle is indoors: stand out from its door.
                    var p = balcony.Terrace ? balcony.Door + balcony.Outward * WallInset
                        : Polygon.Centroid(level.Polygons[balcony.Polygon]);
                    if (probe.Find(p, b.FloorY(balcony.Floor), SlabTolerance, SearchCells, false, out var at))
                        sites.Add(ChestSiteKind.Balcony, at);
                }
            }

            // Behind fences and beside cars.
            var runs = FenceRuns(level, s.Props);
            foreach (var run in runs)
            {
                var mid = (run.From + run.To) * 0.5f;
                int plot = RoomGraph.Locate(level, mid);
                if (plot < 0) continue;
                var normal = run.Normal;
                var middle = Polygon.Centroid(level.PolygonOf(level.Plots[plot]));
                if (Vector2.Dot(normal, middle - mid) > 0f) normal = -normal;
                foreach (var side in new[] { normal, -normal })
                    if (probe.Find(mid + side * Offset, run.Y, GroundTolerance, 1, false, out var at))
                    {
                        sites.Add(ChestSiteKind.BehindFence, at);
                        break;
                    }
            }
            foreach (var prop in level.Props)
            {
                if (prop.Kind != PropKind.Car) continue;
                var shape = Shape(s.Props, prop);
                var centre = new Vector2(prop.Position.x, prop.Position.z);
                var along = Along(prop.YawDegrees);
                var across = new Vector2(along.y, -along.x);
                int plot = RoomGraph.Locate(level, centre);
                var middle = plot >= 0 ? Polygon.Centroid(level.PolygonOf(level.Plots[plot])) : centre;
                var sides = new List<Vector2>
                {
                    centre + across * (shape.Width * 0.5f + Offset), centre - across * (shape.Width * 0.5f + Offset),
                    centre + along * (shape.Length * 0.5f + Offset), centre - along * (shape.Length * 0.5f + Offset),
                };
                // The side facing the room's middle first: a boundary car's other side is against the wall.
                sides.Sort((x, y) => (x - middle).sqrMagnitude.CompareTo((y - middle).sqrMagnitude));
                foreach (var p in sides)
                    if (probe.Find(p, prop.Position.y, SlabTolerance, 1, false, out var at))
                    {
                        sites.Add(ChestSiteKind.NearCar, at);
                        break;
                    }
            }

            sites.TopUp(probe);
            level.ChestSites.AddRange(sites.All);

            var lines = new Lines(level, probe);
            foreach (var b in level.Buildings)
            {
                var footprint = level.Polygons[b.Footprint];
                for (int i = 0; i < footprint.Count; i++)
                {
                    var from = footprint[i];
                    var to = footprint[(i + 1) % footprint.Count];
                    var dir = (to - from).normalized;
                    var outward = new Vector2(dir.y, -dir.x);
                    var mid = (from + to) * 0.5f;
                    lines.Along(PickupLineKind.BuildingEdge, from + outward * Offset, to + outward * Offset,
                        GroundAt(level, b.Plot, mid), GroundTolerance);
                }
                foreach (var balcony in b.Balconies)
                {
                    if (balcony.Terrace)
                    {
                        // Along the floor's outer walls, out on the terrace (the probe drops the walls with none outside).
                        var floor = level.Polygons[b.FloorFootprints[balcony.Floor]];
                        for (int i = 0; i < floor.Count; i++)
                        {
                            var from = floor[i];
                            var to = floor[(i + 1) % floor.Count];
                            var dir = (to - from).normalized;
                            var outward = new Vector2(dir.y, -dir.x) * Offset;
                            lines.Along(PickupLineKind.Balcony, from + outward, to + outward, b.FloorY(balcony.Floor),
                                SlabTolerance);
                        }
                        continue;
                    }
                    var polygon = level.Polygons[balcony.Polygon];
                    // The railing edge: the one farthest out.
                    int edge = 0;
                    float farthest = float.NegativeInfinity;
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        float d = Vector2.Dot((polygon[i] + polygon[(i + 1) % polygon.Count]) * 0.5f, balcony.Outward);
                        if (d > farthest + 1e-4f)
                        {
                            farthest = d;
                            edge = i;
                        }
                    }
                    // Down the middle of the slab's depth, and in from the side railings.
                    Polygon.Extent(polygon, balcony.Outward, out float near, out float far);
                    var inward = -balcony.Outward * ((far - near) * 0.5f);
                    var a = polygon[edge];
                    var along = (polygon[(edge + 1) % polygon.Count] - a).normalized;
                    var c = polygon[(edge + 1) % polygon.Count];
                    lines.Along(PickupLineKind.Balcony, a + along * 0.5f + inward, c - along * 0.5f + inward,
                        b.FloorY(balcony.Floor), SlabTolerance);
                }
            }
            foreach (var edge in outline)
            {
                var dir = (edge.To - edge.From).normalized;
                var inward = new Vector2(-dir.y, dir.x) * WallInset;
                var mid = (edge.From + edge.To) * 0.5f;
                lines.Along(PickupLineKind.BoundaryWall, edge.From + inward, edge.To + inward,
                    GroundAt(level, edge.A, mid + inward), GroundTolerance);
            }
            foreach (var run in runs)
            {
                var mid = (run.From + run.To) * 0.5f;
                int plot = RoomGraph.Locate(level, mid);
                if (plot < 0) continue;
                var normal = run.Normal;
                // The open side: toward the room's middle.
                if (Vector2.Dot(normal, Polygon.Centroid(level.PolygonOf(level.Plots[plot])) - mid) < 0f) normal = -normal;
                lines.Along(PickupLineKind.Fence, run.From + normal * 0.8f, run.To + normal * 0.8f, run.Y, GroundTolerance);
            }
            foreach (var sub in level.SubRooms)
            {
                if (sub.Surface != SurfaceTag.GroundSinkhole) continue;
                var rim = Polygon.Outset(level.Polygons[sub.Polygon], Offset);
                lines.AroundRim(rim, GroundAt(level, sub.Plot, sub.Site), GroundTolerance);
            }
            foreach (var t in level.Transitions)
            {
                if (t.Type is not (TransitionType.Ledge or TransitionType.Ramp or TransitionType.Bank)) continue;
                var mid = (t.From + t.To) * 0.5f;
                var dir = (t.To - t.From).normalized;
                var normal = new Vector2(-dir.y, dir.x);
                float a = GroundAt(level, t.PlotA, mid), bHeight = GroundAt(level, t.PlotB, mid);
                int higher = a >= bHeight ? t.PlotA : t.PlotB;
                // Point the normal into the higher plot.
                if (!Polygon.Contains(level.PolygonOf(level.Plots[higher]), mid + normal * 0.5f)) normal = -normal;
                if (t.Type == TransitionType.Ledge)
                    lines.Along(PickupLineKind.Ledge, t.From + normal * 0.8f, t.To + normal * 0.8f, Mathf.Max(a, bHeight),
                        GroundTolerance);
                else
                    // A ramp or bank runs down into the lower plot from the edge: walk it from the top.
                    lines.Straight(PickupLineKind.RampPath, mid - normal * 0.5f, -normal, LineSpacing, Mathf.Max(a, bHeight),
                        FollowTolerance);
            }
            level.PickupLines.AddRange(lines.All);
        }

        /// <summary>One link per pair of plots that share a transition, sorted.</summary>
        public static List<RoomLink> Links(LevelDescription level)
        {
            var byPair = new SortedDictionary<long, RoomLink>();
            foreach (var t in level.Transitions)
            {
                if (t.PlotA == t.PlotB || t.PlotA < 0 || t.PlotB < 0) continue;
                int a = Mathf.Min(t.PlotA, t.PlotB), b = Mathf.Max(t.PlotA, t.PlotB);
                long key = ((long)a << 32) | (uint)b;
                if (!byPair.TryGetValue(key, out var link)) link = new RoomLink { RoomA = a, RoomB = b };
                if (TransitionTypes.IsTwoWay(t.Type))
                {
                    link.AToB = true;
                    link.BToA = true;
                }
                else if (t.Type == TransitionType.Cliff)
                {
                    var mid = (t.From + t.To) * 0.5f;
                    if (GroundAt(level, a, mid) >= GroundAt(level, b, mid)) link.AToB = true;
                    else link.BToA = true;
                }
                byPair[key] = link;
            }
            return new List<RoomLink>(byPair.Values);
        }

        /// <summary>The plot's plane at <paramref name="p"/>: its ground before terrain, and exactly at its edges.</summary>
        private static float GroundAt(LevelDescription level, int plot, Vector2 p)
        {
            var d = level.Plots[plot];
            return d.BaseHeight + Vector2.Dot(d.Slope, p - Polygon.Centroid(level.PolygonOf(d)));
        }

        /// <summary>Each corner where the outline turns left (counter-clockwise), and the step from it to the point
        /// <see cref="WallInset"/> from both walls.</summary>
        private static IEnumerable<(Vector2 corner, Vector2 inward)> ConvexCorners(List<PlotEdge> outline)
        {
            foreach (var a in outline)
            foreach (var b in outline)
            {
                if (b.From != a.To) continue;
                var da = (a.To - a.From).normalized;
                var db = (b.To - b.From).normalized;
                if (da.x * db.y - da.y * db.x <= 1e-3f) continue;
                var na = new Vector2(-da.y, da.x);
                var nb = new Vector2(-db.y, db.x);
                var bisector = (na + nb).normalized;
                float cos = Mathf.Max(0.3f, Vector2.Dot(bisector, na));
                yield return (a.To, bisector * (WallInset / cos));
            }
        }

        private static PropShape Shape(in PropSettings props, PropDescription prop)
        {
            var variants = props.Variants(prop.Kind);
            return variants[Mathf.Clamp(prop.Variant, 0, variants.Length - 1)];
        }

        private static Vector2 Along(float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        private struct FenceRun
        {
            public Vector2 From;
            public Vector2 To;
            /// <summary>The ground the run stands on, at its first piece.</summary>
            public float Y;
            public Vector2 Normal => new Vector2(-(To - From).normalized.y, (To - From).normalized.x);
        }

        /// <summary>Fence pieces joined end to end at the same yaw, in the order the prop stage placed them.</summary>
        private static List<FenceRun> FenceRuns(LevelDescription level, in PropSettings props)
        {
            var runs = new List<FenceRun>();
            bool open = false;
            FenceRun run = default;
            float lastYaw = 0f;
            foreach (var prop in level.Props)
            {
                if (prop.Kind != PropKind.Fence)
                {
                    if (open) runs.Add(run);
                    open = false;
                    continue;
                }
                var along = Along(prop.YawDegrees);
                float half = Shape(props, prop).Length * prop.Stretch * 0.5f;
                var centre = new Vector2(prop.Position.x, prop.Position.z);
                var from = centre - along * half;
                var to = centre + along * half;
                if (open && Vector2.Dot(Along(lastYaw), along) > 0.9998f && (from - run.To).sqrMagnitude < 0.01f)
                    run.To = to;
                else
                {
                    if (open) runs.Add(run);
                    run = new FenceRun { From = from, To = to, Y = prop.Position.y };
                    open = true;
                }
                lastYaw = prop.YawDegrees;
            }
            if (open) runs.Add(run);
            return runs;
        }

        /// <summary>Finds where a chest or pickup can stand on the validated level's surfaces.</summary>
        private sealed class Probe
        {
            private readonly SurfaceSet _surfaces;
            private readonly SpanGrid _grid;
            private readonly Reachability _reach;
            private readonly float _step;
            /// <summary>Per surface: 0 not yet checked, 1 passes, 2 fails.</summary>
            private readonly byte[] _verdict;

            public Probe(LevelDescription level, ValidationResult check, float step)
            {
                _surfaces = check.Surfaces;
                _grid = _surfaces.Grid;
                _reach = check.Reachability;
                _step = step;
                _verdict = new byte[_surfaces.Count];
            }

            public SurfaceSet Surfaces => _surfaces;

            /// <summary>The passing surface nearest <paramref name="p"/> within <paramref name="search"/> cells, whose
            /// height is within <paramref name="tolerance"/> of <paramref name="height"/> (the closest in its column).</summary>
            public bool Find(Vector2 p, float height, float tolerance, int search, bool ramps, out Vector3 at)
            {
                at = default;
                int cx = Mathf.FloorToInt(p.x / _grid.CellSize), cz = Mathf.FloorToInt(p.y / _grid.CellSize);
                for (int r = 0; r <= search; r++)
                {
                    int best = -1;
                    float bestDistance = float.PositiveInfinity;
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (!_grid.InBounds(x, z)) continue;
                        int i = SurfaceAt(x, z, height, tolerance);
                        if (i < 0 || !Passes(i, ramps)) continue;
                        float d = (new Vector2(_grid.CellCenterX(x), _grid.CellCenterZ(z)) - p).sqrMagnitude;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = i;
                        }
                    }
                    if (best >= 0)
                    {
                        at = PositionOf(best);
                        return true;
                    }
                }
                return false;
            }

            public Vector3 PositionOf(int i)
            {
                var a = _surfaces[i];
                return new Vector3(_grid.CellCenterX(a.X), a.Height, _grid.CellCenterZ(a.Z));
            }

            private int SurfaceAt(int x, int z, float height, float tolerance)
            {
                _surfaces.ColumnRange(x, z, out int start, out int end);
                int best = -1;
                float bestDelta = tolerance;
                for (int i = start; i < end; i++)
                {
                    float delta = Mathf.Abs(_surfaces[i].Height - height);
                    if (delta <= bestDelta && _surfaces[i].HasWalkableRole)
                    {
                        bestDelta = delta;
                        best = i;
                    }
                }
                return best;
            }

            /// <summary>Walkable (not stairs; ramps only when asked), in R, not a sink, with ground within a step of it
            /// in every cell round it.</summary>
            public bool Passes(int i, bool ramps)
            {
                var a = _surfaces[i];
                if (!a.HasWalkableRole || a.Role == PrimitiveRole.Stairs) return false;
                if (a.Role == PrimitiveRole.Ramp && !ramps) return false;
                if (_verdict[i] == 0) _verdict[i] = (byte)(Clear(i) ? 1 : 2);
                return _verdict[i] == 1;
            }

            private bool Clear(int i)
            {
                var a = _surfaces[i];
                if (!_reach.InR[i] || _reach.Sink[i]) return false;
                for (int dz = -ClearCells; dz <= ClearCells; dz++)
                for (int dx = -ClearCells; dx <= ClearCells; dx++)
                {
                    int x = a.X + dx, z = a.Z + dz;
                    if (!_grid.InBounds(x, z)) return false;
                    _surfaces.ColumnRange(x, z, out int start, out int end);
                    bool found = false;
                    for (int t = start; t < end && !found; t++)
                        found = _surfaces[t].HasWalkableRole && Mathf.Abs(_surfaces[t].Height - a.Height) <= _step;
                    if (!found) return false;
                }
                return true;
            }
        }

        /// <summary>The chest sites so far, kept apart, with each room's centre for their yaw.</summary>
        private sealed class Sites
        {
            public readonly List<ChestSite> All = new();
            private readonly LevelDescription _level;
            private readonly Vector2[] _centres;

            public Sites(LevelDescription level)
            {
                _level = level;
                _centres = new Vector2[level.Plots.Count];
                for (int i = 0; i < _centres.Length; i++) _centres[i] = Polygon.Centroid(level.PolygonOf(level.Plots[i]));
            }

            public bool Add(ChestSiteKind kind, Vector3 at)
            {
                var plan = new Vector2(at.x, at.z);
                int room = RoomGraph.Locate(_level, plan);
                if (room < 0) return false;
                foreach (var other in All)
                {
                    float d = (new Vector2(other.Position.x, other.Position.z) - plan).sqrMagnitude;
                    if (d < MinSiteGap * MinSiteGap && Mathf.Abs(other.Position.y - at.y) < 2f) return false;
                    if (other.Kind == kind && other.Room == room && d < SiteSpacing * SiteSpacing) return false;
                }
                var toMiddle = _centres[room] - plan;
                float yaw = toMiddle.sqrMagnitude > 1e-4f ? Mathf.Atan2(toMiddle.x, toMiddle.y) * Mathf.Rad2Deg : 0f;
                All.Add(new ChestSite { Kind = kind, Room = room, Position = at, YawDegrees = yaw });
                return true;
            }

            /// <summary>Open-ground sites for every room short of <see cref="MinSitesPerRoom"/>, each the passing
            /// surface farthest from the room's sites so far (the one nearest the room's middle when it has none).</summary>
            public void TopUp(Probe probe)
            {
                var count = new int[_centres.Length];
                foreach (var site in All) count[site.Room]++;
                bool any = false;
                foreach (int c in count) any |= c < MinSitesPerRoom;
                if (!any) return;

                var candidates = new List<int>[_centres.Length];
                for (int i = 0; i < candidates.Length; i++) candidates[i] = new List<int>();
                var surfaces = probe.Surfaces;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    if (!probe.Passes(i, false)) continue;
                    var at = probe.PositionOf(i);
                    int room = RoomGraph.Locate(_level, new Vector2(at.x, at.z));
                    if (room >= 0 && count[room] < MinSitesPerRoom) candidates[room].Add(i);
                }
                for (int room = 0; room < _centres.Length; room++)
                {
                    var pool = candidates[room];
                    while (count[room] < MinSitesPerRoom && pool.Count > 0)
                    {
                        int best = -1;
                        float bestScore = float.NegativeInfinity;
                        for (int k = 0; k < pool.Count; k++)
                        {
                            var at = probe.PositionOf(pool[k]);
                            var plan = new Vector2(at.x, at.z);
                            float score = float.PositiveInfinity;
                            foreach (var site in All)
                                if (site.Room == room)
                                    score = Mathf.Min(score, (new Vector2(site.Position.x, site.Position.z) - plan).sqrMagnitude);
                            if (float.IsPositiveInfinity(score)) score = -(plan - _centres[room]).sqrMagnitude;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = k;
                            }
                        }
                        var chosen = probe.PositionOf(pool[best]);
                        pool.RemoveAt(best);
                        if (Add(ChestSiteKind.OpenGround, chosen)) count[room]++;
                    }
                }
            }
        }

        /// <summary>The pickup lines so far, kept <see cref="LineGap"/> apart.</summary>
        private sealed class Lines
        {
            public readonly List<PickupLine> All = new();
            private readonly LevelDescription _level;
            private readonly Probe _probe;

            public Lines(LevelDescription level, Probe probe)
            {
                _level = level;
                _probe = probe;
            }

            /// <summary>A line centred on the segment from <paramref name="from"/> to <paramref name="to"/>, as widely
            /// spaced as fits (up to <see cref="LineSpacing"/>), a little in from its ends.</summary>
            public void Along(PickupLineKind kind, Vector2 from, Vector2 to, float height, float tolerance)
            {
                float length = (to - from).magnitude;
                float spacing = Mathf.Min(LineSpacing, (length - 1f) / (LinePoints - 1));
                if (spacing < MinLineSpacing) return;
                var dir = (to - from) / length;
                var start = (from + to) * 0.5f - dir * (spacing * (LinePoints - 1) * 0.5f);
                Straight(kind, start, dir, spacing, height, tolerance);
            }

            /// <summary>A line from <paramref name="start"/> along <paramref name="dir"/>. The first point's surface is
            /// found near <paramref name="height"/>, each next one near the point before it.</summary>
            public void Straight(PickupLineKind kind, Vector2 start, Vector2 dir, float spacing, float height, float tolerance)
            {
                var points = new List<Vector2>();
                for (int k = 0; k < LinePoints; k++) points.Add(start + dir * (spacing * k));
                Add(kind, points, height, tolerance, kind == PickupLineKind.RampPath);
            }

            /// <summary>A line along the rim's outline, from its first corner on.</summary>
            public void AroundRim(List<Vector2> rim, float height, float tolerance)
            {
                if (rim.Count < 3) return;
                float perimeter = 0f;
                for (int i = 0; i < rim.Count; i++) perimeter += (rim[(i + 1) % rim.Count] - rim[i]).magnitude;
                float spacing = Mathf.Min(LineSpacing, perimeter / LinePoints);
                if (spacing < MinLineSpacing) return;
                var points = new List<Vector2>();
                int side = 0;
                float along = 0f;
                for (int k = 0; k < LinePoints; k++)
                {
                    float target = k * spacing;
                    while (side < rim.Count && along + (rim[(side + 1) % rim.Count] - rim[side]).magnitude < target)
                    {
                        along += (rim[(side + 1) % rim.Count] - rim[side]).magnitude;
                        side++;
                    }
                    if (side >= rim.Count) return;
                    var a = rim[side];
                    var b = rim[(side + 1) % rim.Count];
                    points.Add(a + (b - a).normalized * (target - along));
                }
                Add(PickupLineKind.SinkholeEdge, points, height, tolerance, false);
            }

            private void Add(PickupLineKind kind, List<Vector2> points, float height, float tolerance, bool ramps)
            {
                var placed = new List<Vector3>(points.Count);
                foreach (var p in points)
                {
                    float near = placed.Count == 0 ? height : placed[placed.Count - 1].y;
                    float within = placed.Count == 0 ? tolerance : FollowTolerance;
                    if (!_probe.Find(p, near, within, 0, ramps, out var at)) return;
                    placed.Add(at);
                }
                var middle = placed[placed.Count / 2];
                var plan = new Vector2(middle.x, middle.z);
                int room = RoomGraph.Locate(_level, plan);
                if (room < 0) return;
                foreach (var other in All)
                {
                    var m = other.Points[other.Points.Count / 2];
                    if ((new Vector2(m.x, m.z) - plan).sqrMagnitude < LineGap * LineGap) return;
                }
                All.Add(new PickupLine { Kind = kind, Room = room, Points = placed });
            }
        }
    }
}
