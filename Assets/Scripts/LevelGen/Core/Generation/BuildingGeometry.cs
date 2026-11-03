using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>Stage 4's geometry, and the local check that measures one building on its own plot.</summary>
    public static partial class BuildingStage
    {
        public const float RailingThickness = 0.1f;

        public static void Emit(LevelDescription level, List<BuildingPlan> plans, in LevelGenSettings s)
        {
            foreach (var plan in plans)
                if (plan.Dropped == null) EmitBuilding(level, plan, plan.Plot, s);
        }

        /// <summary>The bottom of a room's slab on floor k: the plot under a first floor on the ground, else a slab's
        /// thickness under its level.</summary>
        private static float SlabBottom(BuildingPlan plan, int k, float offset) =>
            k == 0 && plan.Placement != FirstFloor.Stilts ? plan.Ground : plan.FloorY(k) + Mathf.Min(0f, offset) - LevelGenSettings.SlabThickness;

        /// <summary>
        /// Measures the building alone on its plot (flat ground, nothing around): its entrances must be joined through
        /// it, every floor, balcony and reachable roof must be reachable from the plot, nothing in it may be a sink, and
        /// every walkable surface keeps its headroom. Returns why it failed, or null. Overlaps are left to the whole
        /// level's check.
        /// </summary>
        public static string LocalCheck(BuildingPlan plan, List<Vector2> plot, in LevelGenSettings s)
        {
            var level = new LevelDescription { Size = new Vector3(s.Width, s.MaxHeight, s.Depth) };
            int polygon = level.AddPolygon(plot);
            level.Plots.Add(new PlotDescription { Id = plan.Plot, Polygon = polygon, BaseHeight = plan.Ground });
            level.AddPrism(PrimitiveRole.Floor, polygon, TransitionStage.GroundBottom, plan.Ground, Vector2.zero, OwnerKind.Plot,
                plan.Plot, TransitionStage.GroundSurfaces);
            EmitBuilding(level, plan, plan.Plot, s);
            StageProfiler.Begin("Local check");
            var check = LevelEvaluator.Validate(level, s, overlaps: false);
            StageProfiler.End();
            var broken = new List<string>();
            foreach (var c in check.Constraints)
                if (!c.Passed && c.Constraint is Constraint.NoOneGetsStuck or Constraint.EveryBuildingIsWalkThrough
                        or Constraint.HeadroomAndSizes)
                    broken.Add($"{c.Constraint}: {c.Detail}");
            return broken.Count == 0 ? null : string.Join("; ", broken);
        }

        private static void EmitBuilding(LevelDescription level, BuildingPlan plan, int plotId, in LevelGenSettings s)
        {
            int id = level.Buildings.Count;
            int firstPrimitive = level.Primitives.Count;
            var d = new BuildingDescription
            {
                Id = id, Plot = plotId, Class = plan.Class, Footprint = level.AddPolygon(plan.Footprint), Placement = plan.Placement,
                FirstFloorHeight = plan.FirstFloorY, Floors = plan.Floors, FloorHeight = plan.FloorHeight,
                HasCore = plan.HasCore, Core = plan.Core, RoofReachable = plan.RoofReachable,
                HasExteriorFlight = plan.Flight != null,
            };
            d.Entrances.AddRange(plan.Entrances);
            foreach (var spine in plan.Spine) d.Spine.Add(level.AddPolygon(spine));
            for (int k = 0; k < plan.Floors; k++)
                d.FloorFootprints.Add(k == 0 ? d.Footprint : level.AddPolygon(plan.FloorPlans[k].Footprint));
            foreach (var balcony in plan.Balconies)
                d.Balconies.Add(new BalconyDescription
                {
                    Floor = balcony.Floor, Polygon = level.AddPolygon(balcony.Polygon), Terrace = balcony.Terrace,
                    Door = balcony.Door, Outward = balcony.Outward,
                });
            for (int k = 0; k < plan.Floors; k++)
            {
                foreach (var o in plan.FloorPlans[k].Openings)
                    d.Openings.Add(new OpeningDescription
                    {
                        Floor = k, Side = o.Side, T = o.T, HalfWidth = o.HalfWidth, Bottom = o.Bottom, Top = o.Top, Door = o.Door,
                    });
                var floor = plan.FloorPlans[k];
                int first = d.Rooms.Count;
                foreach (var room in floor.Rooms)
                    d.Rooms.Add(new RoomDescription { Floor = k, Polygon = level.AddPolygon(room.Polygon), Height = plan.FloorY(k) + room.Offset });
                foreach (var wall in floor.Walls)
                {
                    var edge = new RoomEdgeDescription
                    {
                        Floor = k, Left = first + wall.Left, Right = first + wall.Right, Kind = wall.Kind, From = wall.From, To = wall.To,
                    };
                    edge.Gaps.AddRange(wall.Gaps);
                    d.Edges.Add(edge);
                    if (wall.Kind != EdgeKind.Wall) continue;
                    float high = Mathf.Max(RoomOffset(floor, wall.Left), RoomOffset(floor, wall.Right));
                    var w = new InteriorWallDescription { Floor = k, From = wall.From, To = wall.To, GapTop = high + s.Buildings.DoorHeight };
                    w.Gaps.AddRange(wall.Gaps);
                    d.Walls.Add(w);
                }
            }
            level.Buildings.Add(d);

            EmitSlabs(level, plan, d, id);
            for (int k = 0; k < plan.Floors; k++)
            {
                EmitWalls(level, plan, k, id, s);
                EmitInteriorWalls(level, plan, k, id, s);
            }
            EmitRailings(level, plan, id, s);
            EmitRoomFlights(level, plan, id);
            foreach (var porch in plan.Porches)
                level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(porch), plan.Ground, plan.FirstFloorY, Vector2.zero,
                    OwnerKind.Building, id, Surfaces.Of(SurfaceTag.Balcony, SurfaceTag.Plinth, SurfaceTag.Plinth));
            if (plan.Placement == FirstFloor.Raised) EmitSteps(level, plan, id, s);
            if (plan.Placement == FirstFloor.Stilts)
                foreach (var pillar in Pillars(plan.Footprint))
                    level.AddPrism(PrimitiveRole.Pillar, level.AddPolygon(pillar), plan.Ground,
                        plan.FirstFloorY - LevelGenSettings.SlabThickness, Vector2.zero, OwnerKind.Building, id,
                        Surfaces.All(SurfaceTag.Stilts));
            if (plan.HasCore) EmitCore(level, plan, id, s);
            if (plan.Flight != null) EmitExteriorFlight(level, plan, id);
            PlatformsStopShort(level, firstPrimitive);
        }

        /// <summary>
        /// Stop short: a landing is cut back to the faces of the building's walls and railings beside it at its height.
        /// A floor scaled from the one below, or a core against a wall at a corner, can bring a wall's band a hair over
        /// a landing placed against another floor's face.
        /// </summary>
        private static void PlatformsStopShort(LevelDescription level, int first)
        {
            var walls = new List<(List<Vector2> polygon, float bottom, float top)>();
            for (int i = first; i < level.Primitives.Count; i++)
            {
                var p = level.Primitives[i];
                if (p.Shape == PrimitiveShape.Prism && p.Role is PrimitiveRole.Wall or PrimitiveRole.Railing)
                    walls.Add((level.Polygons[p.Polygon], p.Position.y, p.Position.y + p.Size.y));
            }
            for (int i = level.Primitives.Count - 1; i >= first; i--)
            {
                var p = level.Primitives[i];
                if (p.Shape != PrimitiveShape.Prism || p.Role != PrimitiveRole.Platform) continue;
                float bottom = p.Position.y, top = p.Position.y + p.Size.y;
                var pieces = new List<List<Vector2>> { level.Polygons[p.Polygon] };
                bool cut = false;
                foreach (var (wall, wallBottom, wallTop) in walls)
                {
                    if (wallTop <= bottom || wallBottom >= top) continue;
                    var next = new List<List<Vector2>>();
                    foreach (var piece in pieces)
                        if (Polygon.ClipConvex(piece, wall, true).Count == 0) next.Add(piece);
                        else
                        {
                            next.AddRange(Polygon.Subtract(piece, wall));
                            cut = true;
                        }
                    pieces = next;
                }
                if (!cut) continue;
                level.Primitives.RemoveAt(i);
                var replacements = new List<Primitive>();
                foreach (var piece in pieces)
                {
                    if (Polygon.MinWidth(piece) <= Polygon.MinSolidWidth) continue;
                    var q = p;
                    q.Polygon = level.AddPolygon(piece);
                    var anchor = Polygon.Centroid(piece);
                    q.Position = new Vector3(anchor.x, bottom, anchor.y);
                    replacements.Add(q);
                }
                level.Primitives.InsertRange(i, replacements);
            }
        }

        /// <summary>
        /// A slab at every level: the first floor (a plinth standing on the plot unless on stilts), the level between
        /// two floors (the larger of the two, so a smaller floor stands on a terrace and a larger one overhangs), and
        /// the roof. Each covers its floor inset to the walls' inner faces, so slab and walls meet face to face; levels
        /// the core reaches have its well cut out, as convex pieces around it. Under a floor with rooms, each room's part
        /// is at the room's own level (a higher room's side is the riser face over a lower neighbour; a lowered room's
        /// slab drops with it), stopping short of the walls between rooms; the rest (a terrace) is at the floor's level.
        /// </summary>
        private static void EmitSlabs(LevelDescription level, BuildingPlan plan, BuildingDescription d, int id)
        {
            float slab = LevelGenSettings.SlabThickness;
            bool stilts = plan.Placement == FirstFloor.Stilts;
            for (int level0 = 0; level0 <= plan.Floors; level0++)
            {
                float y = plan.FloorY(level0);
                var outline = Polygon.Inset(SlabOutline(plan, level0), WallThickness * 0.5f);
                if (outline.Count < 3) continue;
                bool roof = level0 == plan.Floors;
                bool well = plan.HasCore && (level0 > 0 || stilts) && (!roof || plan.RoofByCore);
                float bottom = level0 == 0 && !stilts ? plan.Ground : y - slab;
                if (y - bottom < 1e-3f) continue;
                var role = roof && !plan.RoofReachable ? PrimitiveRole.Ceiling : PrimitiveRole.Floor;
                // The first floor's slab is the plinth when raised; under stilts its bottom is a ceiling over the spine.
                var surfaces = Surfaces.Of(roof ? SurfaceTag.Roof : SurfaceTag.BuildingFloor, SurfaceTag.SlabEdge,
                    SurfaceTag.BuildingCeiling);
                if (level0 == 0 && plan.Placement == FirstFloor.Raised) surfaces.Front = surfaces.Bottom = SurfaceTag.Plinth;
                var pieces = well ? AroundRect(outline, CoreRect(plan.Core), plan.Core) : new List<List<Vector2>> { outline };
                var floor = level0 < plan.Floors ? plan.FloorPlans[level0] : null;
                if (floor == null || floor.Rooms.Count == 0)
                {
                    foreach (var piece in pieces)
                        level.AddPrism(role, level.AddPolygon(piece), bottom, y, Vector2.zero, OwnerKind.Building, id, surfaces);
                    continue;
                }
                for (int r = 0; r < floor.Rooms.Count; r++)
                {
                    var roomSlab = RoomSlab(floor, r);
                    float offset = floor.Rooms[r].Offset;
                    float roomBottom = SlabBottom(plan, level0, offset);
                    foreach (var piece in pieces)
                    {
                        var part = Polygon.ClipConvex(piece, roomSlab);
                        if (part.Count >= 3 && Polygon.Area(part) > 1e-4f)
                            level.AddPrism(role, level.AddPolygon(part), roomBottom, y + offset, Vector2.zero, OwnerKind.Building, id, surfaces);
                    }
                    if (offset < 0f && floor.Scale != 1f) EmitSkirt(level, plan, level0, r, roomBottom, y - slab, id, role, surfaces);
                }
                var inner = Polygon.Inset(floor.Footprint, WallThickness * 0.5f);
                foreach (var piece in pieces)
                    foreach (var rest in Polygon.Subtract(piece, inner))
                        if (rest.Count >= 3 && Polygon.Area(rest) > 1e-4f)
                            level.AddPrism(role, level.AddPolygon(rest), bottom, y, Vector2.zero, OwnerKind.Building, id, surfaces);
            }
        }

        /// <summary>
        /// Under a lowered room on a resized floor, the outer walls stand on the terrace slab, not on walls below: a
        /// skirt under each wall band along the room, from the lowered slab's bottom up to the terrace slab's, closes the
        /// drop into the floor below. It is bounded by the room's edges' centre lines, so neighbouring skirts meet face to
        /// face.
        /// </summary>
        private static void EmitSkirt(LevelDescription level, BuildingPlan plan, int k, int room, float bottom, float top, int id,
            PrimitiveRole role, Surfaces surfaces)
        {
            var floor = plan.FloorPlans[k];
            float half = WallThickness * 0.5f;
            foreach (var band in Bands(floor.Footprint, true, half, half))
            {
                var piece = band;
                foreach (var e in floor.Walls)
                {
                    if (e.Left != room && e.Right != room) continue;
                    var dir = (e.To - e.From).normalized;
                    var right = new Vector2(dir.y, -dir.x);
                    float c = Vector2.Dot(right, e.From);
                    piece = e.Left == room ? Polygon.ClipHalfPlane(piece, right, c) : Polygon.ClipHalfPlane(piece, -right, -c);
                    if (piece.Count == 0) break;
                }
                // Only the bands the room itself meets.
                if (piece.Count < 3 || Polygon.Area(piece) < 1e-4f
                    || Polygon.ClipConvex(piece, Polygon.Outset(floor.Rooms[room].Polygon, 0.01f)).Count < 3)
                    continue;
                level.AddPrism(role, level.AddPolygon(piece), bottom, top, Vector2.zero, OwnerKind.Building, id, surfaces);
            }
        }

        /// <summary>The footprint a level's slab covers (before the inset to the walls): the larger of the floors
        /// meeting there.</summary>
        private static List<Vector2> SlabOutline(BuildingPlan plan, int level0)
        {
            if (level0 == 0) return plan.Footprint;
            if (level0 == plan.Floors) return plan.FloorPlans[level0 - 1].Footprint;
            var below = plan.FloorPlans[level0 - 1].Footprint;
            var above = plan.FloorPlans[level0].Footprint;
            return Polygon.Area(above) > Polygon.Area(below) ? above : below;
        }

        /// <summary>
        /// Outer walls of floor k: per side, one mitred band (<see cref="Bands"/>) per stretch between openings, from the
        /// floor (the ground for a first floor that isn't on stilts) up to the next level's slab top, or to its bottom
        /// where that slab covers the wall (an overhanging floor above). Doors and windows leave a wall below their
        /// bottom (if any) and above their top. The bands meet at the corners on their bisector, never overlapping.
        /// </summary>
        private static void EmitWalls(LevelDescription level, BuildingPlan plan, int k, int id, in LevelGenSettings s)
        {
            var floor = plan.FloorPlans[k];
            float y = plan.FloorY(k);
            float bottom = k > 0 ? y : plan.Placement == FirstFloor.Stilts ? y - LevelGenSettings.SlabThickness : plan.Ground;
            float top = plan.FloorY(k + 1);
            var footprint = floor.Footprint;
            var above = k + 1 < plan.Floors ? Polygon.Inset(SlabOutline(plan, k + 1), WallThickness * 0.5f) : null;
            var bands = Bands(footprint, true, WallThickness * 0.5f, WallThickness * 0.5f);
            for (int side = 0; side < footprint.Count; side++)
            {
                Side(footprint, side, out var a, out var dir, out float length, out var outward);
                var band = bands[side];
                float sideTop = above != null && Polygon.ClipConvex(band, above).Count > 0 ? top - LevelGenSettings.SlabThickness : top;
                var openings = floor.Openings.FindAll(o => o.Side == side);
                openings.Sort((p, q) => p.T.CompareTo(q.T));
                // Openings are in order along the side; one that overlaps the one before (two entrances' doors close
                // together) gets its sill and lintel only past where that one's end.
                float start = float.NegativeInfinity;
                foreach (var o in openings)
                {
                    float from = Mathf.Max(start, o.T - o.HalfWidth), to = o.T + o.HalfWidth;
                    WallPiece(level, id, band, a, dir, -outward, start, o.T - o.HalfWidth, bottom, sideTop, WallThickness * 0.5f,
                        PrimitiveRole.Wall, OuterWall);
                    if (y + o.Bottom - bottom >= 0.3f)
                        WallPiece(level, id, band, a, dir, -outward, from, to, bottom, y + o.Bottom,
                            WallThickness * 0.5f, PrimitiveRole.Wall, OuterWall);
                    WallPiece(level, id, band, a, dir, -outward, from, to, y + o.Top, sideTop,
                        WallThickness * 0.5f, PrimitiveRole.Wall, OuterWall);
                    start = Mathf.Max(start, to);
                }
                WallPiece(level, id, band, a, dir, -outward, start, float.PositiveInfinity, bottom, sideTop, WallThickness * 0.5f,
                    PrimitiveRole.Wall, OuterWall);
            }
        }

        /// <summary>
        /// Interior walls (the edges drawn as walls) of floor k, ending at the outer walls' inner faces and running up
        /// to the ceiling slab's bottom. With rooms they stand between the two rooms' slabs, from the lower slab bottom:
        /// each gap keeps a threshold up to the higher room's level and a lintel from a door's height over it. Without
        /// rooms (one slab under the floor) they stand on the slab.
        /// </summary>
        private static void EmitInteriorWalls(LevelDescription level, BuildingPlan plan, int k, int id, in LevelGenSettings s)
        {
            var floor = plan.FloorPlans[k];
            float y = plan.FloorY(k);
            float top = plan.FloorY(k + 1) - LevelGenSettings.SlabThickness;
            var inside = Polygon.Inset(floor.Footprint, WallThickness * 0.5f);
            float half = InteriorWallThickness * 0.5f;
            bool rooms = floor.Rooms.Count > 0;
            foreach (var wall in floor.Walls)
            {
                if (wall.Kind != EdgeKind.Wall) continue;
                var band = Polygon.ClipConvex(Bands(new List<Vector2> { wall.From, wall.To }, false, half, half)[0], inside);
                if (band.Count < 3) continue;
                float left0 = RoomOffset(floor, wall.Left), right0 = RoomOffset(floor, wall.Right);
                float bottom = rooms ? Mathf.Min(SlabBottom(plan, k, left0), SlabBottom(plan, k, right0)) : y;
                float high = y + Mathf.Max(left0, right0);
                var dir = (wall.To - wall.From).normalized;
                var left = new Vector2(-dir.y, dir.x);
                // As with outer openings, a gap that overlaps the one before gets its threshold and lintel only past it.
                var gaps = new List<Vector2>(wall.Gaps);
                gaps.Sort((p, q) => p.x.CompareTo(q.x));
                float start = float.NegativeInfinity;
                foreach (var gap in gaps)
                {
                    float from = Mathf.Max(start, gap.x);
                    WallPiece(level, id, band, wall.From, dir, left, start, gap.x, bottom, top, half, PrimitiveRole.Wall, InteriorWall);
                    WallPiece(level, id, band, wall.From, dir, left, from, gap.y, bottom, high, half, PrimitiveRole.Wall, InteriorWall);
                    WallPiece(level, id, band, wall.From, dir, left, from, gap.y, high + s.Buildings.DoorHeight, top, half,
                        PrimitiveRole.Wall, InteriorWall);
                    start = Mathf.Max(start, gap.y);
                }
                WallPiece(level, id, band, wall.From, dir, left, start, float.PositiveInfinity, bottom, top, half, PrimitiveRole.Wall,
                    InteriorWall);
            }
        }

        /// <summary>Each room flight: from the lower room's slab up to the higher room's level, its top at the edge.</summary>
        private static void EmitRoomFlights(LevelDescription level, BuildingPlan plan, int id)
        {
            for (int k = 0; k < plan.Floors; k++)
                foreach (var wall in plan.FloorPlans[k].Walls)
                    foreach (var f in wall.Flights)
                        Flight(level, id, f.Top + f.Down * (f.Run * 0.5f), -f.Down, plan.FloorY(k) + f.Low, f.High - f.Low, f.Run,
                            f.Width, f.Risers);
        }

        /// <summary>
        /// Railings <c>railingHeight</c> tall: a parapet on the wall below wherever a floor is smaller than the one under
        /// it (the terrace's edge, less where the upper floor's walls stand) and round a reachable roof, and a thin
        /// railing on each slab balcony's open edges, inside the slab's edge. All mitred at their corners.
        /// </summary>
        private static void EmitRailings(LevelDescription level, BuildingPlan plan, int id, in LevelGenSettings s)
        {
            float height = s.Buildings.RailingHeight;
            float half = WallThickness * 0.5f;
            void Parapet(List<Vector2> outline, float y, List<Vector2> upper, List<Vector2> gap = null)
            {
                var upperBands = upper != null ? Bands(upper, true, half, half) : new List<List<Vector2>>();
                if (gap != null) upperBands.Add(gap);
                foreach (var band in Bands(outline, true, half, half))
                {
                    var pieces = new List<List<Vector2>> { band };
                    foreach (var wall in upperBands)
                    {
                        var next = new List<List<Vector2>>();
                        foreach (var piece in pieces) next.AddRange(Polygon.Subtract(piece, wall));
                        pieces = next;
                    }
                    foreach (var piece in pieces)
                        AddWall(level, id, piece, y, y + height, PrimitiveRole.Railing, Railing, 0u);
                }
            }
            for (int k = 1; k < plan.Floors; k++)
                if (plan.FloorPlans[k].Scale < 1f) Parapet(plan.FloorPlans[k - 1].Footprint, plan.FloorY(k), plan.FloorPlans[k].Footprint);
            if (plan.RoofReachable)
            {
                // A roof reached by the exterior flight leaves the parapet open where the flight's landing meets it.
                var top = plan.FloorPlans[plan.Floors - 1].Footprint;
                List<Vector2> gap = null;
                if (plan.RoofByFlight)
                {
                    Side(top, plan.Flight.Side, out var a, out var dir, out _, out var outward);
                    var at = a + dir * Vector2.Dot(Polygon.Centroid(plan.Flight.Landing) - a, dir);
                    gap = Rect(at - outward * WallThickness, outward, dir, 2f * WallThickness, plan.Flight.Width);
                }
                Parapet(top, plan.RoofY, null, gap);
            }

            // Railing edges between rooms: on the higher room's slab (the left one when level), inside the edge, with
            // their gaps open.
            for (int k = 0; k < plan.Floors; k++)
            {
                var floor = plan.FloorPlans[k];
                var inside = Polygon.Inset(floor.Footprint, half);
                foreach (var wall in floor.Walls)
                {
                    if (wall.Kind != EdgeKind.Railing) continue;
                    float left0 = RoomOffset(floor, wall.Left), right0 = RoomOffset(floor, wall.Right);
                    bool onLeft = left0 >= right0;
                    float y = plan.FloorY(k) + Mathf.Max(left0, right0);
                    var band = Polygon.ClipConvex(Bands(new List<Vector2> { wall.From, wall.To }, false,
                        onLeft ? RailingThickness : 0f, onLeft ? 0f : RailingThickness)[0], inside);
                    if (band.Count < 3) continue;
                    var dir = (wall.To - wall.From).normalized;
                    float start = float.NegativeInfinity;
                    foreach (var gap in wall.Gaps)
                    {
                        WallPiece(level, id, band, wall.From, dir, Vector2.zero, start, gap.x, y, y + height, 1f, PrimitiveRole.Railing, Railing);
                        start = gap.y;
                    }
                    WallPiece(level, id, band, wall.From, dir, Vector2.zero, start, float.PositiveInfinity, y, y + height, 1f,
                        PrimitiveRole.Railing, Railing);
                }
            }

            foreach (var balcony in plan.Balconies)
            {
                if (balcony.Terrace || balcony.Landing) continue;
                float y = plan.FloorY(balcony.Floor);
                level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(balcony.Polygon), y - LevelGenSettings.SlabThickness, y,
                    Vector2.zero, OwnerKind.Building, id, Surfaces.All(SurfaceTag.Balcony));
                if (balcony.Railings.Count == 0) continue;
                // The open edges as one chain, the railing on the slab's side of it.
                var chain = new List<Vector2> { balcony.Railings[0].a };
                foreach (var (_, b) in balcony.Railings) chain.Add(b);
                var centre = Polygon.Centroid(balcony.Polygon);
                var d0 = (chain[1] - chain[0]).normalized;
                bool slabOnLeft = Vector2.Dot(new Vector2(-d0.y, d0.x), centre - chain[0]) > 0f;
                foreach (var band in Bands(chain, false, slabOnLeft ? RailingThickness : 0f, slabOnLeft ? 0f : RailingThickness))
                    AddWall(level, id, band, y, y + height, PrimitiveRole.Railing, Railing, 0u);
            }
        }

        /// <summary>
        /// The bands a wall of the given thickness along a polyline covers: one convex quad per segment, from
        /// <paramref name="right"/> to the segment's right to <paramref name="left"/> to its left, counter-clockwise
        /// (right edge first, along the segment). Neighbouring segments meet on their corner's bisector (a mitre), so
        /// the bands tile the wall without overlapping; an open chain's ends are square.
        /// </summary>
        public static List<List<Vector2>> Bands(List<Vector2> points, bool closed, float left, float right)
        {
            int n = points.Count;
            int segments = closed ? n : n - 1;
            var normals = new Vector2[segments];
            for (int i = 0; i < segments; i++)
            {
                var d = (points[(i + 1) % n] - points[i]).normalized;
                normals[i] = new Vector2(-d.y, d.x);
            }
            // The offset of a joint: square at an open end, else the mitre of its two segments' offsets.
            Vector2 Offset(int joint, int segment)
            {
                bool start = joint == segment;
                if (!closed && (start ? joint == 0 : joint == n - 1)) return normals[segment];
                var other = normals[start ? (segment + segments - 1) % segments : (segment + 1) % segments];
                var m = normals[segment] + other;
                return m / (1f + Vector2.Dot(normals[segment], other));
            }
            var bands = new List<List<Vector2>>(segments);
            for (int i = 0; i < segments; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                var oa = Offset(i, i);
                var ob = Offset((i + 1) % n, i);
                bands.Add(new List<Vector2> { a - oa * right, b - ob * right, b + ob * left, a + oa * left });
            }
            return bands;
        }

        /// <summary>
        /// The part of a wall's band between distances <paramref name="from"/> and <paramref name="to"/> along it from
        /// <paramref name="a"/> (infinite: the band's own end), as a prism from <paramref name="bottom"/> to
        /// <paramref name="top"/>. Its sides on the line <paramref name="halfThickness"/> towards
        /// <paramref name="back"/> take the back tag.
        /// </summary>
        private static void WallPiece(LevelDescription level, int id, List<Vector2> band, Vector2 a, Vector2 dir, Vector2 back,
            float from, float to, float bottom, float top, float halfThickness, PrimitiveRole role, Surfaces surfaces)
        {
            if (to - from < 0.05f || top - bottom < 0.05f) return;
            var piece = band;
            if (!float.IsNegativeInfinity(from)) piece = Polygon.ClipHalfPlane(piece, -dir, -(Vector2.Dot(dir, a) + from));
            if (!float.IsPositiveInfinity(to)) piece = Polygon.ClipHalfPlane(piece, dir, Vector2.Dot(dir, a) + to);
            if (piece.Count < 3) return;
            uint backSides = 0u;
            float line = Vector2.Dot(back, a) + halfThickness;
            for (int k = 0; k < piece.Count; k++)
                if (Mathf.Abs(Vector2.Dot(back, piece[k]) - line) < 1e-3f && Mathf.Abs(Vector2.Dot(back, piece[(k + 1) % piece.Count]) - line) < 1e-3f)
                    backSides |= 1u << k;
            AddWall(level, id, piece, bottom, top, role, surfaces, backSides);
        }

        private static void AddWall(LevelDescription level, int id, List<Vector2> polygon, float bottom, float top,
            PrimitiveRole role, Surfaces surfaces, uint backSides)
        {
            if (top - bottom < 0.05f || polygon.Count < 3 || Polygon.Area(polygon) < 1e-4f) return;
            if (!level.AddPrism(role, level.AddPolygon(polygon), bottom, top, Vector2.zero, OwnerKind.Building, id, surfaces))
                return;
            var p = level.Primitives[^1];
            p.BackSides = backSides;
            level.Primitives[^1] = p;
        }

        /// <summary>An outer wall's tags: a box's front (local +X) is to the right of a counter-clockwise side, outside.</summary>
        private static readonly Surfaces OuterWall = new()
        {
            Top = SurfaceTag.OuterWallOutside, Bottom = SurfaceTag.OuterWallOutside, Front = SurfaceTag.OuterWallOutside,
            Back = SurfaceTag.OuterWallInside, Ends = SurfaceTag.OuterWallOutside,
        };
        private static readonly Surfaces InteriorWall = Surfaces.All(SurfaceTag.InteriorWall);
        private static readonly Surfaces Railing = Surfaces.All(SurfaceTag.Railing);
        private static readonly Surfaces Stairs = Surfaces.All(SurfaceTag.Stairs);

        /// <summary>Spine-wide steps outside each door, from the plot up to the raised floor, one riser per cell.</summary>
        private static void EmitSteps(LevelDescription level, BuildingPlan plan, int id, in LevelGenSettings s)
        {
            float rise = plan.FirstFloorY - plan.Ground;
            int steps = Risers(rise, s);
            float run = steps * s.CellSize;
            for (int i = 0; i < plan.Entrances.Count; i++)
            {
                var outward = plan.Outward[i];
                var centre = plan.Entrances[i] + outward * (WallThickness * 0.5f + run * 0.5f);
                Flight(level, id, centre, -outward, plan.Ground, rise, run, s.Buildings.SpineWidth, steps);
            }
        }

        /// <summary>The exterior flight, solid from the ground, and its landing, solid from the ground up to the door.</summary>
        private static void EmitExteriorFlight(LevelDescription level, BuildingPlan plan, int id)
        {
            var f = plan.Flight;
            Flight(level, id, f.Low + f.Uphill * (f.Run * 0.5f), f.Uphill, f.BottomY, f.TopY - f.BottomY, f.Run, f.Width, f.Risers);
            level.AddPrism(PrimitiveRole.Platform, level.AddPolygon(f.Landing), f.BottomY, f.TopY, Vector2.zero, OwnerKind.Building, id,
                Surfaces.All(SurfaceTag.Balcony));
        }

        /// <summary>
        /// One switchback per level the core climbs: flight A rises from the open end toward the wall, a landing
        /// against the wall, flight B rises back to the open end on the other half. Flight B floats a flight's rise
        /// up, so the floor under it keeps a player's height.
        /// </summary>
        private static void EmitCore(LevelDescription level, BuildingPlan plan, int id, in LevelGenSettings s)
        {
            var core = plan.Core;
            float fh = plan.FloorHeight;
            int risers = Risers(fh, s);
            int risersA = (risers + 1) / 2, risersB = risers - risersA;
            float riser = fh / risers;
            float riseA = risersA * riser, riseB = risersB * riser;
            float runA = risersA * s.CellSize, runB = risersB * s.CellSize;
            float flightWidth = core.Width * 0.5f;
            var towardWall = -core.Inward;

            for (int j = 0; j < plan.CoreLevels; j++)
            {
                float y = plan.CoreBottom + j * fh;
                // The first flight and landing stand on the floor; above that they float over the level below.
                float slack = j == 0 ? 0f : HeadroomSlack;
                var aCentre = core.Origin - core.Along * (flightWidth * 0.5f) + core.Inward * (CoreLanding + runA * 0.5f);
                var bCentre = core.Origin + core.Along * (flightWidth * 0.5f) + core.Inward * (CoreLanding + runB * 0.5f);
                Flight(level, id, aCentre, towardWall, y + slack, riseA - slack, runA, flightWidth, risersA);
                Flight(level, id, bCentre, core.Inward, y + riseA + HeadroomSlack, riseB - HeadroomSlack, runB, flightWidth, risersB);

                var landing = new List<Vector2>
                {
                    core.Origin - core.Along * (core.Width * 0.5f), core.Origin + core.Along * (core.Width * 0.5f),
                    core.Origin + core.Along * (core.Width * 0.5f) + core.Inward * CoreLanding,
                    core.Origin - core.Along * (core.Width * 0.5f) + core.Inward * CoreLanding,
                };
                if (Polygon.SignedArea(landing) < 0f) landing.Reverse();
                // It stops at the wall's inner face.
                landing = Polygon.ClipHalfPlane(landing, -core.Inward, -(Vector2.Dot(core.Inward, core.Origin) + WallThickness * 0.5f));
                // The first landing is solid down to the floor (a floor under a floating one would lack headroom);
                // above that a landing is a slab, so the one below keeps headroom where it meets flight B.
                float landingBottom = j == 0 ? y : y + riseA - LevelGenSettings.SlabThickness;
                level.AddPrism(PrimitiveRole.Platform, level.AddPolygon(landing), landingBottom, y + riseA, Vector2.zero,
                    OwnerKind.Building, id, Stairs);
            }
        }

        private static void Flight(LevelDescription level, int id, Vector2 centre, Vector2 uphill, float bottom, float rise,
            float run, float width, int steps)
        {
            level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Stairs,
                Shape = PrimitiveShape.Stairs,
                Position = new Vector3(centre.x, bottom, centre.y),
                Size = new Vector3(width, rise, run),
                YawDegrees = Mathf.Atan2(uphill.x, uphill.y) * Mathf.Rad2Deg,
                Steps = steps,
                OwnerKind = OwnerKind.Building,
                OwnerId = id,
                Surfaces = Stairs,
            });
        }

        /// <summary>The convex pieces of <paramref name="polygon"/> outside <paramref name="rect"/>, a rectangle in the
        /// core's frame: beside it on either side, beyond its open end, and behind its landing end.</summary>
        private static List<List<Vector2>> AroundRect(List<Vector2> polygon, List<Vector2> rect, in StairCoreDescription core)
        {
            float half = core.Width * 0.5f;
            float along0 = Vector2.Dot(core.Along, core.Origin);
            float inward0 = Vector2.Dot(core.Inward, core.Origin);
            var pieces = new List<List<Vector2>>
            {
                // Beside the core: along < -half, along > +half.
                Polygon.ClipHalfPlane(polygon, core.Along, along0 - half),
                Polygon.ClipHalfPlane(polygon, -core.Along, -(along0 + half)),
            };
            var middle = Polygon.ClipHalfPlane(polygon, -core.Along, -(along0 - half));
            middle = Polygon.ClipHalfPlane(middle, core.Along, along0 + half);
            pieces.Add(Polygon.ClipHalfPlane(middle, -core.Inward, -(inward0 + core.Length)));
            pieces.Add(Polygon.ClipHalfPlane(middle, core.Inward, inward0));
            pieces.RemoveAll(p => p.Count < 3);
            return pieces;
        }
    }
}
