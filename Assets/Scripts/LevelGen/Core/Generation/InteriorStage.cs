using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Stage 7, inside the buildings (part of the props). Per room, at the room's own level: a ceiling light at its
    /// centroid, its top flush with the slab above (stack), then tables, shelves and crates at
    /// <c>interiorPropDensity</c> per 10 units of floor, against walls first (a shelf only against a wall), each standing
    /// on the room's slab (stack) and stopping short of the walls. They keep out of the spine, the stair core and its
    /// approach, every door, wall gap, railing gap and room flight with <see cref="FrontClear"/> of ground in front,
    /// the ledges and railings along open and railing edges, and windows' jump paths; and a prop that would cut a walkable
    /// route between two of the room's ways in is left out. Outside, a wall lamp beside each entrance and one over each
    /// porch side (a stilted building's porch is a first-floor balcony: its lamp is beside its door). Fixtures have no
    /// collider and no primitives; each is listed as a light.
    /// </summary>
    public static class InteriorStage
    {
        /// <summary>Props against a wall stand this far off its face.</summary>
        public const float WallGap = 0.02f;
        /// <summary>Props keep this far from keep-out zones and from each other.</summary>
        public const float Clearance = 0.3f;
        /// <summary>Clear ground in front of a door, a gap or a flight's foot, and a window's jump path.</summary>
        public const float FrontClear = 1.5f;
        /// <summary>A lamp beside a door keeps this far from the door's side.</summary>
        public const float LampGap = 0.25f;
        /// <summary>Light priorities: room lights (plus their room's area) outrank entrance lamps, which outrank porch lamps.</summary>
        public const float RoomPriority = 20000f, EntrancePriority = 10000f, PorchPriority = 0f;

        private const float Grid = 0.25f;
        // A family of random streams beside PropStage's, one per building.
        private const int Streams = -4;

        public static void Build(List<PropPlan> props, List<BuildingPlan> buildings, in LevelGenSettings s, ulong seed)
        {
            if (buildings == null) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (building.Dropped != null) continue;
                var rng = SeedTree.Stream(SeedTree.Derive(SeedTree.Derive(seed, GenStage.Props, Streams), GenStage.Props, i),
                    GenStage.Props);
                for (int k = 0; k < building.Floors; k++)
                {
                    int rooms = Mathf.Max(1, building.FloorPlans[k].Rooms.Count);
                    for (int r = 0; r < rooms; r++) Room(props, building, k, r, s, ref rng);
                }
                Lamps(props, building, s, ref rng);
            }
        }

        // ---------------------------------------------------------------- rooms

        /// <summary>A room's ceiling light and props. A floor without rooms is one room at the floor's level.</summary>
        private static void Room(List<PropPlan> props, BuildingPlan plan, int k, int r, in LevelGenSettings s, ref Pcg32 rng)
        {
            var p = s.Props;
            var floor = plan.FloorPlans[k];
            bool rooms = floor.Rooms.Count > 0;
            float y = plan.FloorY(k) + (rooms ? floor.Rooms[r].Offset : 0f);
            var inner = Polygon.Inset(floor.Footprint, BuildingStage.WallThickness * 0.5f);
            var region = rooms ? Polygon.ClipConvex(BuildingStage.RoomSlab(floor, r), inner) : inner;
            if (region.Count < 3) return;
            float area = Polygon.Area(region);
            var zones = KeepOut(plan, k, s);
            var taken = new List<List<Vector2>>();

            CeilingLight(props, taken, plan, k, region, y, s, ref rng);

            float total = p.TableWeight + p.ShelfWeight + p.CrateWeight;
            float expected = area * p.InteriorPropDensity / 10f;
            int count = Mathf.FloorToInt(expected) + (rng.Chance(expected - Mathf.Floor(expected)) ? 1 : 0);
            if (count == 0) return;
            var walls = WallSides(floor, region);
            var route = new Route(plan, k, r, region, s);
            for (int i = 0; i < count; i++)
                for (int tries = 0; tries < 16; tries++)
                {
                    float kindPick = rng.NextFloat() * (total > 0f ? total : 3f);
                    float variantPick = rng.NextFloat();
                    float sidePick = rng.NextFloat(), alongPick = rng.NextFloat();
                    float u = rng.NextFloat(), v = rng.NextFloat();
                    var kind = total <= 0f
                        ? (PropKind)((int)PropKind.Table + Mathf.Min(2, (int)kindPick))
                        : kindPick < p.TableWeight ? PropKind.Table
                        : kindPick < p.TableWeight + p.ShelfWeight ? PropKind.Shelf : PropKind.Crate;
                    var variants = p.Variants(kind);
                    int variant = PropShape.Pick(variants, variantPick);
                    var shape = variants[variant];
                    bool againstWall = tries < 10 || kind == PropKind.Shelf;
                    if (againstWall && walls.Count == 0) continue;

                    Vector2 centre, along;
                    if (againstWall)
                    {
                        // A wall side by length; the room is on its left, and the prop's front (+X, right of its length)
                        // faces it.
                        float perimeter = 0f;
                        foreach (var (a, b) in walls) perimeter += (b - a).magnitude;
                        float pick = sidePick * perimeter;
                        int side = 0;
                        for (; side < walls.Count - 1; side++)
                        {
                            float length = (walls[side].b - walls[side].a).magnitude;
                            if (pick <= length) break;
                            pick -= length;
                        }
                        var (from, to) = walls[side];
                        float sideLength = (to - from).magnitude;
                        if (sideLength < shape.Length) continue;
                        var dir = (to - from) / sideLength;
                        float t = Mathf.Lerp(shape.Length * 0.5f, sideLength - shape.Length * 0.5f, alongPick);
                        along = -dir;
                        centre = from + dir * t + new Vector2(-dir.y, dir.x) * (WallGap + shape.Width * 0.5f);
                    }
                    else
                    {
                        // Anywhere in the room, square to its longest side.
                        Polygon.Extent(region, Vector2.right, out float x0, out float x1);
                        Polygon.Extent(region, Vector2.up, out float z0, out float z1);
                        centre = new Vector2(Mathf.Lerp(x0, x1, u), Mathf.Lerp(z0, z1, v));
                        along = Vector2.right;
                        float longest = 0f;
                        for (int e = 0; e < region.Count; e++)
                        {
                            var edge = region[(e + 1) % region.Count] - region[e];
                            if (edge.magnitude > longest)
                            {
                                longest = edge.magnitude;
                                along = edge / longest;
                            }
                        }
                    }
                    if (TryAdd(props, taken, zones, region, route, plan, k, y, kind, variant, shape, centre, along, s)) break;
                }
        }

        private static bool TryAdd(List<PropPlan> props, List<List<Vector2>> taken, List<List<Vector2>> zones,
            List<Vector2> region, Route route, BuildingPlan plan, int k, float y, PropKind kind, int variant, PropShape shape,
            Vector2 centre, Vector2 along, in LevelGenSettings s)
        {
            var across = new Vector2(-along.y, along.x);
            var footprint = Box(centre, along, across, shape.Length, shape.Width);
            var padded = Box(centre, along, across, shape.Length + 2f * Clearance, shape.Width + 2f * Clearance);
            foreach (var corner in footprint)
                if (!Polygon.Contains(region, corner)) return false;
            foreach (var zone in zones)
                if (Polygon.Area(Polygon.ClipConvex(padded, zone)) > 1e-3f) return false;
            foreach (var other in taken)
                if (Polygon.Area(Polygon.ClipConvex(padded, other)) > 1e-3f) return false;
            if (y + shape.Height > Ceiling(plan, k, footprint) - 0.01f) return false;
            if (!route.KeepsJoined(footprint)) return false;

            props.Add(new PropPlan
            {
                Kind = kind, Variant = variant, Shape = shape, Leaf = plan.Plot, Centre = centre, Along = along,
                Length = shape.Length, Width = shape.Width, Bottom = y, Height = shape.Height, Footprint = footprint,
            });
            taken.Add(footprint);
            route.Add(footprint);
            return true;
        }

        /// <summary>
        /// The room's ceiling light: at its centroid (or, when that is in the stair core or under a step in the ceiling,
        /// halfway from it to a corner), its top flush with the slab above.
        /// </summary>
        private static void CeilingLight(List<PropPlan> props, List<List<Vector2>> taken, BuildingPlan plan, int k,
            List<Vector2> region, float y, in LevelGenSettings s, ref Pcg32 rng)
        {
            var variants = s.Props.CeilingLights;
            int variant = PropShape.Pick(variants, rng.NextFloat());
            var shape = variants[variant];
            var centroid = Polygon.Centroid(region);
            var core = plan.HasCore ? BuildingStage.CoreRect(plan.Core) : null;
            var along = (region[1] - region[0]).normalized;
            var across = new Vector2(-along.y, along.x);
            var spots = new List<Vector2> { centroid };
            foreach (var corner in region) spots.Add(Vector2.Lerp(centroid, corner, 0.5f));
            foreach (var at in spots)
            {
                var footprint = Box(at, along, across, shape.Length, shape.Width);
                bool inside = true;
                foreach (var corner in footprint) inside &= Polygon.Contains(region, corner);
                if (!inside) continue;
                if (core != null && Polygon.Area(Polygon.ClipConvex(footprint, core)) > 1e-4f) continue;
                if (!FlushCeiling(plan, k, footprint, out float top)) continue;
                float bottom = top - shape.Height;
                // Over a player's eyes.
                if (bottom - y < s.Player.EyeHeight) continue;

                float range = 0f;
                foreach (var corner in region)
                    range = Mathf.Max(range, Mathf.Sqrt((corner - at).sqrMagnitude + (bottom - y) * (bottom - y)));
                props.Add(new PropPlan
                {
                    Kind = PropKind.CeilingLight, Variant = variant, Shape = shape, Leaf = plan.Plot, Centre = at, Along = along,
                    Length = shape.Length, Width = shape.Width, Bottom = bottom, Height = shape.Height, Footprint = footprint,
                    Light = new LightDescription
                    {
                        Position = new Vector3(at.x, bottom - 0.05f, at.y), Range = range + 0.5f, Slot = LightSlot.Room,
                        Priority = RoomPriority + Polygon.Area(region),
                    },
                });
                taken.Add(footprint);
                return;
            }
        }

        /// <summary>
        /// The bottom of whatever is over a floor's room: the next level's slab (the roof's over the top floor), or lower
        /// where a lowered room above, its walls and skirts hang into it.
        /// </summary>
        private static float Ceiling(BuildingPlan plan, int k, List<Vector2> footprint)
        {
            float slab = LevelGenSettings.SlabThickness;
            float bottom = plan.FloorY(k + 1) - slab;
            if (k + 1 >= plan.Floors) return bottom;
            var above = plan.FloorPlans[k + 1];
            foreach (var room in above.Rooms)
                if (room.Offset < 0f && Polygon.Area(Polygon.ClipConvex(footprint, Reach(room))) > 1e-4f)
                    bottom = Mathf.Min(bottom, plan.FloorY(k + 1) + room.Offset - slab);
            return bottom;
        }

        /// <summary>Whether the slab bottom over a footprint is one plane, and its height: the next level's slab, or a
        /// lowered room's slab above that holds the whole footprint.</summary>
        private static bool FlushCeiling(BuildingPlan plan, int k, List<Vector2> footprint, out float top)
        {
            float slab = LevelGenSettings.SlabThickness;
            top = plan.FloorY(k + 1) - slab;
            if (k + 1 >= plan.Floors) return true;
            var above = plan.FloorPlans[k + 1];
            int lowered = -1;
            for (int q = 0; q < above.Rooms.Count; q++)
            {
                if (above.Rooms[q].Offset >= 0f || Polygon.Area(Polygon.ClipConvex(footprint, Reach(above.Rooms[q]))) <= 1e-4f)
                    continue;
                if (lowered >= 0) return false;
                lowered = q;
            }
            if (lowered < 0) return true;
            var slabOutline = BuildingStage.RoomSlab(above, lowered);
            foreach (var corner in footprint)
                if (!Polygon.Contains(slabOutline, corner)) return false;
            top = plan.FloorY(k + 1) + above.Rooms[lowered].Offset - slab;
            return true;
        }

        /// <summary>How far a lowered room's slab, its walls and the skirts under them reach down into the floor below.</summary>
        private static List<Vector2> Reach(RoomPlan room) =>
            Polygon.Outset(room.Polygon, BuildingStage.WallThickness + BuildingStage.InteriorWallThickness);

        /// <summary>The region's sides that stand against a wall: all but those along an open or railing edge.</summary>
        private static List<(Vector2 a, Vector2 b)> WallSides(FloorPlan floor, List<Vector2> region)
        {
            var sides = new List<(Vector2, Vector2)>();
            for (int i = 0; i < region.Count; i++)
            {
                var a = region[i];
                var b = region[(i + 1) % region.Count];
                if ((b - a).magnitude < 0.5f) continue;
                bool wall = true;
                foreach (var e in floor.Walls)
                {
                    if (e.Kind == EdgeKind.Wall) continue;
                    var dir = (e.To - e.From).normalized;
                    if (Mathf.Abs(Cross(dir, a - e.From)) < 1e-2f && Mathf.Abs(Cross(dir, b - e.From)) < 1e-2f) wall = false;
                }
                if (wall) sides.Add((a, b));
            }
            return sides;
        }

        /// <summary>
        /// What a floor's props keep out of: the stair core and its approach, the spine (on a ground-level floor), every
        /// door and window with <see cref="FrontClear"/> in front, every gap in an edge with as much either side, every
        /// room flight with as much at its foot, and a band along each open edge (its ledge and the way across) and
        /// railing edge.
        /// </summary>
        private static List<List<Vector2>> KeepOut(BuildingPlan plan, int k, in LevelGenSettings s)
        {
            var zones = new List<List<Vector2>>();
            var floor = plan.FloorPlans[k];
            float half = BuildingStage.WallThickness * 0.5f;
            if (plan.HasCore) zones.Add(BuildingStage.CoreRect(plan.Core, BuildingStage.CoreApproach));
            if (k == 0 && plan.Placement != FirstFloor.Stilts) zones.AddRange(plan.Spine);
            foreach (var o in floor.Openings)
            {
                Side(floor.Footprint, o.Side, out var a, out var dir, out var outward);
                var face = a + dir * o.T - outward * half;
                zones.Add(Box(face - outward * (FrontClear * 0.5f), -outward, dir, FrontClear, 2f * o.HalfWidth + 0.5f));
            }
            foreach (var e in floor.Walls)
            {
                float length = (e.To - e.From).magnitude;
                if (length < Polygon.Epsilon) continue;
                var dir = (e.To - e.From) / length;
                var normal = new Vector2(-dir.y, dir.x);
                foreach (var gap in e.Gaps)
                {
                    var centre = e.From + dir * ((gap.x + gap.y) * 0.5f);
                    zones.Add(Box(centre, normal, dir, 2f * FrontClear + BuildingStage.InteriorWallThickness, gap.y - gap.x + 0.5f));
                }
                foreach (var f in e.Flights) zones.Add(BuildingStage.FlightRect(f, FrontClear));
                if (e.Kind == EdgeKind.Wall) continue;
                float band = e.Kind == EdgeKind.Open ? FrontClear : 0.5f;
                zones.Add(Box((e.From + e.To) * 0.5f, dir, normal, length, 2f * band));
            }
            return zones;
        }

        /// <summary>
        /// A room's walkable routes on a fine grid: the cells a player (<c>MinWalkWidth</c> across) fits in, inside the
        /// room's walls, out of the stair core and clear of its props. The ways in (doors, gaps, flights, the core's
        /// approach, open edges) are points a step inside; a prop is refused if two that were joined no longer are. The
        /// grid of free cells is kept as props are added, so a candidate only clears the cells near it.
        /// </summary>
        private sealed class Route
        {
            private readonly List<Vector2> _walkable;
            private readonly List<Vector2> _core;
            private readonly Vector2 _origin;
            private readonly int _cellsX, _cellsZ;
            private readonly List<int> _ways = new();
            private readonly int[] _baseline;
            private readonly float _radius;
            /// <summary>The free cells with the props added so far; null when the room has no walkable inset.</summary>
            private readonly bool[] _free;

            public Route(BuildingPlan plan, int k, int r, List<Vector2> region, in LevelGenSettings s)
            {
                _radius = LevelGenSettings.MinWalkWidth * 0.5f;
                _walkable = Polygon.Inset(region, _radius);
                _core = plan.HasCore ? BuildingStage.CoreRect(plan.Core) : null;
                Polygon.Extent(region, Vector2.right, out float x0, out float x1);
                Polygon.Extent(region, Vector2.up, out float z0, out float z1);
                _origin = new Vector2(x0, z0);
                _cellsX = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / Grid));
                _cellsZ = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / Grid));
                if (_walkable.Count < 3) return;

                var floor = plan.FloorPlans[k];
                float half = BuildingStage.WallThickness * 0.5f;
                var points = new List<Vector2>();
                foreach (var o in floor.Openings)
                {
                    if (!o.Door) continue;
                    Side(floor.Footprint, o.Side, out var a, out var dir, out var outward);
                    points.Add(a + dir * o.T - outward * (half + 1f));
                }
                foreach (var e in floor.Walls)
                {
                    if (floor.Rooms.Count > 0 && e.Left != r && e.Right != r) continue;
                    var dir = (e.To - e.From).normalized;
                    var into = new Vector2(-dir.y, dir.x) * (e.Left == r ? 1f : -1f);
                    foreach (var gap in e.Gaps) points.Add(e.From + dir * ((gap.x + gap.y) * 0.5f) + into * 1f);
                    foreach (var f in e.Flights) points.Add(f.Top + f.Down * (f.Run + 1f));
                    if (e.Kind == EdgeKind.Open) points.Add((e.From + e.To) * 0.5f + into * 1f);
                }
                if (plan.HasCore)
                    points.Add(plan.Core.Origin + plan.Core.Inward * (plan.Core.Length + 1f));

                _free = new bool[_cellsX * _cellsZ];
                for (int cell = 0; cell < _free.Length; cell++)
                {
                    var c = Centre(cell);
                    _free[cell] = Polygon.Contains(_walkable, c) && (_core == null || !Polygon.Contains(_core, c));
                }
                foreach (var point in points)
                {
                    int cell = Nearest(_free, point);
                    if (cell >= 0) _ways.Add(cell);
                }
                _baseline = Components(_free);
            }

            public void Add(List<Vector2> footprint)
            {
                if (_free != null) Block(_free, footprint);
            }

            public bool KeepsJoined(List<Vector2> footprint)
            {
                if (_baseline == null || _ways.Count < 2) return true;
                var free = (bool[])_free.Clone();
                Block(free, footprint);
                var now = Components(free);
                for (int i = 0; i < _ways.Count; i++)
                    for (int j = i + 1; j < _ways.Count; j++)
                        if (_baseline[_ways[i]] == _baseline[_ways[j]] && (now[_ways[i]] < 0 || now[_ways[i]] != now[_ways[j]]))
                            return false;
                return true;
            }

            private Vector2 Centre(int cell) => _origin + new Vector2((cell % _cellsX + 0.5f) * Grid, (cell / _cellsX + 0.5f) * Grid);

            /// <summary>Takes the cells within a player's radius of a prop out of <paramref name="free"/>. Only cells in
            /// the prop's bounds grown by the radius (and a margin) can be that close.</summary>
            private void Block(bool[] free, List<Vector2> prop)
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var v in prop)
                {
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                    minZ = Mathf.Min(minZ, v.y); maxZ = Mathf.Max(maxZ, v.y);
                }
                float reach = _radius + 0.5f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - reach - _origin.x) / Grid));
                int x1 = Mathf.Min(_cellsX - 1, Mathf.CeilToInt((maxX + reach - _origin.x) / Grid));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - reach - _origin.y) / Grid));
                int z1 = Mathf.Min(_cellsZ - 1, Mathf.CeilToInt((maxZ + reach - _origin.y) / Grid));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int cell = x + z * _cellsX;
                        if (free[cell] && Distance(prop, Centre(cell)) < _radius) free[cell] = false;
                    }
            }

            /// <summary>The free cell nearest a point, within a metre, or -1.</summary>
            private int Nearest(bool[] free, Vector2 point)
            {
                int best = -1;
                float bestDistance = 1f;
                for (int cell = 0; cell < free.Length; cell++)
                {
                    if (!free[cell]) continue;
                    float d = (Centre(cell) - point).magnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = cell;
                    }
                }
                return best;
            }

            /// <summary>A component label per free cell (4-neighbour), -1 for blocked ones.</summary>
            private int[] Components(bool[] free)
            {
                var label = new int[free.Length];
                for (int i = 0; i < label.Length; i++) label[i] = -1;
                var queue = new Queue<int>();
                int next = 0;
                for (int start = 0; start < free.Length; start++)
                {
                    if (!free[start] || label[start] >= 0) continue;
                    label[start] = next;
                    queue.Enqueue(start);
                    while (queue.Count > 0)
                    {
                        int cell = queue.Dequeue();
                        int x = cell % _cellsX, z = cell / _cellsX;
                        void Visit(int nx, int nz)
                        {
                            if (nx < 0 || nz < 0 || nx >= _cellsX || nz >= _cellsZ) return;
                            int n = nx + nz * _cellsX;
                            if (!free[n] || label[n] >= 0) return;
                            label[n] = next;
                            queue.Enqueue(n);
                        }
                        Visit(x - 1, z);
                        Visit(x + 1, z);
                        Visit(x, z - 1);
                        Visit(x, z + 1);
                    }
                    next++;
                }
                return label;
            }
        }

        // ---------------------------------------------------------------- lamps

        /// <summary>
        /// Wall lamps on the first floor's outer walls: one beside each entrance's door (on whichever side of it the wall
        /// is whole), and one over each porch side, as far from the side's doors as the wall allows. A stilted building
        /// has no walls at its entrances on the ground: its porches are first-floor balconies, each with a lamp beside its
        /// door. A lamp is left out where no stretch of wall takes it.
        /// </summary>
        private static void Lamps(List<PropPlan> props, BuildingPlan plan, in LevelGenSettings s, ref Pcg32 rng)
        {
            // One look for all of a building's lamps.
            var variants = s.Props.WallLights;
            int variant = PropShape.Pick(variants, rng.NextFloat());
            var shape = variants[variant];
            var placed = new List<List<Vector2>>();
            var footprint = plan.Footprint;
            if (plan.Placement == FirstFloor.Stilts)
            {
                foreach (var balcony in plan.Balconies)
                    if (balcony.Floor == 0 && !balcony.Terrace && !balcony.Landing)
                        BesideDoor(props, placed, plan, balcony.Door, balcony.Outward, LightSlot.Porch, variant, shape, s);
                return;
            }
            for (int e = 0; e < plan.Entrances.Count; e++)
                BesideDoor(props, placed, plan, plan.Entrances[e], plan.Outward[e], LightSlot.Entrance, variant, shape, s);
            for (int side = 0; side < footprint.Count; side++)
            {
                Side(footprint, side, out var a, out var dir, out var outward);
                float length = (footprint[(side + 1) % footprint.Count] - a).magnitude;
                bool porch = false;
                foreach (var piece in plan.Porches)
                {
                    var c = Polygon.Centroid(piece);
                    float out0 = Vector2.Dot(c - a, outward), t = Vector2.Dot(c - a, dir);
                    porch |= out0 > 0f && out0 < s.Buildings.BalconyDepth + 0.5f && t > 0f && t < length;
                }
                if (!porch) continue;
                foreach (float f in new[] { 0.25f, 0.75f, 0.5f, 0.15f, 0.85f, 0.35f, 0.65f })
                    if (TryLamp(props, placed, plan, side, f * length, LightSlot.Porch, variant, shape, s, onPorch: true)) break;
            }
        }

        private static void BesideDoor(List<PropPlan> props, List<List<Vector2>> placed, BuildingPlan plan, Vector2 door,
            Vector2 outward, LightSlot slot, int variant, PropShape shape, in LevelGenSettings s)
        {
            var footprint = plan.Footprint;
            for (int side = 0; side < footprint.Count; side++)
            {
                Side(footprint, side, out var a, out var dir, out var o);
                if (Vector2.Dot(o, outward) < 0.9999f || Mathf.Abs(Vector2.Dot(door - a, o)) > 0.05f) continue;
                float t = Vector2.Dot(door - a, dir);
                float half = s.Buildings.DoorWidth * 0.5f;
                foreach (var opening in plan.FloorPlans[0].Openings)
                    if (opening.Door && opening.Side == side && Mathf.Abs(opening.T - t) < opening.HalfWidth + 0.5f)
                        half = opening.HalfWidth;
                float offset = half + LampGap + shape.Length * 0.5f;
                if (TryLamp(props, placed, plan, side, t + offset, slot, variant, shape, s, onPorch: false)) return;
                TryLamp(props, placed, plan, side, t - offset, slot, variant, shape, s, onPorch: false);
                return;
            }
        }

        /// <summary>
        /// A lamp on the first floor's outer wall, side <paramref name="side"/> at <paramref name="t"/> along it, against
        /// the wall's outer face (stop short), its top level with a door's: if the wall there is whole (clear of the
        /// corners and every opening), it is clear of the exterior flight and its landing and of the other lamps, and (for
        /// a porch lamp) the porch is under it.
        /// </summary>
        private static bool TryLamp(List<PropPlan> props, List<List<Vector2>> placed, BuildingPlan plan, int side, float t,
            LightSlot slot, int variant, PropShape shape, in LevelGenSettings s, bool onPorch)
        {
            var floor = plan.FloorPlans[0];
            Side(floor.Footprint, side, out var a, out var dir, out var outward);
            float length = (floor.Footprint[(side + 1) % floor.Footprint.Count] - a).magnitude;
            float half = shape.Length * 0.5f;
            float corner = BuildingStage.WallThickness + BuildingStage.OpeningGap;
            if (t - half < corner || t + half > length - corner) return false;
            foreach (var o in floor.Openings)
                if (o.Side == side && Mathf.Abs(o.T - t) < o.HalfWidth + half + 0.1f) return false;

            float faceOut = BuildingStage.WallThickness * 0.5f;
            var centre = a + dir * t + outward * (faceOut + shape.Width * 0.5f);
            // Its length along the wall, its front (+X) facing out.
            var along = dir;
            var footprint = Box(centre, along, new Vector2(-along.y, along.x), shape.Length, shape.Width);
            var padded = Polygon.Outset(footprint, 0.1f);
            if (plan.Flight != null
                && (Polygon.Area(Polygon.ClipConvex(padded, BuildingStage.FlightRect(plan))) > 1e-4f
                    || Polygon.Area(Polygon.ClipConvex(padded, plan.Flight.Landing)) > 1e-4f))
                return false;
            foreach (var other in placed)
                if (Polygon.Area(Polygon.ClipConvex(Polygon.Outset(footprint, 0.5f), other)) > 1e-4f) return false;
            if (onPorch)
            {
                var below = a + dir * t + outward * (faceOut + 0.5f);
                bool over = false;
                foreach (var piece in plan.Porches) over |= Polygon.Contains(piece, below);
                if (!over) return false;
            }

            float y = plan.FloorY(0);
            float bottom = y + s.Buildings.DoorHeight - shape.Height;
            // Under the wall's top, which is the next level (or its slab's bottom where a larger floor overhangs).
            if (bottom + shape.Height > plan.FloorY(1) - LevelGenSettings.SlabThickness) return false;
            props.Add(new PropPlan
            {
                Kind = PropKind.WallLight, Variant = variant, Shape = shape, Leaf = plan.Plot, Centre = centre, Along = along,
                Length = shape.Length, Width = shape.Width, Bottom = bottom, Height = shape.Height, Footprint = footprint,
                Light = new LightDescription
                {
                    Position = new Vector3(centre.x, bottom + shape.Height * 0.5f, centre.y) + new Vector3(outward.x, 0f, outward.y) * (shape.Width * 0.5f + 0.1f),
                    Range = s.Props.LampRange, Slot = slot,
                    Priority = slot == LightSlot.Entrance ? EntrancePriority : PorchPriority,
                },
            });
            placed.Add(footprint);
            return true;
        }

        // ---------------------------------------------------------------- helpers

        private static void Side(List<Vector2> polygon, int side, out Vector2 a, out Vector2 dir, out Vector2 outward)
        {
            a = polygon[side];
            dir = (polygon[(side + 1) % polygon.Count] - a).normalized;
            outward = new Vector2(dir.y, -dir.x);
        }

        private static List<Vector2> Box(Vector2 centre, Vector2 along, Vector2 across, float length, float width)
        {
            var l = along * (length * 0.5f);
            var w = across * (width * 0.5f);
            var box = new List<Vector2> { centre - l - w, centre + l - w, centre + l + w, centre - l + w };
            if (Polygon.SignedArea(box) < 0f) box.Reverse();
            return box;
        }

        /// <summary>Distance from a point to a convex polygon: 0 inside.</summary>
        private static float Distance(List<Vector2> polygon, Vector2 p)
        {
            if (Polygon.Contains(polygon, p)) return 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var ab = polygon[(i + 1) % polygon.Count] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
