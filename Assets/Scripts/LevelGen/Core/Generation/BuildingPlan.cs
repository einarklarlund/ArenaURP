using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A gap in an outer wall: a door (from the floor up) or a window (from the sill up).</summary>
    public struct WallOpening
    {
        /// <summary>The footprint side it is on, and its centre's distance along it from the side's first vertex.</summary>
        public int Side;
        public float T;
        public float HalfWidth;
        /// <summary>Above the floor: 0 for a door, the sill height for a window.</summary>
        public float Bottom;
        public float Top;
        public bool Door;
    }

    /// <summary>
    /// An edge between two rooms: a chord of its floor, drawn as a wall with door-wide gaps, a railing with a door-wide
    /// gap in its middle, or nothing at all (gaps spine-wide where it crosses the spine). Chords never cross, so each one
    /// is one edge with one room on either side.
    /// </summary>
    public sealed class InteriorWall
    {
        public Vector2 From;
        public Vector2 To;
        /// <summary>Gaps as (start, end) distances from <see cref="From"/>. On an open edge they are where flights go.</summary>
        public List<Vector2> Gaps = new();
        public EdgeKind Kind;
        /// <summary>The rooms (indices into <see cref="FloorPlan.Rooms"/>) to the left and right of From → To.</summary>
        public int Left;
        public int Right;
        /// <summary>Short flights filling the gaps when the two rooms sit at different levels.</summary>
        public List<RoomFlight> Flights = new();
    }

    /// <summary>A convex piece of a floor between its edges, at its own level.</summary>
    public sealed class RoomPlan
    {
        /// <summary>The piece of the floor inside the outer walls, up to the edges' centre lines.</summary>
        public List<Vector2> Polygon;
        /// <summary>Height above (or, negative, below) the floor's level: whole risers.</summary>
        public float Offset;
        /// <summary>It holds an outside door or a stair-core landing, so it keeps the floor's level.</summary>
        public bool Fixed;
    }

    /// <summary>A short flight across an edge's gap, from the lower room's level up to the higher's.</summary>
    public sealed class RoomFlight
    {
        /// <summary>The middle of its top end, on the edge (or the wall's face), and the unit direction down into the
        /// lower room.</summary>
        public Vector2 Top;
        public Vector2 Down;
        public float Width;
        public float Run;
        public int Risers;
        /// <summary>Offsets of its foot and top from the floor's level.</summary>
        public float Low;
        public float High;
    }

    public sealed class FloorPlan
    {
        public List<Vector2> Footprint;
        /// <summary>Size relative to the floor below (1 for the first floor).</summary>
        public float Scale = 1f;
        public List<WallOpening> Openings = new();
        /// <summary>The edges between rooms (see <see cref="InteriorWall"/>).</summary>
        public List<InteriorWall> Walls = new();
        /// <summary>The floor inside its outer walls cut along the edges. Empty means one room at the floor's level.</summary>
        public List<RoomPlan> Rooms = new();
    }

    /// <summary>
    /// An outdoor space a floor opens onto: a slab balcony outside a wall, a terrace (the roof of the larger floor
    /// below, outside this one), or the landing at the top of an exterior flight.
    /// </summary>
    public sealed class BalconyPlan
    {
        public int Floor;
        public bool Terrace;
        public bool Landing;
        /// <summary>The slab or landing; for a terrace, the larger floor below.</summary>
        public List<Vector2> Polygon;
        public Vector2 Door;
        public Vector2 Outward;
        /// <summary>A slab balcony's open edges, where its railing runs.</summary>
        public List<(Vector2 a, Vector2 b)> Railings = new();
    }

    /// <summary>A straight flight outside an outer wall, from the ground up to a landing at a floor's door.</summary>
    public sealed class ExteriorFlightPlan
    {
        public int Side;
        /// <summary>The middle of the flight's low end, and the unit direction it climbs along the wall.</summary>
        public Vector2 Low;
        public Vector2 Uphill;
        public float Width;
        public float Run;
        public int Risers;
        public float BottomY;
        public float TopY;
        public List<Vector2> Landing;
    }

    /// <summary>Stage 4's plan for one building, before geometry.</summary>
    public sealed class BuildingPlan
    {
        public int Plot;
        public BuildingClass Class;
        public float Ground;
        public bool FullPlot;
        /// <summary>The first floor's footprint (as <c>FloorPlans[0].Footprint</c>).</summary>
        public List<Vector2> Footprint;
        public FirstFloor Placement;
        public float FirstFloorY;
        public float FloorHeight;
        public int Floors;
        public List<Vector2> Entrances = new();
        /// <summary>Per entrance, the outward unit normal of its side.</summary>
        public List<Vector2> Outward = new();
        /// <summary>The lobby, then one strip per entrance.</summary>
        public List<List<Vector2>> Spine = new();
        public bool HasCore;
        public StairCoreDescription Core;
        /// <summary>The point floors are scaled about: on the wall the core stands against (nearest the core's centre), or
        /// the first floor's centroid.</summary>
        public Vector2 Anchor;
        public List<FloorPlan> FloorPlans = new();
        public List<BalconyPlan> Balconies = new();
        public ExteriorFlightPlan Flight;
        public bool RoofReachable;
        /// <summary>The roof is reached by the exterior flight (a small building's way up), not by the core.</summary>
        public bool RoofByFlight;
        /// <summary>The ground-level porch's slabs, outside the outer walls (a stilted building's porch is a balcony).</summary>
        public List<List<Vector2>> Porches = new();
        /// <summary>Plans made, including the one kept (a failed local check retries from the next sub-seed).</summary>
        public int Attempts = 1;
        /// <summary>Why the building failed (its level attempt fails with it), or null.</summary>
        public string Dropped;
        /// <summary>Notes on what was cut back: floors that didn't fit, a core that didn't fit, failed local checks.</summary>
        public List<string> Notes = new();

        public float FloorY(int floor) => FirstFloorY + floor * FloorHeight;
        public float RoofY => FloorY(Floors);
        /// <summary>Height the core starts from: the ground under stilts, the first floor otherwise.</summary>
        public float CoreBottom => Placement == FirstFloor.Stilts ? Ground : FirstFloorY;
        /// <summary>Flight pairs the core has: one per floor it climbs to, and one more up to a reachable roof.</summary>
        public int CoreLevels => !HasCore ? 0 : (Placement == FirstFloor.Stilts ? Floors : Floors - 1) + (RoofByCore ? 1 : 0);
        public bool RoofByCore => RoofReachable && !RoofByFlight;
        /// <summary>Whether a floor is off the ground: every floor but a ground-level or raised first floor.</summary>
        public bool Elevated(int floor) => floor > 0 || Placement == FirstFloor.Stilts;
    }
}
