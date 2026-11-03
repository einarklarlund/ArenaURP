using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>One prop: a variant's footprint (a box, or a trunk and canopy) yawed along a plot edge.</summary>
    public sealed class PropPlan
    {
        public PropKind Kind;
        /// <summary>Index into the kind's variants in the settings, and that variant's shape.</summary>
        public int Variant;
        public PropShape Shape;
        /// <summary>A fence piece's length over its variant's (fitted to its run); 1 otherwise.</summary>
        public float Stretch = 1f;
        /// <summary>The leaf it stands on.</summary>
        public int Leaf;
        public Vector2 Centre;
        /// <summary>Unit direction of its length, along the nearest edge.</summary>
        public Vector2 Along;
        /// <summary>The ground area it takes: along <see cref="Along"/>, and across.</summary>
        public float Length;
        public float Width;
        /// <summary>Its foot: the level ground under a car or fence, or under a tree's trunk.</summary>
        public float Bottom;
        /// <summary>Its height from its bottom.</summary>
        public float Height;
        /// <summary>The footprint, counter-clockwise (a tree's canopy for a tree).</summary>
        public List<Vector2> Footprint;
        /// <summary>A light fixture's light (its prop is filled in when the props are emitted); null for other props.</summary>
        public LightDescription? Light;
    }

    /// <summary>
    /// Stage 7, props. Fences come first: rings round buildings on inscribed footprints, then rooms' fences (all their
    /// sides, or some), then rings round sinkholes (see <see cref="Ring"/>). Then rows of parked cars along the boundary
    /// walls (see <see cref="CarRows"/>). Cars and trees stand in a band 1–3 units inside every room's edges,
    /// <c>density</c> per 100 units of edge, chosen by their weights and yawed to the edge.
    /// Each try at a car or tree draws its variant first and places that variant's footprint; each fence run draws one
    /// variant, so a fence line looks the same along its length. Last, the buildings' interior props and light fixtures
    /// (see <see cref="InteriorStage"/>).
    /// Keep-out zones protect every ramp and bank (plus a 2-unit run-out at its foot, and where it arrives on the higher
    /// plot), a 2-unit strip each side of every flat and ledge segment, buildings with their porches (see
    /// <see cref="Porch"/>), each entrance's clear zone and a spine-wide extension out from it, and exterior flights.
    /// </summary>
    public static class PropStage
    {
        /// <summary>Props keep this far from keep-out zones and from each other.</summary>
        public const float Clearance = 0.5f;
        /// <summary>A fence piece stretches along its length by up to this much either way, so a run takes whole pieces.</summary>
        public const float MaxFenceStretch = 0.25f;
        /// <summary>Rows of parked cars end this far short of their wall's ends.</summary>
        public const float CarRowEndGap = 1f;

        // Families of random streams beside the per-room ones, each indexed by building, plot or outline edge.
        private const int BuildingFenceStreams = -1, SinkholeFenceStreams = -2, CarRowStreams = -3;

        /// <param name="ground">The ground of each plot (leaf): its plane, or its terrain.</param>
        /// <param name="terrains">The terrain plans, for sinkholes. Without them sinkholes get no rings.</param>
        public static List<PropPlan> Build(PlotPartition leaves, PlotPlan plan, List<BuildingPlan> buildings,
            GroundSurface ground, in LevelGenSettings s, ulong seed, List<TerrainPlan> terrains = null)
        {
            var props = new List<PropPlan>();
            var p = s.Props;
            var keepOut = KeepOut(leaves, plan, buildings, s);
            var taken = new List<List<Vector2>>();
            var fencing = new Fencing
            {
                Props = props, Taken = taken, KeepOut = KeepOut(leaves, plan, buildings, s, gates: true),
                Leaves = leaves, Ground = ground, Settings = s,
            };
            foreach (var v in p.Fences) fencing.Thickest = Mathf.Max(fencing.Thickest, v.Width);

            var ringed = BuildingFences(fencing, leaves, buildings, seed);
            for (int room = 0; room < leaves.Polygons.Count; room++)
            {
                var rng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Props, room), GenStage.Props);
                Fences(fencing, leaves.Polygons[room], room, ringed.Contains(room), ref rng);
            }
            var sinkholes = SinkholeFences(fencing, leaves, terrains, seed);
            CarRows(props, taken, keepOut, sinkholes, leaves, buildings, ground, s, seed);

            Band(props, taken, keepOut, leaves, ground, s, seed);
            StageProfiler.Begin("Interior");
            InteriorStage.Build(props, buildings, s, seed);
            StageProfiler.End();
            return props;
        }

        /// <summary>Cars and trees in a band 1–3 units inside every room's edges (see the class summary).</summary>
        private static void Band(List<PropPlan> props, List<List<Vector2>> taken, List<List<Vector2>> keepOut,
            PlotPartition leaves, GroundSurface ground, in LevelGenSettings s, ulong seed)
        {
            var p = s.Props;
            float total = p.CarWeight + p.TreeWeight;
            if (p.Density <= 0f || total <= 0f) return;
            for (int leaf = 0; leaf < leaves.Polygons.Count; leaf++)
            {
                var rng = SeedTree.Stream(seed, GenStage.Props, leaf + 1);
                var polygon = leaves.Polygons[leaf];
                float perimeter = 0f;
                for (int k = 0; k < polygon.Count; k++) perimeter += (polygon[(k + 1) % polygon.Count] - polygon[k]).magnitude;
                float expected = perimeter * p.Density / 100f;
                int count = Mathf.FloorToInt(expected) + (rng.Chance(expected - Mathf.Floor(expected)) ? 1 : 0);
                for (int i = 0; i < count; i++)
                    for (int tries = 0; tries < 12; tries++)
                    {
                        float pick = rng.NextFloat() * perimeter;
                        float d = rng.Range(1f, 3f);
                        float kindPick = rng.NextFloat() * total;
                        float variantPick = rng.NextFloat();
                        int side = 0;
                        for (; side < polygon.Count - 1; side++)
                        {
                            float len = (polygon[(side + 1) % polygon.Count] - polygon[side]).magnitude;
                            if (pick <= len) break;
                            pick -= len;
                        }
                        var a = polygon[side];
                        var dir = (polygon[(side + 1) % polygon.Count] - a).normalized;
                        var inward = new Vector2(-dir.y, dir.x);
                        var kind = kindPick < p.CarWeight ? PropKind.Car : PropKind.Tree;
                        var variants = p.Variants(kind);
                        int variant = PropShape.Pick(variants, variantPick);
                        var extent = variants[variant].Extent(kind);
                        var centre = a + dir * pick + inward * (d + extent.y * 0.5f);
                        if (TryAdd(props, taken, keepOut, leaves, ground, s, leaf, kind, variant, variants[variant], centre, dir)) break;
                    }
            }
        }

        /// <summary>What every fence is placed against: the props so far and the zones fences keep out of.</summary>
        private sealed class Fencing
        {
            public List<PropPlan> Props;
            public List<List<Vector2>> Taken;
            /// <summary>The keep-out zones with gates (see <see cref="KeepOut"/>).</summary>
            public List<List<Vector2>> KeepOut;
            public PlotPartition Leaves;
            public GroundSurface Ground;
            public LevelGenSettings Settings;
            /// <summary>Runs and corners are found before a run's variant is drawn, so they keep clear for the thickest.</summary>
            public float Thickest;
        }

        private static Pcg32 Stream(ulong seed, int family, int index) =>
            SeedTree.Stream(SeedTree.Derive(SeedTree.Derive(seed, GenStage.Props, family), GenStage.Props, index), GenStage.Props);

        /// <summary>
        /// With <c>buildingFenceOdds</c>, a building on an inscribed footprint (not a full-plot one: its porch fills the
        /// inset) gets a fence ring <c>buildingFenceMargin</c> outside its porch, cut back to <c>fenceMargin</c> inside its
        /// plot. The ring has a gate <c>spineWidth + 2</c> wide where each entrance's outward line crosses it, and a gap
        /// beside the foot of the exterior flight. Returns the plots that got a ring.
        /// </summary>
        private static HashSet<int> BuildingFences(Fencing f, PlotPartition leaves, List<BuildingPlan> buildings, ulong seed)
        {
            var ringed = new HashSet<int>();
            if (buildings == null) return ringed;
            var p = f.Settings.Props;
            for (int i = 0; i < buildings.Count; i++)
            {
                var building = buildings[i];
                if (building.Dropped != null || building.FullPlot) continue;
                var rng = Stream(seed, BuildingFenceStreams, i);
                bool fenced = rng.Chance(p.BuildingFenceOdds);
                bool old = rng.Chance(p.OldFenceOdds);
                if (!fenced) continue;
                var inner = Polygon.Inset(leaves.Polygons[building.Plot], p.FenceMargin);
                if (inner.Count < 3) continue;
                var ring = Polygon.ClipConvex(Polygon.Outset(Porch(building), p.BuildingFenceMargin), inner);
                if (ring.Count < 3) continue;
                var sides = Sides(ring);
                var gaps = new List<Vector2>();
                float gate = f.Settings.Buildings.SpineWidth + 2f;
                for (int e = 0; e < building.Entrances.Count; e++)
                    if (ClipLine(building.Entrances[e], building.Outward[e], ring, out _, out float exit))
                        gaps.Add(new Vector2(PerimeterAt(sides, building.Entrances[e] + building.Outward[e] * exit), gate));
                if (building.Flight != null)
                {
                    // One unit out from its low end, where a player steps on.
                    var foot = building.Flight.Low - building.Flight.Uphill;
                    gaps.Add(new Vector2(PerimeterAt(sides, foot), building.Flight.Width + 2f));
                }
                Ring(f, sides, null, gaps, null, old, building.Plot, ref rng);
                ringed.Add(building.Plot);
            }
            return ringed;
        }

        /// <summary>
        /// A room's own fences, <c>fenceMargin</c> in from its edges, none in a room with a building fence. With
        /// <c>enclosedRoomOdds</c> the room is fenced on all its sides; otherwise, with <c>fencedRoomOdds</c>,
        /// <c>min..maxFencedSides</c> of its sides (collinear pieces joined) are. Runs are cut wherever they would enter a
        /// fence keep-out zone, which gives a gap at every way into a neighbouring room (a gate at each flat or ledge
        /// crossing, and the whole of every ramp and bank, on either side) and at building entrances. With
        /// <c>oldFenceOdds</c> the room's fences are old.
        /// </summary>
        private static void Fences(Fencing f, List<Vector2> room, int index, bool ringed, ref Pcg32 rng)
        {
            var p = f.Settings.Props;
            var inner = Polygon.Inset(room, p.FenceMargin);
            if (inner.Count < 3) return;
            var sides = Sides(inner);
            bool fenced = rng.Chance(p.FencedRoomOdds);
            bool old = rng.Chance(p.OldFenceOdds);
            int wanted = Mathf.Min(sides.Count, rng.Range(p.MinFencedSides, p.MaxFencedSides + 1));
            var order = new List<int>();
            for (int k = 0; k < sides.Count; k++) order.Add(k);
            for (int k = order.Count - 1; k > 0; k--)
            {
                int j = rng.Range(0, k + 1);
                (order[k], order[j]) = (order[j], order[k]);
            }
            bool enclosed = rng.Chance(p.EnclosedRoomOdds);
            if (ringed || !(fenced || enclosed)) return;

            var mask = new bool[sides.Count];
            for (int n = 0; n < (enclosed ? sides.Count : wanted); n++) mask[order[n]] = true;
            Ring(f, sides, mask, null, null, old, index, ref rng);
        }

        /// <summary>
        /// With <c>sinkholeFenceOdds</c>, a sinkhole gets a fence ring <c>sinkholeFenceMargin</c> outside its rim, with
        /// <c>min..maxSinkholeGaps</c> gaps <c>doorWidth</c> wide centred at evenly spaced points along the ring from a
        /// random start. The ring is cut where it would leave its room. Returns every sinkhole's polygon.
        /// </summary>
        private static List<List<Vector2>> SinkholeFences(Fencing f, PlotPartition leaves, List<TerrainPlan> terrains, ulong seed)
        {
            var sinkholes = new List<List<Vector2>>();
            if (terrains == null) return sinkholes;
            var p = f.Settings.Props;
            foreach (var terrain in terrains)
            {
                var rng = Stream(seed, SinkholeFenceStreams, terrain.Plot);
                foreach (var cell in terrain.Cells)
                {
                    if (!cell.Sinkhole) continue;
                    var rim = new List<Vector2>(cell.Polygon);
                    if (Polygon.SignedArea(rim) < 0f) rim.Reverse();
                    sinkholes.Add(rim);
                    bool fenced = rng.Chance(p.SinkholeFenceOdds);
                    bool old = rng.Chance(p.OldFenceOdds);
                    int count = rng.Range(p.MinSinkholeGaps, p.MaxSinkholeGaps + 1);
                    float start = rng.NextFloat();
                    if (!fenced) continue;
                    var sides = Sides(Polygon.Outset(rim, p.SinkholeFenceMargin));
                    if (sides.Count < 3) continue;
                    float perimeter = 0f;
                    foreach (var (a, b) in sides) perimeter += (b - a).magnitude;
                    var gaps = new List<Vector2>();
                    for (int k = 0; k < count; k++)
                        gaps.Add(new Vector2((start + k / (float)count) * perimeter, f.Settings.Buildings.DoorWidth));
                    Ring(f, sides, null, gaps, leaves.Polygons[terrain.Plot], old, terrain.Plot, ref rng);
                }
            }
            return sinkholes;
        }

        /// <summary>
        /// Fences along the <paramref name="sides"/> of a convex counter-clockwise ring (those in
        /// <paramref name="fenced"/>, or all), the pieces centred on it. Where two fenced sides meet they are butt-joined:
        /// the first runs on to the second's outer face, and the second starts at the first's inner face. At a shallow
        /// corner (under 45°) both stop at its bisector instead. A side is cut wherever a piece would enter a keep-out
        /// zone or leave <paramref name="within"/>, and at each of <paramref name="gaps"/> (its centre's distance along the
        /// ring from the first side's start, and its width). An old fence then gets a few small gaps in each run. Each run
        /// draws a variant and takes a whole number of its pieces, each stretched by up to <see cref="MaxFenceStretch"/> to
        /// fit (a run too short for one piece is left open); each stands on the ground under it.
        /// </summary>
        private static void Ring(Fencing f, List<(Vector2 a, Vector2 b)> sides, bool[] fenced, List<Vector2> gaps,
            List<Vector2> within, bool old, int room, ref Pcg32 rng)
        {
            var p = f.Settings.Props;
            int n = sides.Count;
            float half = f.Thickest * 0.5f;
            const float step = 0.25f;
            float perimeter = 0f;
            foreach (var (a, b) in sides) perimeter += (b - a).magnitude;
            bool Fenced(int k) => fenced == null || fenced[(k + n) % n];
            Vector2 Dir(int k) => (sides[(k + n) % n].b - sides[(k + n) % n].a).normalized;

            float before = 0f;
            for (int k = 0; k < n; before += (sides[k].b - sides[k].a).magnitude, k++)
            {
                if (!Fenced(k)) continue;
                var (origin, end) = sides[k];
                float length = (end - origin).magnitude;
                var dir = Dir(k);
                var inward = new Vector2(-dir.y, dir.x);
                float t0 = 0f, t1 = length;
                if (Fenced(k - 1))
                {
                    Join(Dir(k - 1), dir, half, out _, out float start);
                    t0 = start;
                }
                if (Fenced(k + 1))
                {
                    Join(dir, Dir(k + 1), half, out float over, out _);
                    t1 = length + over;
                }
                if (t1 - t0 < 1f) continue;

                // Free stretches of the line: where a fence piece there keeps clear of every keep-out zone.
                var runs = new List<Vector2>();
                float open = float.NaN;
                int steps = Mathf.FloorToInt((t1 - t0) / step);
                for (int i = 0; i <= steps + 1; i++)
                {
                    // The last step closes any run still open at the line's end.
                    float t = Mathf.Min(t0 + i * step, t1);
                    var at = origin + dir * t;
                    var probe = Box(at, dir, inward, step + 2f * Clearance, f.Thickest + 2f * Clearance);
                    bool free = i <= steps;
                    foreach (var zone in f.KeepOut)
                        if (free && Polygon.Area(Polygon.ClipConvex(probe, zone)) > 1e-3f) free = false;
                    if (free && within != null && Polygon.Area(Polygon.ClipConvex(probe, within)) < Polygon.Area(probe) - 1e-3f)
                        free = false;
                    if (free && float.IsNaN(open)) open = t;
                    if (!free && !float.IsNaN(open))
                    {
                        if (t - open >= 1f) runs.Add(new Vector2(open, t));
                        open = float.NaN;
                    }
                }

                // The ring's own gaps, in this side's terms (one may wrap round the ring's start).
                if (gaps != null)
                    foreach (var gap in gaps)
                        for (int wrap = -1; wrap <= 1; wrap++)
                        {
                            float centre = gap.x + wrap * perimeter - before;
                            Cut(runs, centre - gap.y * 0.5f, centre + gap.y * 0.5f);
                        }

                // An old fence: a few small gaps in each run long enough to keep both halves.
                if (old)
                {
                    int count = rng.Range(p.MinOldGaps, p.MaxOldGaps + 1);
                    for (int g = 0; g < count; g++)
                    {
                        float width = rng.Range(p.MinOldGapWidth, p.MaxOldGapWidth);
                        float where = rng.NextFloat();
                        int which = runs.FindIndex(r => r.y - r.x >= width + 2f);
                        if (which < 0) break;
                        var r = runs[which];
                        float centre = Mathf.Lerp(r.x + 1f + width * 0.5f, r.y - 1f - width * 0.5f, where);
                        runs[which] = new Vector2(r.x, centre - width * 0.5f);
                        runs.Insert(which + 1, new Vector2(centre + width * 0.5f, r.y));
                    }
                }

                var variants = p.Fences;
                foreach (var run in runs)
                {
                    int variant = PropShape.Pick(variants, rng.NextFloat());
                    var shape = variants[variant];
                    float span = run.y - run.x;
                    if (span < shape.Length * (1f - MaxFenceStretch)) continue;
                    int pieces = Mathf.Max(1, Mathf.RoundToInt(span / shape.Length));
                    float stretch = Mathf.Clamp(span / (pieces * shape.Length), 1f - MaxFenceStretch, 1f + MaxFenceStretch);
                    float pieceLength = shape.Length * stretch;
                    // Centred in the run, when the clamp leaves it a little short.
                    float first = (run.x + run.y) * 0.5f - pieces * pieceLength * 0.5f;
                    for (int i = 0; i < pieces; i++)
                    {
                        var centre = origin + dir * (first + (i + 0.5f) * pieceLength);
                        AddFence(f, centre, dir, inward, variant, shape, stretch, room);
                    }
                }
            }
        }

        /// <summary>
        /// How two fences of half-thickness <paramref name="half"/> along a convex corner (from direction
        /// <paramref name="into"/> to <paramref name="outOf"/>) meet without overlapping: how far past the corner the
        /// first runs (<paramref name="over"/>, negative to stop short of it), and how far past it the second starts.
        /// </summary>
        private static void Join(Vector2 into, Vector2 outOf, float half, out float over, out float start)
        {
            // tan of half the turn.
            float tan = Mathf.Max(0f, (into.x * outOf.y - into.y * outOf.x) / (1f + Vector2.Dot(into, outOf)));
            if (tan >= 0.4142f)
            {
                // A butt joint: the first's outer corner reaches the second's outer face, and the second starts
                // where its outer corner leaves the first's inner face.
                over = half * tan;
                start = half / tan;
            }
            else
            {
                // A shallow corner, where a butt joint would leave a long gap: both stop at the bisector.
                over = -half * tan;
                start = half * tan;
            }
        }

        /// <summary>Takes <c>from..to</c> out of the runs.</summary>
        private static void Cut(List<Vector2> runs, float from, float to)
        {
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                var r = runs[i];
                if (to <= r.x || from >= r.y) continue;
                runs.RemoveAt(i);
                if (to < r.y) runs.Insert(i, new Vector2(to, r.y));
                if (from > r.x) runs.Insert(i, new Vector2(r.x, from));
            }
        }

        /// <summary>The distance along the ring's sides, from the first side's start, to the point on them nearest
        /// <paramref name="point"/>.</summary>
        private static float PerimeterAt(List<(Vector2 a, Vector2 b)> sides, Vector2 point)
        {
            float best = float.PositiveInfinity, at = 0f, before = 0f;
            foreach (var (a, b) in sides)
            {
                float length = (b - a).magnitude;
                var dir = (b - a) / length;
                float t = Mathf.Clamp(Vector2.Dot(point - a, dir), 0f, length);
                float distance = (a + dir * t - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    at = before + t;
                }
                before += length;
            }
            return at;
        }

        /// <summary>A fence piece standing on the ground under it, which must be level (it is left out otherwise), on its
        /// room, and clear of the pieces before it: where two fences cross, the later one gives way.</summary>
        private static void AddFence(Fencing f, Vector2 centre, Vector2 along, Vector2 across, int variant, PropShape shape,
            float stretch, int room)
        {
            float length = shape.Length * stretch;
            var footprint = Box(centre, along, across, length, shape.Width);
            foreach (var v in footprint)
                if (!Polygon.Contains(f.Leaves.Polygons[room], v)) return;
            if (!f.Ground.Level(room, footprint, out float bottom)) return;
            if (bottom + shape.Height > f.Settings.MaxHeight) return;
            foreach (var other in f.Taken)
                if (Polygon.ClipConvex(footprint, other, true).Count > 0) return;
            f.Props.Add(new PropPlan
            {
                Kind = PropKind.Fence, Variant = variant, Shape = shape, Stretch = stretch, Leaf = room, Centre = centre,
                Along = along, Length = length, Width = shape.Width, Bottom = bottom, Height = shape.Height,
                Footprint = footprint,
            });
            f.Taken.Add(footprint);
        }

        /// <summary>
        /// With <c>boundaryCarOdds</c>, a boundary wall's run (one outline edge) gets a row of parked cars parallel to it,
        /// <c>carWallGap</c> off its inner face and <c>min..maxCarSpacing</c> apart, from <see cref="CarRowEndGap"/> in from
        /// one end to as far from the other. Each slot draws its variant and is left empty with <c>carSkipOdds</c>. A car
        /// goes in a slot only if it lies in its plot, the ground under its whole footprint is level (one plane, so it
        /// stacks with no gap under it: a car over a slope or a seam would leave a gap a floor can't keep its headroom
        /// in), and it keeps <see cref="Clearance"/> from keep-out zones, sinkholes and other props (fences and their rings
        /// included) and <c>spineWidth</c> from every building and porch. Otherwise the row stops there and starts again
        /// as soon as the slot's car fits.
        /// </summary>
        private static void CarRows(List<PropPlan> props, List<List<Vector2>> taken, List<List<Vector2>> keepOut,
            List<List<Vector2>> sinkholes, PlotPartition leaves, List<BuildingPlan> buildings, GroundSurface ground,
            in LevelGenSettings s, ulong seed)
        {
            var p = s.Props;
            var clear = new List<List<Vector2>>();
            if (buildings != null)
                foreach (var building in buildings)
                    if (building.Dropped == null) clear.Add(Polygon.Outset(Porch(building), s.Buildings.SpineWidth));
            const float slide = 0.5f;
            var variants = p.Cars;
            for (int i = 0; i < leaves.Outline.Count; i++)
            {
                var edge = leaves.Outline[i];
                var rng = Stream(seed, CarRowStreams, i);
                if (!rng.Chance(p.BoundaryCarOdds)) continue;
                float length = (edge.To - edge.From).magnitude;
                if (length < Polygon.Epsilon) continue;
                var along = (edge.To - edge.From) / length;
                // The plot is on the left of a counter-clockwise outline edge.
                var inward = new Vector2(-along.y, along.x);
                var polygon = leaves.Polygons[edge.A];

                float t = CarRowEndGap;
                int variant = PropShape.Pick(variants, rng.NextFloat());
                bool skip = rng.Chance(p.CarSkipOdds);
                while (true)
                {
                    var shape = variants[variant];
                    var extent = shape.Extent(PropKind.Car);
                    if (t + extent.x > length - CarRowEndGap) break;
                    if (!skip)
                    {
                        var centre = edge.From + along * (t + extent.x * 0.5f) + inward * (p.CarWallGap + extent.y * 0.5f);
                        if (!Park(props, taken, keepOut, sinkholes, clear, polygon, ground, s, edge.A, variant, shape,
                                centre, along))
                        {
                            t += slide;
                            continue;
                        }
                    }
                    t += extent.x + rng.Range(p.MinCarSpacing, p.MaxCarSpacing);
                    variant = PropShape.Pick(variants, rng.NextFloat());
                    skip = rng.Chance(p.CarSkipOdds);
                }
            }
        }

        /// <summary>A parked car along a boundary wall, if its slot is clear (see <see cref="CarRows"/>).</summary>
        private static bool Park(List<PropPlan> props, List<List<Vector2>> taken, List<List<Vector2>> keepOut,
            List<List<Vector2>> sinkholes, List<List<Vector2>> clear, List<Vector2> polygon, GroundSurface ground,
            in LevelGenSettings s, int plot, int variant, PropShape shape, Vector2 centre, Vector2 along)
        {
            var extent = shape.Extent(PropKind.Car);
            var across = new Vector2(-along.y, along.x);
            var footprint = Box(centre, along, across, extent.x, extent.y);
            var padded = Box(centre, along, across, extent.x + 2f * Clearance, extent.y + 2f * Clearance);
            foreach (var v in footprint)
                if (!Polygon.Contains(polygon, v)) return false;
            foreach (var zone in keepOut)
                if (Polygon.Area(Polygon.ClipConvex(padded, zone)) > 1e-3f) return false;
            foreach (var zone in sinkholes)
                if (Polygon.Area(Polygon.ClipConvex(padded, zone)) > 1e-3f) return false;
            foreach (var zone in clear)
                if (Polygon.Area(Polygon.ClipConvex(footprint, zone)) > 1e-3f) return false;
            foreach (var other in taken)
                if (Polygon.Area(Polygon.ClipConvex(padded, other)) > 1e-3f) return false;
            if (!ground.Level(plot, footprint, out float bottom)) return false;
            if (bottom + shape.Top(PropKind.Car) > s.MaxHeight) return false;

            props.Add(new PropPlan
            {
                Kind = PropKind.Car, Variant = variant, Shape = shape, Leaf = plot, Centre = centre, Along = along,
                Length = extent.x, Width = extent.y, Bottom = bottom, Height = shape.Top(PropKind.Car), Footprint = footprint,
            });
            taken.Add(footprint);
            return true;
        }

        /// <summary>
        /// A building with its porch, as one convex outline: the hull of every floor's footprint, its ground-level
        /// porch slabs (<see cref="BuildingPlan.Porches"/>), its slab balconies and landings (a stilted building's porch
        /// is a first-floor balcony, and upper ones overhang the ground) and its exterior flight. It is what fence rings,
        /// keep-out zones and the cars' clearance are drawn round.
        /// </summary>
        public static List<Vector2> Porch(BuildingPlan building)
        {
            var points = new List<Vector2>(building.Footprint);
            foreach (var floor in building.FloorPlans) points.AddRange(floor.Footprint);
            foreach (var porch in building.Porches) points.AddRange(porch);
            foreach (var balcony in building.Balconies)
                if (!balcony.Terrace) points.AddRange(balcony.Polygon);
            if (building.Flight != null) points.AddRange(BuildingStage.FlightRect(building));
            return Polygon.Hull(points);
        }

        /// <summary>The polygon's sides, with collinear pieces (where other polygons' corners were inserted) joined.</summary>
        private static List<(Vector2 a, Vector2 b)> Sides(List<Vector2> polygon)
        {
            var corners = new List<Vector2>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var previous = polygon[(k + polygon.Count - 1) % polygon.Count];
                var next = polygon[(k + 1) % polygon.Count];
                var d0 = (polygon[k] - previous).normalized;
                var d1 = (next - polygon[k]).normalized;
                if (Mathf.Abs(d0.x * d1.y - d0.y * d1.x) > 1e-3f || Vector2.Dot(d0, d1) < 0f) corners.Add(polygon[k]);
            }
            var sides = new List<(Vector2, Vector2)>();
            for (int k = 0; k < corners.Count; k++) sides.Add((corners[k], corners[(k + 1) % corners.Count]));
            return sides;
        }

        /// <summary>The parameter range of the line <c>p + dir·t</c> inside a convex counter-clockwise polygon.</summary>
        private static bool ClipLine(Vector2 p, Vector2 dir, List<Vector2> polygon, out float t0, out float t1)
        {
            t0 = float.NegativeInfinity;
            t1 = float.PositiveInfinity;
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var e = polygon[(k + 1) % polygon.Count];
                var outward = new Vector2(e.y - a.y, a.x - e.x);
                float num = Vector2.Dot(outward, a - p), den = Vector2.Dot(outward, dir);
                // Parallel within about 1e-4 radians: a line may lie along one of the polygon's edges, and float noise
                // must not turn that edge into a crossing at an arbitrary point.
                if (Mathf.Abs(den) < 1e-4f * outward.magnitude)
                {
                    if (num < -1e-3f * outward.magnitude) return false;
                    continue;
                }
                float t = num / den;
                if (den > 0f) t1 = Mathf.Min(t1, t);
                else t0 = Mathf.Max(t0, t);
            }
            return t1 > t0;
        }

        private static bool TryAdd(List<PropPlan> props, List<List<Vector2>> taken, List<List<Vector2>> keepOut,
            PlotPartition leaves, GroundSurface ground, in LevelGenSettings s, int leaf, PropKind kind,
            int variant, PropShape shape, Vector2 centre, Vector2 along)
        {
            var extent = shape.Extent(kind);
            float length = extent.x, width = extent.y;
            var across = new Vector2(-along.y, along.x);
            var footprint = Box(centre, along, across, length, width);
            var padded = Box(centre, along, across, length + 2f * Clearance, width + 2f * Clearance);
            var room = Polygon.Inset(leaves.Polygons[leaf], 0.25f);
            foreach (var v in footprint)
                if (!Polygon.Contains(room, v)) return false;
            foreach (var zone in keepOut)
                if (Polygon.Area(Polygon.ClipConvex(padded, zone)) > 1e-3f) return false;
            foreach (var other in taken)
                if (Polygon.Area(Polygon.ClipConvex(padded, other)) > 1e-3f) return false;

            // Stand on level ground: a car's whole footprint, a tree's trunk. A tree's canopy must keep a player's
            // height over the ground under it (a 5 × 5 grid over it: uneven ground can peak between its corners).
            var stand = kind == PropKind.Tree ? Box(centre, along, across, shape.Length, shape.Width) : footprint;
            if (!ground.Level(leaf, stand, out float bottom)) return false;
            if (kind == PropKind.Tree)
            {
                float high = float.NegativeInfinity;
                for (int i = 0; i <= 4; i++)
                    for (int j = 0; j <= 4; j++)
                        high = Mathf.Max(high, ground.At(leaf, centre + along * (length * (i / 4f - 0.5f)) + across * (width * (j / 4f - 0.5f))));
                if (high - bottom > shape.CanopyBottom - s.Player.PlayerHeight - 0.1f) return false;
            }
            if (bottom + shape.Top(kind) > s.MaxHeight) return false;

            props.Add(new PropPlan
            {
                Kind = kind, Variant = variant, Shape = shape, Leaf = leaf, Centre = centre, Along = along, Length = length,
                Width = width, Bottom = bottom, Height = shape.Top(kind), Footprint = footprint,
            });
            taken.Add(footprint);
            return true;
        }

        private static List<Vector2> Box(Vector2 centre, Vector2 along, Vector2 across, float length, float width)
        {
            var l = along * (length * 0.5f);
            var w = across * (width * 0.5f);
            var box = new List<Vector2> { centre - l - w, centre + l - w, centre + l + w, centre - l + w };
            if (Polygon.SignedArea(box) < 0f) box.Reverse();
            return box;
        }

        /// <summary>
        /// The zones props stay out of, as convex polygons. With <paramref name="gates"/> (for fences), a flat or ledge
        /// crossing keeps only a gate <c>doorWidth + 2</c> wide clear at its middle, rather than its whole length.
        /// </summary>
        public static List<List<Vector2>> KeepOut(PlotPartition leaves, PlotPlan plan, List<BuildingPlan> buildings,
            in LevelGenSettings s, bool gates = false)
        {
            var zones = new List<List<Vector2>>();
            const float strip = 2f;
            foreach (var segment in plan.Segments)
            {
                if (TransitionStage.IsSlope(segment.Type))
                {
                    var slope = TransitionStage.SlopeOutline(leaves, segment, strip, s.TransitionSizes.RampWidth,
                        TransitionStage.FlareReach(segment, s));
                    if (slope.Count >= 3) zones.Add(slope);
                    // Where it arrives on the higher plot, so nothing (a fence least of all) blocks its top.
                    var along = (segment.To - segment.From).normalized;
                    var up = TransitionStage.Into(leaves.Polygons[segment.Higher], segment.From, segment.To);
                    float span = segment.Type == TransitionType.Ramp ? s.TransitionSizes.RampWidth + 2f : segment.Length;
                    zones.Add(Box((segment.From + segment.To) * 0.5f + up * (strip + 1f) * 0.5f, along, up, span, strip + 1f));
                    continue;
                }
                if (segment.Type is not (TransitionType.Flat or TransitionType.Ledge)) continue;
                var dir = (segment.To - segment.From).normalized;
                var normal = new Vector2(-dir.y, dir.x);
                var mid = (segment.From + segment.To) * 0.5f;
                float clear = gates ? Mathf.Min(segment.Length, s.Buildings.DoorWidth + 2f) : segment.Length;
                zones.Add(Box(mid, dir, normal, clear, 2f * strip));
            }

            if (buildings == null) return zones;
            foreach (var building in buildings)
            {
                if (building.Dropped != null) continue;
                zones.Add(Porch(building));
                for (int i = 0; i < building.Entrances.Count; i++)
                {
                    var outward = building.Outward[i];
                    var across = new Vector2(-outward.y, outward.x);
                    float length = 4f + s.Buildings.BalconyDepth;
                    zones.Add(Box(building.Entrances[i] + outward * (length * 0.5f), outward, across, length, s.Buildings.SpineWidth + 1f));
                }
                if (building.Flight != null)
                {
                    zones.Add(Polygon.Outset(BuildingStage.FlightRect(building), 1f));
                    zones.Add(Polygon.Outset(building.Flight.Landing, 1f));
                }
            }
            return zones;
        }

        /// <summary>Each prop's record, and its placeholder boxes: what the span grid and the validator see, and what a
        /// variant without a prefab is drawn as. A light fixture has no boxes (it has no collider, and the builder draws
        /// its placeholder itself), only its light.</summary>
        public static void Emit(LevelDescription level, List<PropPlan> props)
        {
            for (int id = 0; id < props.Count; id++)
            {
                var prop = props[id];
                var shape = prop.Shape;
                float yaw = Mathf.Atan2(prop.Along.x, prop.Along.y) * Mathf.Rad2Deg;
                level.Props.Add(new PropDescription
                {
                    Id = id, Kind = prop.Kind, Variant = prop.Variant, Stretch = prop.Stretch, YawDegrees = yaw,
                    Position = new Vector3(prop.Centre.x, prop.Bottom, prop.Centre.y),
                });
                void Add(PrimitiveRole role, float bottom, float height, float length, float width) => level.Primitives.Add(new Primitive
                {
                    Role = role,
                    Shape = PrimitiveShape.Box,
                    Position = new Vector3(prop.Centre.x, bottom, prop.Centre.y),
                    // Local +Z runs along the prop's length.
                    Size = new Vector3(width, height, length),
                    YawDegrees = yaw,
                    OwnerKind = OwnerKind.Prop,
                    OwnerId = id,
                    Surfaces = Surfaces.All(SurfaceTag.PropPlaceholder),
                });
                if (PropKinds.IsFixture(prop.Kind))
                {
                    if (prop.Light is { } light)
                    {
                        light.Prop = id;
                        level.Lights.Add(light);
                    }
                    continue;
                }
                switch (prop.Kind)
                {
                    // Each stands on the level ground under it (see GroundSurface.Level), bottom to its top.
                    case PropKind.Fence:
                        Add(PrimitiveRole.Cover, prop.Bottom, prop.Height, prop.Length, shape.Width);
                        break;
                    case PropKind.Car:
                        Add(PrimitiveRole.Cover, prop.Bottom, shape.Height, shape.Length, shape.Width);
                        break;
                    // Tables, shelves and crates stand on their room's slab, its top at their bottom.
                    case PropKind.Table or PropKind.Shelf or PropKind.Crate:
                        Add(PrimitiveRole.Cover, prop.Bottom, shape.Height, shape.Length, shape.Width);
                        break;
                    default:
                        Add(PrimitiveRole.Pillar, prop.Bottom, shape.Height, shape.Length, shape.Width);
                        Add(PrimitiveRole.Ceiling, prop.Bottom + shape.CanopyBottom, shape.CanopyHeight, shape.CanopyLength,
                            shape.CanopyWidth);
                        break;
                }
            }
        }
    }
}
