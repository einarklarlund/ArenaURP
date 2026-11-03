using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Arena.LevelGen
{
    public enum PrimitiveRole : byte
    {
        Floor = 0,
        Platform = 1,
        Ramp = 2,
        Stairs = 3,
        Wall = 10,
        Cover = 11,
        Pillar = 12,
        /// <summary>A balcony or roof edge guard. It blocks like a wall but isn't cover.</summary>
        Railing = 13,
        Ceiling = 20,
    }

    public enum PrimitiveShape : byte
    {
        Box = 0,
        /// <summary>A wedge rising from its local -Z end (height 0) to its +Z end (full height).</summary>
        Ramp = 1,
        /// <summary>A solid flight rising toward local +Z in <see cref="Primitive.Steps"/> equal risers.</summary>
        Stairs = 2,
        /// <summary>A thin horizontal box: floors, platforms, ceilings.</summary>
        Slab = 3,
        /// <summary>
        /// A convex polygon from <see cref="LevelDescription.Polygons"/> extruded from <c>Position.y</c> up to a planar
        /// top. See <see cref="Primitive.PrismTop"/>. Size.x, Size.z and yaw are unused: the polygon is in level space.
        /// </summary>
        Prism = 4,
    }

    public enum OwnerKind : byte
    {
        Level = 0,
        Plot = 1,
        Transition = 2,
        Building = 3,
        Prop = 4,
        /// <summary>The walls around the level rectangle. They are exempt from the bounds check.</summary>
        Boundary = 5,
    }

    /// <summary>
    /// What a face is, for picking its material. Level tags map to a theme's level slots; building tags are generic and
    /// resolve through the building material profile drawn for the primitive's owner building. <see cref="None"/> is
    /// only for hand-made test geometry.
    /// </summary>
    public enum SurfaceTag : byte
    {
        None = 0,
        Ground = 1,
        GroundMound = 2,
        GroundDip = 3,
        GroundSinkhole = 4,
        CliffFace = 5,
        Ramp = 6,
        RampSide = 7,
        Bank = 8,
        BoundaryWall = 9,
        PropPlaceholder = 10,

        BuildingFloor = 32,
        BuildingCeiling = 33,
        OuterWallOutside = 34,
        OuterWallInside = 35,
        InteriorWall = 36,
        SlabEdge = 37,
        Balcony = 38,
        Railing = 39,
        Roof = 40,
        Stairs = 41,
        Plinth = 42,
        Stilts = 43,
    }

    public static class SurfaceTags
    {
        /// <summary>A building's tag, resolved through the building's material profile rather than the theme.</summary>
        public static bool IsBuilding(SurfaceTag tag) => tag >= SurfaceTag.BuildingFloor;
    }

    /// <summary>
    /// A primitive's surface tag per face group. A Box, Ramp or Stairs uses all five in local axes: top (+Y), bottom
    /// (−Y), front (+X), back (−X) and ends (±Z). A wall runs along local Z, so front and back are its two broad faces.
    /// A Prism uses top, bottom, and front for every side but those in <see cref="Primitive.BackSides"/>, which use back.
    /// </summary>
    public struct Surfaces
    {
        public SurfaceTag Top;
        public SurfaceTag Bottom;
        public SurfaceTag Front;
        public SurfaceTag Back;
        public SurfaceTag Ends;

        public static Surfaces All(SurfaceTag tag) =>
            new() { Top = tag, Bottom = tag, Front = tag, Back = tag, Ends = tag };

        /// <summary>A prism's or slab's tags: its top, its sides, and its bottom.</summary>
        public static Surfaces Of(SurfaceTag top, SurfaceTag sides, SurfaceTag bottom) =>
            new() { Top = top, Bottom = bottom, Front = sides, Back = sides, Ends = sides };
    }

    /// <summary>What a segment of the edge between two plots is to walk across.</summary>
    public enum TransitionType : byte
    {
        Flat = 0,
        Ledge = 1,
        Cliff = 2,
        Ramp = 3,
        Bank = 4,
        Blocked = 5,
    }

    public static class PrimitiveRoles
    {
        public const uint BlockerMask =
            (1u << (int)PrimitiveRole.Wall) | (1u << (int)PrimitiveRole.Cover) | (1u << (int)PrimitiveRole.Pillar)
            | (1u << (int)PrimitiveRole.Railing);

        public static bool IsWalkable(PrimitiveRole r) =>
            r is PrimitiveRole.Floor or PrimitiveRole.Platform or PrimitiveRole.Ramp or PrimitiveRole.Stairs;

        public static bool IsBlocker(PrimitiveRole r) =>
            r is PrimitiveRole.Wall or PrimitiveRole.Cover or PrimitiveRole.Pillar or PrimitiveRole.Railing;
    }

    public static class TransitionTypes
    {
        /// <summary>Walkable or jumpable both ways: what hard constraint 2 asks every plot to have.</summary>
        public static bool IsTwoWay(TransitionType t) =>
            t is TransitionType.Flat or TransitionType.Ledge or TransitionType.Ramp or TransitionType.Bank;
    }

    /// <summary>
    /// One piece of placed geometry. <see cref="Position"/> is the centre of the bottom face in level space
    /// (origin at the level's min corner, y up). <see cref="Size"/> is in local axes before yaw.
    /// </summary>
    public struct Primitive
    {
        public PrimitiveRole Role;
        public PrimitiveShape Shape;
        public Vector3 Position;
        public Vector3 Size;
        /// <summary>Rotation about +Y in degrees, as in <c>Quaternion.Euler(0, yaw, 0)</c>.</summary>
        public float YawDegrees;
        /// <summary>Riser count for <see cref="PrimitiveShape.Stairs"/>; 0 otherwise.</summary>
        public int Steps;
        public OwnerKind OwnerKind;
        public int OwnerId;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/> for <see cref="PrimitiveShape.Prism"/>.</summary>
        public int Polygon;
        /// <summary>A Prism's top gradient: rise per unit of level x and z.</summary>
        public Vector2 Slope;
        public Surfaces Surfaces;
        /// <summary>A Prism's sides that take <see cref="Surfaces.Back"/> rather than Front: bit k is the side from corner
        /// k to corner k + 1. A wall made a prism uses it for its inner face.</summary>
        public uint BackSides;

        /// <summary>
        /// A Prism's top height at level (x, z): <c>Position.y + Size.y</c> at the anchor <c>(Position.x, Position.z)</c>,
        /// tilted by <see cref="Slope"/>. The anchor is any point; the generator uses the polygon's centroid.
        /// </summary>
        public float PrismTop(float x, float z) =>
            Position.y + Size.y + Slope.x * (x - Position.x) + Slope.y * (z - Position.z);
    }

    /// <summary>The per-plot metric targets the allocation stage assigned.</summary>
    public struct PlotTargets
    {
        public float ElevationRange;
        public float ElevationFrequency;
    }

    public sealed class PlotDescription
    {
        public int Id;
        /// <summary>The plot a sub-plot was carved from, or -1 for a top-level plot.</summary>
        public int ParentId = -1;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/>: the outline, convex and counter-clockwise.</summary>
        public int Polygon;
        /// <summary>Ground height at the polygon's centroid.</summary>
        public float BaseHeight;
        /// <summary>Ground gradient (rise per unit of level x and z); zero for a flat plot.</summary>
        public Vector2 Slope;
        public PlotTargets Targets;
    }

    /// <summary>One segment of the edge between two plots, and how it is crossed.</summary>
    public sealed class TransitionDescription
    {
        public int Id;
        public TransitionType Type;
        public int PlotA;
        public int PlotB;
        /// <summary>Segment endpoints on the XZ plane.</summary>
        public Vector2 From;
        public Vector2 To;
        /// <summary>Walkable width of a ramp; the segment length otherwise.</summary>
        public float Width;
    }

    /// <summary>How a building's first floor sits on its plot.</summary>
    public enum FirstFloor : byte
    {
        /// <summary>Flush with the plot (a hair above it, so the floor is the building's own).</summary>
        Ground = 0,
        /// <summary>On a plinth 0.5–1.5 up, with steps at each entrance.</summary>
        Raised = 1,
        /// <summary>A floor height up on pillars, with open ground underneath as the spine.</summary>
        Stilts = 2,
    }

    /// <summary>A building's size class: big buildings have 2–3 taller floors and a balcony, small ones one floor.</summary>
    public enum BuildingClass : byte
    {
        Small = 0,
        Big = 1,
    }

    /// <summary>The buildings a level draws at the start of stage 4 (hard constraint 8 checks the built ones match).</summary>
    public enum BuildingMix : byte
    {
        /// <summary>None drawn: hand-built levels and fixtures.</summary>
        None = 0,
        OneBigTwoSmall = 1,
        TwoBig = 2,
        FourSmall = 3,
    }

    /// <summary>How the edge between two rooms is drawn.</summary>
    public enum EdgeKind : byte
    {
        /// <summary>An interior wall with door-wide gaps.</summary>
        Wall = 0,
        /// <summary>Nothing: the rooms' slabs meet, with a ledge where their levels differ.</summary>
        Open = 1,
        /// <summary>A railing on the higher room's side with a door-wide gap in its middle.</summary>
        Railing = 2,
    }

    /// <summary>A room: a convex piece of one floor between its edges, at its own level.</summary>
    public struct RoomDescription
    {
        public int Floor;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/>: inside the outer walls, up to the edges' centre
        /// lines.</summary>
        public int Polygon;
        /// <summary>Walking height of its slab.</summary>
        public float Height;
    }

    /// <summary>The edge two rooms of a floor share, and its gaps (where flights join rooms at different levels).</summary>
    public sealed class RoomEdgeDescription
    {
        public int Floor;
        /// <summary>Indices into <see cref="BuildingDescription.Rooms"/>: left and right of From → To.</summary>
        public int Left;
        public int Right;
        public EdgeKind Kind;
        public Vector2 From;
        public Vector2 To;
        /// <summary>Gaps as (start, end) distances from <see cref="From"/>.</summary>
        public List<Vector2> Gaps = new();
    }

    /// <summary>A building's switchback stair core, in the frame of the outer wall it stands against.</summary>
    public struct StairCoreDescription
    {
        /// <summary>The middle of the core's end on the wall's inner face.</summary>
        public Vector2 Origin;
        /// <summary>Unit vector along the wall.</summary>
        public Vector2 Along;
        /// <summary>Unit vector into the building.</summary>
        public Vector2 Inward;
        /// <summary>Width across both flights, and length from the wall to where the flights end.</summary>
        public float Width;
        public float Length;
    }

    /// <summary>An outdoor space a building's floor opens onto: a slab balcony, a terrace on the roof of a larger
    /// floor below, or the landing of an exterior flight.</summary>
    public sealed class BalconyDescription
    {
        public int Floor;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/>: the slab or landing (for a terrace, the larger
        /// floor below, of which the part outside this floor is the terrace).</summary>
        public int Polygon;
        public bool Terrace;
        /// <summary>The door from the floor onto it, on the floor's wall, and the wall's outward normal there.</summary>
        public Vector2 Door;
        public Vector2 Outward;
    }

    /// <summary>A door or window in an outer wall of one floor.</summary>
    public struct OpeningDescription
    {
        public int Floor;
        /// <summary>The floor footprint's side it is on, and its centre's distance along it from the side's first vertex.</summary>
        public int Side;
        public float T;
        public float HalfWidth;
        /// <summary>Above the floor: 0 for a door, the sill for a window.</summary>
        public float Bottom;
        public float Top;
        public bool Door;
    }

    /// <summary>A floor-plan wall (an edge drawn as a wall): a chord of its floor between the outer walls' inside faces,
    /// with doorways.</summary>
    public sealed class InteriorWallDescription
    {
        public int Floor;
        public Vector2 From;
        public Vector2 To;
        /// <summary>Doorways as (start, end) distances from <see cref="From"/>; each runs from the floor up to
        /// <see cref="GapTop"/> above the floor's level (a door's height over the higher room either side).</summary>
        public List<Vector2> Gaps = new();
        public float GapTop;
    }

    /// <summary>A terrain sub-room: its outline and the surface its top was tagged with.</summary>
    public struct SubRoomDescription
    {
        public int Plot;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/>, convex and counter-clockwise.</summary>
        public int Polygon;
        public SurfaceTag Surface;
        /// <summary>The point its ground is fanned from: each fan triangle is (site, corner, next corner).</summary>
        public Vector2 Site;
    }

    public sealed class BuildingDescription
    {
        public int Id;
        public int Plot;
        public BuildingClass Class;
        /// <summary>Index into <see cref="LevelDescription.Polygons"/>: the first floor's footprint, convex and
        /// counter-clockwise.</summary>
        public int Footprint;
        /// <summary>Per floor, its footprint: the first floor's scaled about the core, the same shape.</summary>
        public List<int> FloorFootprints = new();
        public List<BalconyDescription> Balconies = new();
        public bool HasExteriorFlight;
        public List<OpeningDescription> Openings = new();
        /// <summary>The edges drawn as walls (every edge, of every kind, is in <see cref="Edges"/>).</summary>
        public List<InteriorWallDescription> Walls = new();
        /// <summary>Every floor's rooms, floor by floor.</summary>
        public List<RoomDescription> Rooms = new();
        public List<RoomEdgeDescription> Edges = new();
        public FirstFloor Placement;
        /// <summary>Walking height of the first floor.</summary>
        public float FirstFloorHeight;
        public int Floors;
        public float FloorHeight;
        /// <summary>Door centres on the footprint's edges (on the ground under the building for stilts).</summary>
        public List<Vector2> Entrances = new();
        /// <summary>Indices into <see cref="LevelDescription.Polygons"/>: the lobby and the strips to each entrance.</summary>
        public List<int> Spine = new();
        public bool HasCore;
        public StairCoreDescription Core;
        public bool RoofReachable;

        public float FloorY(int floor) => FirstFloorHeight + floor * FloorHeight;
        public float RoofY => FloorY(Floors);
        /// <summary>Walking height of the ground-level route through the building: the ground under stilts, the
        /// first floor otherwise.</summary>
        public float SpineY(float plotHeight) => Placement == FirstFloor.Stilts ? plotHeight : FirstFloorHeight;
    }

    /// <summary>One prop: which variant of its kind, and where the variant's pivot (its bottom centre) goes. Its
    /// placeholder primitives are owned by <c>(Prop, Id)</c>.</summary>
    public sealed class PropDescription
    {
        public int Id;
        public PropKind Kind;
        /// <summary>Index into the kind's variants in the config's prop catalogue.</summary>
        public int Variant;
        public Vector3 Position;
        /// <summary>Rotation about +Y; the variant's +Z runs along the prop's length.</summary>
        public float YawDegrees;
        /// <summary>Scale along the variant's +Z: a fence piece fitted to its run. 1 otherwise.</summary>
        public float Stretch = 1f;
    }

    /// <summary>Which of the theme's light colours a fixture takes.</summary>
    public enum LightSlot : byte
    {
        Room = 0,
        Entrance = 1,
        Porch = 2,
    }

    /// <summary>
    /// A light fixture's light. The builder makes real point lights (no shadows) for the few with the highest
    /// <see cref="Priority"/>, and lets the rest only glow.
    /// </summary>
    public struct LightDescription
    {
        /// <summary>The fixture prop it shines from (<see cref="PropDescription.Id"/>).</summary>
        public int Prop;
        public Vector3 Position;
        public float Range;
        public LightSlot Slot;
        /// <summary>Higher shines first: room lights by their room's area, then entrance lamps, then porch lamps.</summary>
        public float Priority;
    }

    /// <summary>Where a pawn can spawn: on open outdoor ground, facing the level's middle.</summary>
    public struct SpawnPoint
    {
        /// <summary>On the ground: a pawn's feet go here.</summary>
        public Vector3 Position;
        public float YawDegrees;
    }

    /// <summary>What a chest site is next to or inside. Directors pick by kind (see <see cref="SiteStage"/>).</summary>
    public enum ChestSiteKind : byte
    {
        SinkholeMiddle = 0,
        /// <summary>Inside a corner of the boundary walls.</summary>
        BorderCorner = 1,
        /// <summary>A ground-floor building room.</summary>
        BuildingGround = 2,
        /// <summary>A room on a building's second floor or higher.</summary>
        BuildingUpper = 3,
        Balcony = 4,
        Hilltop = 5,
        ValleyBottom = 6,
        BehindFence = 7,
        NearCar = 8,
        /// <summary>Plain ground, added so every room has <see cref="SiteStage.MinSitesPerRoom"/> sites.</summary>
        OpenGround = 9,
    }

    /// <summary>A place a chest fits: on a surface in R (not a sink) with clear ground round it.</summary>
    public struct ChestSite
    {
        public ChestSiteKind Kind;
        /// <summary>The room (plot index) it is in, as <see cref="RoomGraph.RoomAt"/> finds it.</summary>
        public int Room;
        /// <summary>On the surface: the chest's bottom centre goes here.</summary>
        public Vector3 Position;
        /// <summary>Faces the room's middle.</summary>
        public float YawDegrees;
    }

    /// <summary>The feature a pickup line runs along.</summary>
    public enum PickupLineKind : byte
    {
        BuildingEdge = 0,
        BoundaryWall = 1,
        Fence = 2,
        Balcony = 3,
        SinkholeEdge = 4,
        /// <summary>Along the top of a ledge transition.</summary>
        Ledge = 5,
        /// <summary>Up a ramp or bank, from its foot to its top.</summary>
        RampPath = 6,
    }

    /// <summary><see cref="SiteStage.LinePoints"/> evenly spaced points lining a feature, each on a surface in R.</summary>
    public sealed class PickupLine
    {
        public PickupLineKind Kind;
        /// <summary>The room its middle point is in.</summary>
        public int Room;
        public List<Vector3> Points = new();
    }

    /// <summary>Two rooms (plots, <c>RoomA &lt; RoomB</c>) that share at least one transition, and which ways a player
    /// can cross: a flat, ledge, ramp or bank goes both ways, a cliff only down, a blocked segment neither.</summary>
    public struct RoomLink
    {
        public int RoomA;
        public int RoomB;
        public bool AToB;
        public bool BToA;
    }

    /// <summary>
    /// The generator's output, and the only thing that leaves the core. Plain data: the server sends it,
    /// clients build it. <see cref="Serialize"/> is byte-stable, so equal bytes mean equal levels.
    /// </summary>
    public sealed class LevelDescription
    {
        public int GeneratorVersion;
        public ulong Seed;
        public ulong ConfigHash;
        public Vector3 Size;
        /// <summary>The building mix stage 4 drew.</summary>
        public BuildingMix Mix;
        /// <summary>The polygon pool: Prism outlines and plot outlines, shared by index.</summary>
        public List<List<Vector2>> Polygons = new();
        public List<PlotDescription> Plots = new();
        public List<TransitionDescription> Transitions = new();
        public List<BuildingDescription> Buildings = new();
        public List<PropDescription> Props = new();
        public List<LightDescription> Lights = new();
        public List<SubRoomDescription> SubRooms = new();
        public List<SpawnPoint> SpawnPoints = new();
        public List<ChestSite> ChestSites = new();
        public List<PickupLine> PickupLines = new();
        /// <summary>The room graph's edges, sorted by (RoomA, RoomB). Rooms are the plots.</summary>
        public List<RoomLink> RoomLinks = new();
        public List<Primitive> Primitives = new();

        /// <summary>Adds a polygon to the pool and returns its index.</summary>
        public int AddPolygon(IEnumerable<Vector2> polygon)
        {
            Polygons.Add(new List<Vector2>(polygon));
            return Polygons.Count - 1;
        }

        public List<Vector2> PolygonOf(PlotDescription plot) => Polygons[plot.Polygon];

        /// <summary>
        /// Adds a Prism over pool polygon <paramref name="polygon"/>, solid from <paramref name="bottom"/> up to a top
        /// that is <paramref name="top"/> at the polygon's centroid and tilts by <paramref name="slope"/>.
        /// </summary>
        /// <summary>A prism over a polygon, unless the polygon is narrower than <see cref="LevelGen.Polygon.MinSolidWidth"/>:
        /// such a needle is what clipping and splitting leave along an edge, and it lies a hair over its neighbours.
        /// Returns whether it was added.</summary>
        public bool AddPrism(PrimitiveRole role, int polygon, float bottom, float top, Vector2 slope, OwnerKind owner,
            int ownerId, Surfaces surfaces)
        {
            if (LevelGen.Polygon.MinWidth(Polygons[polygon]) <= LevelGen.Polygon.MinSolidWidth) return false;
            var anchor = LevelGen.Polygon.Centroid(Polygons[polygon]);
            Primitives.Add(new Primitive
            {
                Role = role,
                Shape = PrimitiveShape.Prism,
                Position = new Vector3(anchor.x, bottom, anchor.y),
                Size = new Vector3(0f, top - bottom, 0f),
                Polygon = polygon,
                Slope = slope,
                OwnerKind = owner,
                OwnerId = ownerId,
                Surfaces = surfaces,
            });
            return true;
        }

        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream))
            {
                w.Write(GeneratorVersion);
                w.Write(Seed);
                w.Write(ConfigHash);
                Write(w, Size);
                w.Write((byte)Mix);

                w.Write(Polygons.Count);
                foreach (var polygon in Polygons)
                {
                    w.Write(polygon.Count);
                    foreach (var p in polygon) Write(w, p);
                }

                w.Write(Plots.Count);
                foreach (var plot in Plots)
                {
                    w.Write(plot.Id);
                    w.Write(plot.ParentId);
                    w.Write(plot.Polygon);
                    w.Write(plot.BaseHeight);
                    Write(w, plot.Slope);
                    w.Write(plot.Targets.ElevationRange);
                    w.Write(plot.Targets.ElevationFrequency);
                }

                w.Write(Transitions.Count);
                foreach (var t in Transitions)
                {
                    w.Write(t.Id);
                    w.Write((byte)t.Type);
                    w.Write(t.PlotA);
                    w.Write(t.PlotB);
                    Write(w, t.From);
                    Write(w, t.To);
                    w.Write(t.Width);
                }

                w.Write(Buildings.Count);
                foreach (var b in Buildings)
                {
                    w.Write(b.Id);
                    w.Write(b.Plot);
                    w.Write(b.Footprint);
                    w.Write((byte)b.Placement);
                    w.Write(b.FirstFloorHeight);
                    w.Write(b.Floors);
                    w.Write(b.FloorHeight);
                    w.Write(b.Entrances.Count);
                    foreach (var e in b.Entrances) Write(w, e);
                    w.Write(b.Spine.Count);
                    foreach (int polygon in b.Spine) w.Write(polygon);
                    w.Write(b.HasCore);
                    Write(w, b.Core.Origin);
                    Write(w, b.Core.Along);
                    Write(w, b.Core.Inward);
                    w.Write(b.Core.Width);
                    w.Write(b.Core.Length);
                    w.Write(b.RoofReachable);
                    w.Write(b.FloorFootprints.Count);
                    foreach (int polygon in b.FloorFootprints) w.Write(polygon);
                    w.Write(b.Balconies.Count);
                    foreach (var balcony in b.Balconies)
                    {
                        w.Write(balcony.Floor);
                        w.Write(balcony.Polygon);
                        w.Write(balcony.Terrace);
                        Write(w, balcony.Door);
                        Write(w, balcony.Outward);
                    }
                    w.Write(b.HasExteriorFlight);
                    w.Write(b.Openings.Count);
                    foreach (var o in b.Openings)
                    {
                        w.Write(o.Floor);
                        w.Write(o.Side);
                        w.Write(o.T);
                        w.Write(o.HalfWidth);
                        w.Write(o.Bottom);
                        w.Write(o.Top);
                        w.Write(o.Door);
                    }
                    w.Write(b.Walls.Count);
                    foreach (var wall in b.Walls)
                    {
                        w.Write(wall.Floor);
                        Write(w, wall.From);
                        Write(w, wall.To);
                        w.Write(wall.GapTop);
                        w.Write(wall.Gaps.Count);
                        foreach (var gap in wall.Gaps) Write(w, gap);
                    }
                    w.Write((byte)b.Class);
                    w.Write(b.Rooms.Count);
                    foreach (var room in b.Rooms)
                    {
                        w.Write(room.Floor);
                        w.Write(room.Polygon);
                        w.Write(room.Height);
                    }
                    w.Write(b.Edges.Count);
                    foreach (var edge in b.Edges)
                    {
                        w.Write(edge.Floor);
                        w.Write(edge.Left);
                        w.Write(edge.Right);
                        w.Write((byte)edge.Kind);
                        Write(w, edge.From);
                        Write(w, edge.To);
                        w.Write(edge.Gaps.Count);
                        foreach (var gap in edge.Gaps) Write(w, gap);
                    }
                }

                w.Write(Props.Count);
                foreach (var prop in Props)
                {
                    w.Write(prop.Id);
                    w.Write((byte)prop.Kind);
                    w.Write(prop.Variant);
                    Write(w, prop.Position);
                    w.Write(prop.YawDegrees);
                    w.Write(prop.Stretch);
                }

                w.Write(Lights.Count);
                foreach (var light in Lights)
                {
                    w.Write(light.Prop);
                    Write(w, light.Position);
                    w.Write(light.Range);
                    w.Write((byte)light.Slot);
                    w.Write(light.Priority);
                }

                w.Write(SubRooms.Count);
                foreach (var room in SubRooms)
                {
                    w.Write(room.Plot);
                    w.Write(room.Polygon);
                    w.Write((byte)room.Surface);
                    Write(w, room.Site);
                }

                w.Write(SpawnPoints.Count);
                foreach (var spawn in SpawnPoints)
                {
                    Write(w, spawn.Position);
                    w.Write(spawn.YawDegrees);
                }

                w.Write(ChestSites.Count);
                foreach (var site in ChestSites)
                {
                    w.Write((byte)site.Kind);
                    w.Write(site.Room);
                    Write(w, site.Position);
                    w.Write(site.YawDegrees);
                }

                w.Write(PickupLines.Count);
                foreach (var line in PickupLines)
                {
                    w.Write((byte)line.Kind);
                    w.Write(line.Room);
                    w.Write(line.Points.Count);
                    foreach (var p in line.Points) Write(w, p);
                }

                w.Write(RoomLinks.Count);
                foreach (var link in RoomLinks)
                {
                    w.Write(link.RoomA);
                    w.Write(link.RoomB);
                    w.Write(link.AToB);
                    w.Write(link.BToA);
                }

                w.Write(Primitives.Count);
                foreach (var p in Primitives)
                {
                    w.Write((byte)p.Role);
                    w.Write((byte)p.Shape);
                    Write(w, p.Position);
                    Write(w, p.Size);
                    w.Write(p.YawDegrees);
                    w.Write(p.Steps);
                    w.Write((byte)p.OwnerKind);
                    w.Write(p.OwnerId);
                    w.Write((byte)p.Surfaces.Top);
                    w.Write((byte)p.Surfaces.Bottom);
                    w.Write((byte)p.Surfaces.Front);
                    w.Write((byte)p.Surfaces.Back);
                    w.Write((byte)p.Surfaces.Ends);
                    if (p.Shape != PrimitiveShape.Prism) continue;
                    w.Write(p.Polygon);
                    Write(w, p.Slope);
                    w.Write(p.BackSides);
                }
            }
            return stream.ToArray();
        }

        public ulong ComputeHash() => StableHash.Fnv1a64(Serialize());

        /// <summary>Reads what <see cref="Serialize"/> wrote: a description sent whole (the network fallback) comes back
        /// with the same bytes and hash.</summary>
        public static LevelDescription Deserialize(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            var d = new LevelDescription
            {
                GeneratorVersion = r.ReadInt32(), Seed = r.ReadUInt64(), ConfigHash = r.ReadUInt64(), Size = ReadVector3(r),
                Mix = (BuildingMix)r.ReadByte(),
            };

            int polygons = r.ReadInt32();
            for (int i = 0; i < polygons; i++)
            {
                var polygon = new List<Vector2>(r.ReadInt32());
                for (int k = polygon.Capacity; k > 0; k--) polygon.Add(ReadVector2(r));
                d.Polygons.Add(polygon);
            }

            int plots = r.ReadInt32();
            for (int i = 0; i < plots; i++)
                d.Plots.Add(new PlotDescription
                {
                    Id = r.ReadInt32(), ParentId = r.ReadInt32(), Polygon = r.ReadInt32(), BaseHeight = r.ReadSingle(),
                    Slope = ReadVector2(r),
                    Targets = new PlotTargets { ElevationRange = r.ReadSingle(), ElevationFrequency = r.ReadSingle() },
                });

            int transitions = r.ReadInt32();
            for (int i = 0; i < transitions; i++)
                d.Transitions.Add(new TransitionDescription
                {
                    Id = r.ReadInt32(), Type = (TransitionType)r.ReadByte(), PlotA = r.ReadInt32(), PlotB = r.ReadInt32(),
                    From = ReadVector2(r), To = ReadVector2(r), Width = r.ReadSingle(),
                });

            int buildings = r.ReadInt32();
            for (int i = 0; i < buildings; i++)
            {
                var b = new BuildingDescription
                {
                    Id = r.ReadInt32(), Plot = r.ReadInt32(), Footprint = r.ReadInt32(), Placement = (FirstFloor)r.ReadByte(),
                    FirstFloorHeight = r.ReadSingle(), Floors = r.ReadInt32(), FloorHeight = r.ReadSingle(),
                };
                for (int k = r.ReadInt32(); k > 0; k--) b.Entrances.Add(ReadVector2(r));
                for (int k = r.ReadInt32(); k > 0; k--) b.Spine.Add(r.ReadInt32());
                b.HasCore = r.ReadBoolean();
                b.Core = new StairCoreDescription
                {
                    Origin = ReadVector2(r), Along = ReadVector2(r), Inward = ReadVector2(r), Width = r.ReadSingle(),
                    Length = r.ReadSingle(),
                };
                b.RoofReachable = r.ReadBoolean();
                for (int k = r.ReadInt32(); k > 0; k--) b.FloorFootprints.Add(r.ReadInt32());
                for (int k = r.ReadInt32(); k > 0; k--)
                    b.Balconies.Add(new BalconyDescription
                    {
                        Floor = r.ReadInt32(), Polygon = r.ReadInt32(), Terrace = r.ReadBoolean(), Door = ReadVector2(r),
                        Outward = ReadVector2(r),
                    });
                b.HasExteriorFlight = r.ReadBoolean();
                for (int k = r.ReadInt32(); k > 0; k--)
                    b.Openings.Add(new OpeningDescription
                    {
                        Floor = r.ReadInt32(), Side = r.ReadInt32(), T = r.ReadSingle(), HalfWidth = r.ReadSingle(),
                        Bottom = r.ReadSingle(), Top = r.ReadSingle(), Door = r.ReadBoolean(),
                    });
                for (int k = r.ReadInt32(); k > 0; k--)
                {
                    var wall = new InteriorWallDescription
                    {
                        Floor = r.ReadInt32(), From = ReadVector2(r), To = ReadVector2(r), GapTop = r.ReadSingle(),
                    };
                    for (int g = r.ReadInt32(); g > 0; g--) wall.Gaps.Add(ReadVector2(r));
                    b.Walls.Add(wall);
                }
                b.Class = (BuildingClass)r.ReadByte();
                for (int k = r.ReadInt32(); k > 0; k--)
                    b.Rooms.Add(new RoomDescription { Floor = r.ReadInt32(), Polygon = r.ReadInt32(), Height = r.ReadSingle() });
                for (int k = r.ReadInt32(); k > 0; k--)
                {
                    var edge = new RoomEdgeDescription
                    {
                        Floor = r.ReadInt32(), Left = r.ReadInt32(), Right = r.ReadInt32(), Kind = (EdgeKind)r.ReadByte(),
                        From = ReadVector2(r), To = ReadVector2(r),
                    };
                    for (int g = r.ReadInt32(); g > 0; g--) edge.Gaps.Add(ReadVector2(r));
                    b.Edges.Add(edge);
                }
                d.Buildings.Add(b);
            }

            int props = r.ReadInt32();
            for (int i = 0; i < props; i++)
                d.Props.Add(new PropDescription
                {
                    Id = r.ReadInt32(), Kind = (PropKind)r.ReadByte(), Variant = r.ReadInt32(), Position = ReadVector3(r),
                    YawDegrees = r.ReadSingle(), Stretch = r.ReadSingle(),
                });

            int lights = r.ReadInt32();
            for (int i = 0; i < lights; i++)
                d.Lights.Add(new LightDescription
                {
                    Prop = r.ReadInt32(), Position = ReadVector3(r), Range = r.ReadSingle(), Slot = (LightSlot)r.ReadByte(),
                    Priority = r.ReadSingle(),
                });

            int subRooms = r.ReadInt32();
            for (int i = 0; i < subRooms; i++)
                d.SubRooms.Add(new SubRoomDescription
                {
                    Plot = r.ReadInt32(), Polygon = r.ReadInt32(), Surface = (SurfaceTag)r.ReadByte(), Site = ReadVector2(r),
                });

            int spawns = r.ReadInt32();
            for (int i = 0; i < spawns; i++)
                d.SpawnPoints.Add(new SpawnPoint { Position = ReadVector3(r), YawDegrees = r.ReadSingle() });

            int sites = r.ReadInt32();
            for (int i = 0; i < sites; i++)
                d.ChestSites.Add(new ChestSite
                {
                    Kind = (ChestSiteKind)r.ReadByte(), Room = r.ReadInt32(), Position = ReadVector3(r), YawDegrees = r.ReadSingle(),
                });

            int lines = r.ReadInt32();
            for (int i = 0; i < lines; i++)
            {
                var line = new PickupLine { Kind = (PickupLineKind)r.ReadByte(), Room = r.ReadInt32() };
                for (int k = r.ReadInt32(); k > 0; k--) line.Points.Add(ReadVector3(r));
                d.PickupLines.Add(line);
            }

            int links = r.ReadInt32();
            for (int i = 0; i < links; i++)
                d.RoomLinks.Add(new RoomLink
                {
                    RoomA = r.ReadInt32(), RoomB = r.ReadInt32(), AToB = r.ReadBoolean(), BToA = r.ReadBoolean(),
                });

            int primitives = r.ReadInt32();
            for (int i = 0; i < primitives; i++)
            {
                var p = new Primitive
                {
                    Role = (PrimitiveRole)r.ReadByte(), Shape = (PrimitiveShape)r.ReadByte(), Position = ReadVector3(r),
                    Size = ReadVector3(r), YawDegrees = r.ReadSingle(), Steps = r.ReadInt32(),
                    OwnerKind = (OwnerKind)r.ReadByte(), OwnerId = r.ReadInt32(),
                    Surfaces = new Surfaces
                    {
                        Top = (SurfaceTag)r.ReadByte(), Bottom = (SurfaceTag)r.ReadByte(), Front = (SurfaceTag)r.ReadByte(),
                        Back = (SurfaceTag)r.ReadByte(), Ends = (SurfaceTag)r.ReadByte(),
                    },
                };
                if (p.Shape == PrimitiveShape.Prism)
                {
                    p.Polygon = r.ReadInt32();
                    p.Slope = ReadVector2(r);
                    p.BackSides = r.ReadUInt32();
                }
                d.Primitives.Add(p);
            }
            return d;
        }

        private static Vector3 ReadVector3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        private static Vector2 ReadVector2(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());

        private static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x); w.Write(v.y); w.Write(v.z);
        }

        private static void Write(BinaryWriter w, Vector2 v)
        {
            w.Write(v.x); w.Write(v.y);
        }
    }
}
