using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Stage 4: on each plot stage 3 kept flat for a building of the drawn mix (big or small), a convex footprint, floors
    /// and how the first one sits, entrances facing the plot's transitions, the spine (a lobby and a strip to each
    /// entrance), and the way up: a switchback stair core, or else an exterior flight to the second floor (or a small
    /// building's roof). Then each floor's size (the one below scaled about the core), terraces where a floor shrinks,
    /// floor plans (rooms between edges drawn as walls, railings or nothing), balconies and porches, a reachable roof,
    /// doors, windows, and each room's level with short flights between rooms at different levels. Each building is
    /// checked on its own plot; a failed check retries it from its next sub-seed, and after the budget the building
    /// fails, and its level attempt with it. Geometry is in BuildingGeometry.cs.
    /// </summary>
    public static partial class BuildingStage
    {
        public const float WallThickness = 0.3f;
        /// <summary>A ground-level first floor sits this far above the plot, so its surface is the building's own.</summary>
        public const float GroundFloorRise = 0.1f;
        /// <summary>Of the buildings lifted off the ground (<c>raisedGroundOdds</c>), the share on stilts.</summary>
        public const float StiltsShare = 0.5f;
        public const float PillarSide = 0.6f;
        /// <summary>The landing at the wall end of a core, and the clear floor kept at its open end.</summary>
        public const float CoreLanding = 1.5f;
        public const float CoreApproach = 2f;
        /// <summary>
        /// Floating flights and landings sit this much above the player's height over what is under them, so float
        /// rounding never leaves 1.9999 of headroom.
        /// </summary>
        public const float HeadroomSlack = 0.01f;
        /// <summary>
        /// Under stilts the outer walls hang a floor up, so a core against one would put its landing under the wall:
        /// there it stands this far in from the wall's inner face.
        /// </summary>
        public const float StiltsCoreGap = 0.5f;
        /// <summary>Overhanging floors, balconies and exterior flights keep this far inside the plot.</summary>
        public const float OverhangMargin = 0.5f;
        public const float InteriorWallThickness = 0.2f;
        /// <summary>Openings keep this far from each other, from interior walls, and from the core.</summary>
        public const float OpeningGap = 0.5f;
        /// <summary>A big building has at least this many floors (it fails when fewer fit).</summary>
        public const int MinBigFloors = 2;
        /// <summary>Balconies keep this far inside the plot (and clear of its slopes): they hang a floor up, over nothing
        /// but the porch.</summary>
        public const float BalconyEdgeMargin = 0.1f;
        /// <summary>A plot whose slope clear-outs leave no footprint of the class is no use retrying.</summary>
        private const string NoFootprint = "no footprint";

        /// <summary>
        /// One plan per building plot, for the class stage 3 chose. A plan that can't be made, or fails its local check,
        /// is made again from the building's next sub-seed; after the budget it keeps <see cref="BuildingPlan.Dropped"/>,
        /// and the level attempt fails (a building is never left out: hard constraint 8).
        /// </summary>
        public static List<BuildingPlan> Build(PlotPartition partition, PlotPlan plots, in LevelGenSettings s, ulong seed)
        {
            var result = new List<BuildingPlan>();
            int budget = Mathf.Max(1, s.SearchBudget.RetriesPerPlot) + 1;
            for (int i = 0; i < plots.BuildingPlots.Count; i++)
            {
                int plot = plots.BuildingPlots[i];
                var buildingClass = plots.BuildingClasses[i];
                ulong buildingSeed = SeedTree.Derive(seed, GenStage.Buildings, i + 1);
                BuildingPlan plan = null;
                var failures = new List<string>();
                for (int attempt = 0; attempt < budget; attempt++)
                {
                    var rng = attempt == 0
                        ? SeedTree.Stream(buildingSeed, GenStage.Buildings)
                        : SeedTree.Stream(SeedTree.Derive(buildingSeed, GenStage.BuildingRetry, attempt), GenStage.BuildingRetry);
                    plan = Plan(plot, buildingClass, partition, plots, s, ref rng);
                    plan.Attempts = attempt + 1;
                    string failure = plan.Dropped ?? LocalCheck(plan, partition.Polygons[plot], s);
                    if (failure == null) break;
                    failures.Add($"attempt {attempt + 1}: {failure}");
                    if (failure.StartsWith(NoFootprint)) break;
                    plan.Dropped = attempt == budget - 1 ? $"failed {budget} times (last: {failure})" : failure;
                }
                plan.Notes.AddRange(failures);
                result.Add(plan);
            }
            return result;
        }

        // ---------------------------------------------------------------- mix

        /// <summary>How many big and small buildings a mix has.</summary>
        public static void MixCounts(BuildingMix mix, out int big, out int small)
        {
            (big, small) = mix switch
            {
                BuildingMix.OneBigTwoSmall => (1, 2),
                BuildingMix.TwoBig => (2, 0),
                BuildingMix.FourSmall => (0, 4),
                _ => (0, 0),
            };
        }

        /// <summary>
        /// Whether a plot is eligible for a class: the class's smallest footprint fits it (the plot filled, less the
        /// porch: at least <see cref="MinWidth"/> across), and its smallest floor count fits under the max height.
        /// </summary>
        public static bool Fits(List<Vector2> plot, BuildingClass buildingClass, in LevelGenSettings s)
        {
            var b = s.Buildings;
            int floors = buildingClass == BuildingClass.Big ? MinBigFloors : 1;
            float height = buildingClass == BuildingClass.Big ? b.BigFloorHeight : b.SmallFloorHeight;
            if (GroundFloorRise + floors * height > s.MaxHeight) return false;
            var full = Polygon.Inset(plot, b.BalconyDepth);
            return full.Count >= 3 && Polygon.MinWidth(full) >= MinWidth(buildingClass, s);
        }

        /// <summary>
        /// A footprint's least width: for a small building a spine with a wall either side and a little room; for a big
        /// one, a stair core along a wall either side of a spine-wide lobby in the middle.
        /// </summary>
        public static float MinWidth(BuildingClass buildingClass, in LevelGenSettings s)
        {
            var b = s.Buildings;
            return buildingClass == BuildingClass.Big
                ? 2f * (2f * b.DoorWidth + WallThickness) + b.SpineWidth
                : b.SpineWidth + 2f * WallThickness + 2f;
        }

        /// <summary>
        /// Draws one mix, with equal weights, among those enough plots are eligible for: big buildings take the largest
        /// eligible plots, then small buildings take theirs at random among the rest of theirs. Fills
        /// <paramref name="plots"/> and <paramref name="classes"/>, and returns the mix, or None when none fits.
        /// </summary>
        public static BuildingMix ChooseMix(List<List<Vector2>> polygons, in LevelGenSettings s, ref Pcg32 rng,
            List<int> plots, List<BuildingClass> classes)
        {
            var big = new List<int>();
            var small = new List<int>();
            for (int i = 0; i < polygons.Count; i++)
            {
                if (Fits(polygons[i], BuildingClass.Big, s)) big.Add(i);
                if (Fits(polygons[i], BuildingClass.Small, s)) small.Add(i);
            }
            return ChooseMix(polygons, big, small, ref rng, plots, classes);
        }

        private static BuildingMix ChooseMix(List<List<Vector2>> polygons, List<int> big, List<int> small, ref Pcg32 rng,
            List<int> plots, List<BuildingClass> classes)
        {
            big.Sort((x, y) =>
            {
                int byArea = Polygon.Area(polygons[y]).CompareTo(Polygon.Area(polygons[x]));
                return byArea != 0 ? byArea : x.CompareTo(y);
            });

            var mixes = new List<BuildingMix>();
            foreach (var mix in new[] { BuildingMix.OneBigTwoSmall, BuildingMix.TwoBig, BuildingMix.FourSmall })
            {
                MixCounts(mix, out int bigCount, out int smallCount);
                if (big.Count < bigCount) continue;
                int left = 0;
                foreach (int plot in small)
                    if (big.IndexOf(plot) < 0 || big.IndexOf(plot) >= bigCount) left++;
                if (left >= smallCount) mixes.Add(mix);
            }
            if (mixes.Count == 0) return BuildingMix.None;

            var chosen = mixes[rng.Range(0, mixes.Count)];
            MixCounts(chosen, out int bigs, out int smalls);
            for (int i = 0; i < bigs; i++)
            {
                plots.Add(big[i]);
                classes.Add(BuildingClass.Big);
            }
            var rest = small.FindAll(p => !plots.Contains(p));
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (rest[i], rest[j]) = (rest[j], rest[i]);
            }
            for (int i = 0; i < smalls; i++)
            {
                plots.Add(rest[i]);
                classes.Add(BuildingClass.Small);
            }
            return chosen;
        }

        /// <summary>
        /// Whether a plot is still eligible for a class once stage 3 has its heights and slopes: flat, eligible
        /// (<see cref="Fits"/>), low enough for a big building's floors, and with the class's smallest footprint left
        /// after the ramps and banks cut into it are cleared.
        /// </summary>
        public static bool FitsAfterSlopes(PlotPartition partition, PlotPlan plots, int plot, BuildingClass buildingClass,
            in LevelGenSettings s)
        {
            var b = s.Buildings;
            var polygon = partition.Polygons[plot];
            if (!plots.Ground[plot].Flat || !Fits(polygon, buildingClass, s)) return false;
            bool big = buildingClass == BuildingClass.Big;
            if (big && plots.Ground[plot].Height + GroundFloorRise + MinBigFloors * b.BigFloorHeight > s.MaxHeight) return false;
            var full = ClearOfSlopes(Polygon.Inset(polygon, b.BalconyDepth), plot, plots, b.BalconyDepth);
            return full.Count >= 3 && Polygon.MinWidth(full) >= MinWidth(buildingClass, s);
        }

        /// <summary>
        /// The end of stage 3: the mix drawn before heights only kept its plots flat. If each of its plots is still
        /// eligible for its class after the slopes' clear-outs, it stands; otherwise one is drawn again among the flat
        /// plots that are (None when no mix fits). Plots it no longer uses get terrain like any other.
        /// </summary>
        public static void ConfirmMix(PlotPartition partition, PlotPlan plan, in LevelGenSettings s, ulong seed)
        {
            if (plan.Failure != null) return;
            bool stands = plan.Mix != BuildingMix.None;
            for (int i = 0; i < plan.BuildingPlots.Count && stands; i++)
                stands = FitsAfterSlopes(partition, plan, plan.BuildingPlots[i], plan.BuildingClasses[i], s);
            if (stands) return;

            var big = new List<int>();
            var small = new List<int>();
            for (int i = 0; i < partition.Polygons.Count; i++)
            {
                if (FitsAfterSlopes(partition, plan, i, BuildingClass.Big, s)) big.Add(i);
                if (FitsAfterSlopes(partition, plan, i, BuildingClass.Small, s)) small.Add(i);
            }
            var rng = SeedTree.Stream(SeedTree.Derive(seed, GenStage.Buildings, 0), GenStage.Buildings);
            plan.BuildingPlots.Clear();
            plan.BuildingClasses.Clear();
            plan.Mix = ChooseMix(partition.Polygons, big, small, ref rng, plan.BuildingPlots, plan.BuildingClasses);
        }

        /// <summary>
        /// Stage 3's base heights, before slopes: a big building's plot that sits too high for two floors under the max
        /// height trades its base with the lowest plot that has no big building, so the mix can be built.
        /// </summary>
        public static void KeepBigPlotsLow(PlotPlan plan, float[] bases, in LevelGenSettings s)
        {
            float highest = s.MaxHeight - GroundFloorRise - MinBigFloors * s.Buildings.BigFloorHeight;
            bool IsBig(int plot)
            {
                int i = plan.BuildingPlots.IndexOf(plot);
                return i >= 0 && plan.BuildingClasses[i] == BuildingClass.Big;
            }
            foreach (int plot in plan.BuildingPlots)
            {
                if (!IsBig(plot) || bases[plot] <= highest + 1e-4f) continue;
                int swap = -1;
                for (int j = 0; j < bases.Length; j++)
                    if (!IsBig(j) && (swap < 0 || bases[j] < bases[swap])) swap = j;
                if (swap >= 0 && bases[swap] < bases[plot]) (bases[plot], bases[swap]) = (bases[swap], bases[plot]);
            }
        }

        // ---------------------------------------------------------------- plan

        private static BuildingPlan Plan(int plot, BuildingClass buildingClass, PlotPartition partition, PlotPlan plots,
            in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            bool big = buildingClass == BuildingClass.Big;
            var plan = new BuildingPlan
            {
                Plot = plot, Class = buildingClass, Ground = plots.Ground[plot].Height,
                FloorHeight = big ? b.BigFloorHeight : b.SmallFloorHeight,
            };
            var polygon = partition.Polygons[plot];

            // Footprint, kept off the ramps and banks cut into this plot.
            plan.FullPlot = rng.Chance(b.FullPlotOdds);
            int alignEdge = rng.Range(0, polygon.Count);
            float jitter = rng.Range(-20f, 20f) * Mathf.Deg2Rad;
            float aspect = rng.Range(1f, 1.6f);
            float scale = rng.Range(0.85f, 1f);
            int chamfers = rng.Range(0, 3);
            float chamferT = rng.Range(0.15f, 0.3f);
            // A big building needs room for its stair core beside the spine; an inscribed shape narrower than that fills
            // the plot instead.
            float floorSide = MinWidth(BuildingClass.Small, s);
            float minSide = MinWidth(buildingClass, s);
            var full = ClearOfSlopes(Polygon.Inset(polygon, b.BalconyDepth), plot, plots, b.BalconyDepth);
            if (!plan.FullPlot)
            {
                // Square to one of the plot's edges, give or take 20°.
                var edge = polygon[(alignEdge + 1) % polygon.Count] - polygon[alignEdge];
                float angle = Mathf.Atan2(edge.y, edge.x) + jitter;
                var inset = ClearOfSlopes(Polygon.Inset(polygon, b.SpineWidth), plot, plots, b.SpineWidth);
                plan.Footprint = Inscribed(inset, angle, aspect, scale, chamfers, chamferT);
                if (plan.Footprint.Count < 3 || Polygon.MinWidth(plan.Footprint) < minSide)
                {
                    plan.Notes.Add("no inscribed footprint fits: it fills the plot instead");
                    plan.FullPlot = true;
                }
            }
            if (plan.FullPlot) plan.Footprint = full;
            plan.Footprint = WithoutShortSides(plan.Footprint);
            if (plan.Footprint.Count < 3 || Polygon.MinWidth(plan.Footprint) < minSide)
            {
                plan.Dropped = $"{NoFootprint} at least {minSide:0.#} wide fits clear of the plot's edges and slopes";
                return plan;
            }

            // First floor and floor count.
            bool lifted = rng.Chance(b.RaisedGroundOdds);
            bool stilts = rng.Chance(StiltsShare);
            float raise = 0.5f + 0.25f * rng.Range(0, 5);
            int floors = big ? rng.Range(b.MinFloors, b.MaxFloors + 1) : 1;
            int fewest = big ? MinBigFloors : 1;
            plan.Placement = !lifted ? FirstFloor.Ground : stilts ? FirstFloor.Stilts : FirstFloor.Raised;
            for (int tries = 0; tries < 2; tries++)
            {
                plan.FirstFloorY = plan.Placement switch
                {
                    FirstFloor.Raised => plan.Ground + raise,
                    FirstFloor.Stilts => plan.Ground + plan.FloorHeight,
                    _ => plan.Ground + GroundFloorRise,
                };
                plan.Floors = floors;
                while (plan.Floors > 0 && plan.FirstFloorY + plan.Floors * plan.FloorHeight > s.MaxHeight) plan.Floors--;
                if (plan.Floors >= fewest) break;
                plan.Placement = FirstFloor.Ground;
            }
            if (plan.Floors < fewest)
            {
                plan.Dropped = big ? $"fewer than {MinBigFloors} floors fit under the max height" : "not even one floor fits under the max height";
                return plan;
            }
            if (plan.Floors < floors) plan.Notes.Add($"{floors} floors cut to {plan.Floors} to fit under the max height");

            // Entrances facing the plot's transitions, then the spine to them.
            if (!PlaceEntrances(plan, plot, plots, s))
            {
                plan.Dropped = "fewer than two entrances fit on the footprint";
                return plan;
            }
            BuildSpine(plan, s);

            // A raised floor's steps must stay on the plot: lower the plinth until they do, or sit on the ground.
            if (plan.Placement == FirstFloor.Raised)
            {
                float rise = Mathf.Min(plan.FirstFloorY - plan.Ground, StepsRise(StepsRoom(plan, ClearOfSlopes(polygon, plot, plots, 0f), s), s));
                if (rise < 0.5f)
                {
                    plan.Notes.Add("no room on the plot for steps up to a plinth: the building sits on the ground instead");
                    plan.Placement = FirstFloor.Ground;
                    rise = GroundFloorRise;
                }
                else if (rise < plan.FirstFloorY - plan.Ground - 1e-4f)
                    plan.Notes.Add($"plinth lowered to {rise:0.##} so the steps fit on the plot");
                plan.FirstFloorY = plan.Ground + rise;
            }

            // The way up: a stair core off the spine against an outer wall, or else an exterior flight to the second
            // level (the first floor, under stilts).
            int levels = plan.Placement == FirstFloor.Stilts ? plan.Floors : plan.Floors - 1;
            var room = ClearOfSlopes(Polygon.Inset(polygon, OverhangMargin), plot, plots, OverhangMargin);
            bool wantRoof = rng.Chance(b.RoofAccessOdds);
            if (levels > 0 && !PlaceCore(plan, s, ref rng))
            {
                if (PlaceExteriorFlight(plan, room, s, ref rng))
                {
                    int reached = plan.Placement == FirstFloor.Stilts ? 1 : 2;
                    if (plan.Floors > reached) plan.Notes.Add($"no stair core fits: an exterior flight climbs to the next floor, {plan.Floors} floors cut to {reached}");
                    else plan.Notes.Add("no stair core fits: an exterior flight climbs to the next floor");
                    plan.Floors = reached;
                }
                else
                {
                    if (plan.Placement == FirstFloor.Stilts)
                    {
                        plan.Notes.Add("no stair core or exterior flight fits under the stilts: the building sits on the ground instead");
                        plan.Placement = FirstFloor.Ground;
                        plan.FirstFloorY = plan.Ground + GroundFloorRise;
                    }
                    plan.Notes.Add($"no stair core or exterior flight fits: {plan.Floors} floors cut to 1");
                    plan.Floors = 1;
                }
            }
            if (plan.Floors < fewest)
            {
                plan.Dropped = "no stair core or exterior flight fits to climb a big building's floors";
                return plan;
            }
            // A small building's roof is reached only by an exterior flight up its outside (not from under stilts).
            if (!big && wantRoof && plan.Placement != FirstFloor.Stilts && plan.Flight == null
                && plan.RoofY + b.RailingHeight <= s.MaxHeight && PlaceExteriorFlight(plan, room, s, ref rng))
                plan.RoofByFlight = true;
            plan.Anchor = plan.HasCore ? CoreWallPoint(plan) : Polygon.Centroid(plan.Footprint);

            SizeFloors(plan, room, floorSide, s, ref rng);
            plan.RoofReachable = plan.RoofByFlight
                || (big && wantRoof && plan.HasCore && plan.RoofY + b.RailingHeight <= s.MaxHeight);
            for (int k = 0; k < plan.Floors; k++) PlanInteriorWalls(plan, k, s, ref rng);
            if (plan.Placement == FirstFloor.Stilts && !PlanStiltsPorch(plan, room, s))
            {
                plan.Dropped = "no porch fits along the stilted building's entrance sides";
                return plan;
            }
            var outside = ClearOfSlopes(Polygon.Inset(polygon, BalconyEdgeMargin), plot, plots, BalconyEdgeMargin);
            PlanBalconies(plan, outside, s, ref rng);
            if (big && !plan.Balconies.Exists(x => x.Floor >= 1 && !x.Landing))
            {
                plan.Dropped = "no balcony or terrace fits on an upper floor";
                return plan;
            }
            for (int k = 0; k < plan.Floors; k++) PlanWindows(plan, k, s, ref rng);
            PlanPorches(plan, room, s);
            for (int k = 0; k < plan.Floors; k++) PlanRoomLevels(plan, k, s, ref rng);
            return plan;
        }

        // ---------------------------------------------------------------- exterior flight

        /// <summary>
        /// A straight flight, <c>doorWidth</c> wide, outside one outer wall and along it, from the ground up to the next
        /// level (the second floor, or the first under stilts), with a landing at its top end against the wall and a
        /// door there. It must lie on the plot clear of slopes (<paramref name="room"/>) and clear of the entrances.
        /// </summary>
        private static bool PlaceExteriorFlight(BuildingPlan plan, List<Vector2> room, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            var footprint = plan.Footprint;
            float topY = plan.Placement == FirstFloor.Stilts ? plan.FirstFloorY : plan.FirstFloorY + plan.FloorHeight;
            float rise = topY - plan.Ground;
            int risers = Risers(rise, s);
            float run = risers * s.CellSize;
            float width = b.DoorWidth;
            var sides = Shuffled(footprint.Count, ref rng);
            bool upFirst = rng.Chance(0.5f);
            foreach (int side in sides)
            {
                Side(footprint, side, out var a, out var dir, out float length, out var outward);
                float needed = run + width + 2f * OpeningGap;
                if (length < needed) continue;
                var face = a + outward * (WallThickness * 0.5f);
                foreach (bool up in upFirst ? new[] { true, false } : new[] { false, true })
                {
                    // The flight climbs along +dir (or -dir) from its low end; the landing lies past its top end.
                    float lowT = up ? OpeningGap : length - OpeningGap;
                    var uphill = up ? dir : -dir;
                    var low = face + dir * lowT + outward * (width * 0.5f);
                    var flight = Rect(low, uphill, outward, run, width);
                    var landing = Rect(low + uphill * run, uphill, outward, width, width);
                    bool fits = Inside(flight, room) && Inside(landing, room);
                    for (int i = 0; i < plan.Entrances.Count && fits; i++)
                    {
                        var steps = Rect(plan.Entrances[i] + plan.Outward[i] * (WallThickness * 0.5f), plan.Outward[i],
                            new Vector2(-plan.Outward[i].y, plan.Outward[i].x), 3f, b.SpineWidth);
                        fits &= Overlap(flight, steps) < 0.01f && Overlap(landing, steps) < 0.01f;
                    }
                    if (!fits) continue;
                    plan.Flight = new ExteriorFlightPlan
                    {
                        Side = side, Low = low, Uphill = uphill, Width = width, Run = run, Risers = risers,
                        BottomY = plan.Ground, TopY = topY, Landing = landing,
                    };
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- floor sizes

        /// <summary>
        /// Each floor above the first is the one below scaled about the anchor by 10–50% (<c>floorResizeMin..Max</c>),
        /// bigger with <c>floorGrowOdds</c>, else smaller. A size that would leave the plot (or its slopes), lose the core
        /// or get narrower than a footprint may be is halved toward the floor below, and then dropped. With an exterior
        /// flight the second floor keeps the first's size, since the flight's landing meets its wall.
        /// </summary>
        private static void SizeFloors(BuildingPlan plan, List<Vector2> room, float minSide, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            plan.FloorPlans.Clear();
            var first = new FloorPlan { Footprint = plan.Footprint };
            plan.FloorPlans.Add(first);
            // The entrances' doors (stilts have no ground-level walls: their entrances are open ground).
            if (plan.Placement != FirstFloor.Stilts)
                foreach (var entrance in plan.Entrances)
                    for (int side = 0; side < plan.Footprint.Count; side++)
                    {
                        Side(plan.Footprint, side, out var a, out var dir, out float length, out _);
                        float t = Vector2.Dot(entrance - a, dir);
                        if (Mathf.Abs(Cross(dir, entrance - a)) > 1e-3f || t <= 0f || t >= length) continue;
                        first.Openings.Add(new WallOpening
                        {
                            Side = side, T = t, HalfWidth = b.DoorWidth * 0.5f, Top = b.DoorHeight, Door = true,
                        });
                        break;
                    }
            for (int k = 1; k < plan.Floors; k++)
            {
                // A full-plot big building has no room outside its walls for a balcony: its top floor shrinks, leaving a
                // terrace instead.
                bool grow = rng.Chance(b.FloorGrowOdds) && !(plan.Class == BuildingClass.Big && plan.FullPlot && k == plan.Floors - 1);
                float amount = rng.Range(b.FloorResizeMin, b.FloorResizeMax);
                var below = plan.FloorPlans[k - 1].Footprint;
                float scale = 1f;
                if (plan.Flight == null)
                    for (int tries = 0; tries < 3; tries++, amount *= 0.5f)
                    {
                        float candidate = grow ? 1f + amount : 1f - amount;
                        if (FloorFits(plan, Scaled(below, plan.Anchor, candidate), room, minSide))
                        {
                            scale = candidate;
                            break;
                        }
                    }
                if (scale == 1f && (b.FloorResizeMax > 0f) && plan.Flight == null) plan.Notes.Add($"floor {k} keeps the size of the one below: no resize fit");
                plan.FloorPlans.Add(new FloorPlan { Footprint = Scaled(below, plan.Anchor, scale), Scale = scale });
            }
        }

        /// <summary>
        /// The point on the wall the core stands against nearest the core's centre. Scaling about a point on a side keeps
        /// that side on its line, so every floor keeps the core against the same wall and shrinks or grows away from it.
        /// </summary>
        private static Vector2 CoreWallPoint(BuildingPlan plan)
        {
            var centre = Polygon.Centroid(CoreRect(plan.Core));
            float best = float.PositiveInfinity;
            var point = centre;
            for (int side = 0; side < plan.Footprint.Count; side++)
            {
                Side(plan.Footprint, side, out var a, out var dir, out float length, out _);
                float t = Mathf.Clamp(Vector2.Dot(centre - a, dir), 0f, length);
                var p = a + dir * t;
                float d = (p - centre).magnitude;
                if (d >= best) continue;
                best = d;
                point = p;
            }
            return point;
        }

        private static bool FloorFits(BuildingPlan plan, List<Vector2> floor, List<Vector2> room, float minSide)
        {
            if (Polygon.MinWidth(floor) < minSide || !Inside(floor, room)) return false;
            if (!plan.HasCore) return true;
            var inner = Polygon.Inset(floor, WallThickness * 0.5f - 0.01f);
            return inner.Count > 0 && Inside(CoreRect(plan.Core, CoreApproach), inner);
        }

        private static List<Vector2> Scaled(List<Vector2> polygon, Vector2 anchor, float scale)
        {
            var result = new List<Vector2>(polygon.Count);
            foreach (var p in polygon) result.Add(anchor + (p - anchor) * scale);
            return result;
        }

        // ---------------------------------------------------------------- floor plans

        /// <summary>
        /// 0..<c>maxInteriorWalls</c> chords of the floor that cross no other chord, so each is one edge between two
        /// convex rooms, every room at least <c>minRoomArea</c> and <c>minRoomWidth</c> (a chord that would make a smaller
        /// one is redrawn, then dropped). Chords keep off the core and its approach and away from the doors. Each edge is
        /// drawn as a wall, nothing or a railing (by their weights). On the spine floor an edge gets a spine-wide gap
        /// wherever it crosses the spine; otherwise a wall gets a door-wide gap, and a railing or an open edge one in its
        /// middle (where an open edge's flight goes).
        /// </summary>
        private static void PlanInteriorWalls(BuildingPlan plan, int k, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            var floor = plan.FloorPlans[k];
            var inner = Polygon.Inset(floor.Footprint, WallThickness * 0.5f);
            if (inner.Count < 3) return;
            int count = rng.Range(0, b.MaxInteriorWalls + 1);
            bool spineFloor = k == 0 && plan.Placement != FirstFloor.Stilts;
            var core = plan.HasCore ? CoreRect(plan.Core, CoreApproach) : null;
            Polygon.Extent(inner, Vector2.right, out float minX, out float maxX);
            Polygon.Extent(inner, Vector2.up, out float minZ, out float maxZ);
            var rooms = new List<List<Vector2>> { inner };
            bool RoomFits(List<Vector2> room) =>
                room.Count >= 3 && Polygon.Area(room) >= b.MinRoomArea && Polygon.MinWidth(room) >= b.MinRoomWidth;

            for (int c = 0; c < count; c++)
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    var p = new Vector2(rng.Range(minX, maxX), rng.Range(minZ, maxZ));
                    float angle = rng.Range(0f, Mathf.PI);
                    float gapT = rng.NextFloat();
                    float kindT = rng.NextFloat();
                    if (!Polygon.Contains(inner, p)) continue;
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    if (!Chord(inner, p, dir, out var from, out var to)) continue;
                    float length = (to - from).magnitude;
                    if (length < 3f) continue;
                    bool ok = true;
                    // Chords neither cross nor come near each other's ends, so no wall band reaches past another edge.
                    foreach (var other in floor.Walls)
                        ok &= !SegmentsCross(from, to, other.From, other.To)
                            && DistanceToSegment(from, other.From, other.To) > 1f && DistanceToSegment(to, other.From, other.To) > 1f
                            && DistanceToSegment(other.From, from, to) > 1f && DistanceToSegment(other.To, from, to) > 1f;
                    if (ok && core != null) ok &= !SegmentHitsPolygon(from, to, core);
                    if (ok && k == 0)
                        foreach (var e in plan.Entrances) ok &= DistanceToSegment(e, from, to) > b.DoorWidth * 0.5f + 1f;
                    if (ok && plan.Flight != null && !plan.RoofByFlight && k == FlightFloor(plan))
                        ok &= DistanceToSegment(FlightDoor(plan), from, to) > b.DoorWidth * 0.5f + 1f;
                    if (!ok) continue;

                    // The room it cuts (chords never cross, so it lies in one), and the two rooms it makes.
                    var along = (to - from) / length;
                    int r = RoomAt(rooms, (from + to) * 0.5f);
                    if (r < 0) continue;
                    Polygon.SplitByChord(rooms[r], from, along, out var left, out var right);
                    if (!RoomFits(left) || !RoomFits(right)) continue;

                    var wall = new InteriorWall { From = from, To = to, Kind = DrawEdgeKind(b, kindT), Left = r, Right = rooms.Count };
                    if (spineFloor)
                        foreach (var spine in plan.Spine)
                            if (ClipSegment(from, to, spine, out float t0, out float t1))
                            {
                                // At least a door wide, where a strip only grazes the chord.
                                float mid = Mathf.Clamp((t0 + t1) * 0.5f, b.DoorWidth * 0.5f, length - b.DoorWidth * 0.5f);
                                float half = Mathf.Max((t1 - t0) * 0.5f + 0.25f, b.DoorWidth * 0.5f);
                                wall.Gaps.Add(new Vector2(Mathf.Max(0f, mid - half), Mathf.Min(length, mid + half)));
                            }
                    MergeGaps(wall.Gaps);
                    if (wall.Gaps.Count == 0)
                    {
                        float half = b.DoorWidth * 0.5f;
                        float t = wall.Kind == EdgeKind.Wall ? Mathf.Lerp(half + OpeningGap, length - half - OpeningGap, gapT) : length * 0.5f;
                        wall.Gaps.Add(new Vector2(t - half, t + half));
                    }

                    // Edges that bounded the cut room now bound one of its halves.
                    foreach (var other in floor.Walls)
                    {
                        var mid = (other.From + other.To) * 0.5f;
                        var leftOf = (other.To - other.From).normalized;
                        leftOf = new Vector2(-leftOf.y, leftOf.x) * 0.05f;
                        if (other.Left == r && Polygon.Contains(right, mid + leftOf)) other.Left = rooms.Count;
                        if (other.Right == r && Polygon.Contains(right, mid - leftOf)) other.Right = rooms.Count;
                    }
                    rooms[r] = left;
                    rooms.Add(right);
                    floor.Walls.Add(wall);
                    break;
                }
            }
            floor.Rooms.Clear();
            foreach (var room in rooms) floor.Rooms.Add(new RoomPlan { Polygon = room });
        }

        private static EdgeKind DrawEdgeKind(in BuildingSettings b, float t)
        {
            float total = b.WallEdgeWeight + b.OpenEdgeWeight + b.RailingEdgeWeight;
            float x = t * total;
            if (x < b.WallEdgeWeight || total <= 0f) return EdgeKind.Wall;
            return x < b.WallEdgeWeight + b.OpenEdgeWeight ? EdgeKind.Open : EdgeKind.Railing;
        }

        private static int RoomAt(List<List<Vector2>> rooms, Vector2 p)
        {
            for (int r = 0; r < rooms.Count; r++)
                if (Polygon.Contains(rooms[r], p)) return r;
            return -1;
        }

        /// <summary>The room of a floor that holds a point, or -1.</summary>
        public static int RoomAt(FloorPlan floor, Vector2 p)
        {
            for (int r = 0; r < floor.Rooms.Count; r++)
                if (Polygon.Contains(floor.Rooms[r].Polygon, p)) return r;
            return -1;
        }

        /// <summary>A room's level relative to its floor (0 when the floor has no rooms).</summary>
        public static float RoomOffset(FloorPlan floor, int room) => room >= 0 && room < floor.Rooms.Count ? floor.Rooms[room].Offset : 0f;

        /// <summary>
        /// The outline of a room's slab: its polygon less half a wall's thickness along each of its edges drawn as a wall,
        /// so the wall stands between the two rooms' slabs (stop short). Open and railing edges keep the centre line.
        /// </summary>
        public static List<Vector2> RoomSlab(FloorPlan floor, int room)
        {
            var polygon = floor.Rooms[room].Polygon;
            float half = InteriorWallThickness * 0.5f;
            foreach (var e in floor.Walls)
            {
                if (e.Kind != EdgeKind.Wall || (e.Left != room && e.Right != room)) continue;
                var dir = (e.To - e.From).normalized;
                var right = new Vector2(dir.y, -dir.x);
                float c = Vector2.Dot(right, e.From);
                polygon = e.Left == room ? Polygon.ClipHalfPlane(polygon, right, c - half) : Polygon.ClipHalfPlane(polygon, -right, -c - half);
                if (polygon.Count == 0) break;
            }
            return polygon;
        }

        // ---------------------------------------------------------------- room levels

        /// <summary>
        /// Each room not holding an outside door or the stair core gets, with <c>roomOffsetOdds</c>, a level
        /// <c>minRoomOffset..maxRoomOffset</c> above or below its floor's, in whole risers. A first floor only raises its
        /// rooms (a lowered one would eat into the ground or the open space under stilts); a lowered room on an upper
        /// floor drops its slab into rooms under it and nothing else. An offset that doesn't fit (<see cref="RoomsFit"/>) shrinks by risers, down to 0,
        /// so the level as it stood before always fits. Then the flights between rooms at different levels.
        /// </summary>
        private static void PlanRoomLevels(BuildingPlan plan, int k, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            var floor = plan.FloorPlans[k];
            if (floor.Rooms.Count == 0) return;
            foreach (var o in floor.Openings)
            {
                if (!o.Door) continue;
                Side(floor.Footprint, o.Side, out var a, out var dir, out _, out var outward);
                int r = RoomAt(floor, a + dir * o.T - outward * (WallThickness * 0.5f + 0.5f));
                if (r >= 0) floor.Rooms[r].Fixed = true;
            }
            if (plan.HasCore)
            {
                int r = RoomAt(floor, Polygon.Centroid(CoreRect(plan.Core)));
                if (r >= 0) floor.Rooms[r].Fixed = true;
            }

            float riser = s.TransitionSizes.StairRiserHeight;
            int fewest = Mathf.CeilToInt(b.MinRoomOffset / riser - 1e-4f);
            int most = Mathf.Max(fewest, Mathf.FloorToInt(b.MaxRoomOffset / riser + 1e-4f));
            bool canLower = k > 0;
            foreach (var room in floor.Rooms)
            {
                bool offset = rng.Chance(b.RoomOffsetOdds);
                int risers = rng.Range(fewest, most + 1);
                bool lower = rng.Chance(0.5f);
                if (!offset || room.Fixed) continue;
                float sign = lower && canLower ? -1f : 1f;
                for (int n = risers; n > 0; n--)
                {
                    room.Offset = sign * n * riser;
                    if (RoomsFit(plan, k, s)) break;
                    room.Offset = 0f;
                }
                if (room.Offset == 0f && risers > 0) plan.Notes.Add($"a room on floor {k} keeps its floor's level: no offset fit");
            }
            PlanFlights(floor, s);
        }

        /// <summary>Fills every edge's flights from its rooms' levels (see <see cref="EdgeFlights"/>).</summary>
        public static void PlanFlights(FloorPlan floor, in LevelGenSettings s)
        {
            foreach (var e in floor.Walls) e.Flights = EdgeFlights(floor, e, s);
        }

        /// <summary>
        /// Whether floor k's room levels work: every room keeps headroom under the ceiling, every wall gap a door's height
        /// over the higher room, a lowered room only drops into rooms below (clear of their edges, flights and the core,
        /// keeping their headroom and door height), and every flight with its run-out lies in its lower room, clear of the
        /// core and its approach, the outside doors' approaches and the other flights.
        /// </summary>
        private static bool RoomsFit(BuildingPlan plan, int k, in LevelGenSettings s)
        {
            var b = s.Buildings;
            var floor = plan.FloorPlans[k];
            float slab = LevelGenSettings.SlabThickness;
            float clear = plan.FloorHeight - slab;
            float headroom = s.Player.PlayerHeight + HeadroomSlack;
            foreach (var room in floor.Rooms)
                if (clear - room.Offset < headroom) return false;
            foreach (var e in floor.Walls)
                if (e.Kind == EdgeKind.Wall && Mathf.Max(RoomOffset(floor, e.Left), RoomOffset(floor, e.Right)) + b.DoorHeight > clear + 1e-4f)
                    return false;

            var core = plan.HasCore ? CoreRect(plan.Core, CoreApproach) : null;
            for (int r = 0; r < floor.Rooms.Count; r++)
            {
                float offset = floor.Rooms[r].Offset;
                if (offset >= 0f) continue;
                if (k == 0) return false;
                // Its slab and the walls round it reach down into the floor below. On a floor the same size as the one
                // below, the outer walls run on down; on a resized one a skirt under the outer wall closes the drop, so
                // the reach takes in the wall, and must keep inside the walls below.
                var below = plan.FloorPlans[k - 1];
                bool skirt = floor.Scale != 1f;
                var reach = Polygon.Outset(floor.Rooms[r].Polygon, skirt ? WallThickness + InteriorWallThickness : InteriorWallThickness);
                if (skirt && !Inside(reach, Polygon.Inset(below.Footprint, WallThickness * 0.5f))) return false;
                if (core != null && Overlap(core, reach) > 0.01f) return false;
                foreach (var e in below.Walls)
                {
                    if (e.Kind != EdgeKind.Open && SegmentHitsPolygon(e.From, e.To, reach)) return false;
                    foreach (var f in e.Flights)
                        if (Overlap(FlightRect(f, 0f), reach) > 0.01f) return false;
                }
                for (int q = 0; q < below.Rooms.Count; q++)
                    if (Overlap(below.Rooms[q].Polygon, reach) > 0.01f
                        && clear + offset - below.Rooms[q].Offset < Mathf.Max(headroom, b.DoorHeight))
                        return false;
            }

            var keep = new List<List<Vector2>>();
            if (core != null) keep.Add(core);
            foreach (var o in floor.Openings)
            {
                if (!o.Door) continue;
                Side(floor.Footprint, o.Side, out var a, out var dir, out _, out var outward);
                keep.Add(Rect(a + dir * o.T - outward * (WallThickness * 0.5f), -outward, dir, LevelGenSettings.MinWalkWidth, 2f * o.HalfWidth));
            }
            foreach (var e in floor.Walls)
                foreach (var f in EdgeFlights(floor, e, s))
                {
                    int lower = RoomOffset(floor, e.Left) < RoomOffset(floor, e.Right) ? e.Left : e.Right;
                    var slabOutline = RoomSlab(floor, lower);
                    var rect = FlightRect(f, LevelGenSettings.MinWalkWidth);
                    // Shrunk a hair, so a flight flush with its room's edge counts as inside.
                    var shrunk = Rect(f.Top + f.Down * 0.01f, f.Down, new Vector2(-f.Down.y, f.Down.x), f.Run + LevelGenSettings.MinWalkWidth - 0.02f, f.Width - 0.02f);
                    if (!Inside(shrunk, slabOutline)) return false;
                    foreach (var other in keep)
                        if (Overlap(rect, other) > 0.01f) return false;
                    keep.Add(rect);
                }
            return true;
        }

        /// <summary>The flights an edge's gaps need when its rooms sit at different levels: each as wide as its gap, from
        /// the edge (a wall's face) down into the lower room, one riser per cell.</summary>
        private static List<RoomFlight> EdgeFlights(FloorPlan floor, InteriorWall e, in LevelGenSettings s)
        {
            var flights = new List<RoomFlight>();
            float left = RoomOffset(floor, e.Left), right = RoomOffset(floor, e.Right);
            if (Mathf.Abs(left - right) < 1e-4f) return flights;
            var dir = (e.To - e.From).normalized;
            var leftNormal = new Vector2(-dir.y, dir.x);
            var down = left < right ? leftNormal : -leftNormal;
            float start = e.Kind == EdgeKind.Wall ? InteriorWallThickness * 0.5f : 0f;
            int risers = Mathf.RoundToInt(Mathf.Abs(left - right) / s.TransitionSizes.StairRiserHeight);
            foreach (var gap in e.Gaps)
                flights.Add(new RoomFlight
                {
                    Top = e.From + dir * ((gap.x + gap.y) * 0.5f) + down * start, Down = down, Width = gap.y - gap.x,
                    Run = risers * s.CellSize, Risers = risers, Low = Mathf.Min(left, right), High = Mathf.Max(left, right),
                });
            return flights;
        }

        /// <summary>A room flight's footprint, <paramref name="runOut"/> longer at its foot.</summary>
        public static List<Vector2> FlightRect(RoomFlight f, float runOut) =>
            Rect(f.Top, f.Down, new Vector2(-f.Down.y, f.Down.x), f.Run + runOut, f.Width);

        // ---------------------------------------------------------------- porches

        /// <summary>
        /// A stilted building's porch: a slab balcony on the first floor along each entrance side (but the exterior
        /// flight's, whose landing serves), railed on its open edges, with a door onto it. False when one doesn't fit.
        /// </summary>
        private static bool PlanStiltsPorch(BuildingPlan plan, List<Vector2> room, in LevelGenSettings s)
        {
            var b = s.Buildings;
            var footprint = plan.Footprint;
            for (int side = 0; side < footprint.Count; side++)
            {
                Side(footprint, side, out var a, out var dir, out float length, out var outward);
                if (!plan.Outward.Exists(o => Vector2.Dot(o, outward) > 0.9999f)) continue;
                if (plan.Flight != null && side == plan.Flight.Side) continue;
                float span = length - 2f * (WallThickness + OpeningGap);
                if (span < b.DoorWidth + 1f) return false;
                var middle = a + dir * (length * 0.5f);
                var face = middle + outward * (WallThickness * 0.5f);
                var slab = Rect(face - dir * (span * 0.5f) + outward * (b.BalconyDepth * 0.5f), dir, outward, span, b.BalconyDepth);
                if (!Inside(slab, room)) return false;
                if (plan.Flight != null && (Overlap(slab, plan.Flight.Landing) > 0.01f || Overlap(slab, FlightRect(plan)) > 0.01f))
                    return false;
                if (!AddDoor(plan, 0, side, middle, s)) return false;
                var porch = new BalconyPlan { Floor = 0, Polygon = slab, Door = middle, Outward = outward };
                var near0 = face - dir * (span * 0.5f);
                var near1 = face + dir * (span * 0.5f);
                var far0 = near0 + outward * b.BalconyDepth;
                var far1 = near1 + outward * b.BalconyDepth;
                porch.Railings.Add((near0, far0));
                porch.Railings.Add((far0, far1));
                porch.Railings.Add((far1, near1));
                plan.Balconies.Add(porch);
            }
            return true;
        }

        /// <summary>
        /// A ground-level or raised building's porch: along every side with an entrance (every side, for a full-plot
        /// building), from the wall's outer face out to <c>balconyDepth</c> from the footprint, mitred where two meet,
        /// at the first floor's level and standing on the plot. It keeps inside the plot clear of slopes, and is carved
        /// round the steps at each door and the exterior flight and its landing.
        /// </summary>
        private static void PlanPorches(BuildingPlan plan, List<Vector2> room, in LevelGenSettings s)
        {
            if (plan.Placement == FirstFloor.Stilts) return;
            var b = s.Buildings;
            var holes = new List<List<Vector2>>();
            if (plan.Placement == FirstFloor.Raised)
            {
                float run = Risers(plan.FirstFloorY - plan.Ground, s) * s.CellSize;
                for (int i = 0; i < plan.Entrances.Count; i++)
                    holes.Add(Rect(plan.Entrances[i] + plan.Outward[i] * (WallThickness * 0.5f), plan.Outward[i],
                        new Vector2(-plan.Outward[i].y, plan.Outward[i].x), run, b.SpineWidth));
            }
            if (plan.Flight != null)
            {
                holes.Add(FlightRect(plan));
                holes.Add(plan.Flight.Landing);
            }
            var bands = Bands(plan.Footprint, true, -WallThickness * 0.5f, b.BalconyDepth);
            for (int side = 0; side < plan.Footprint.Count; side++)
            {
                var outward = SideOutward(plan.Footprint, side);
                if (!plan.FullPlot && !plan.Outward.Exists(o => Vector2.Dot(o, outward) > 0.9999f)) continue;
                var pieces = new List<List<Vector2>> { Polygon.ClipConvex(bands[side], room) };
                foreach (var hole in holes)
                {
                    var next = new List<List<Vector2>>();
                    foreach (var piece in pieces)
                        if (piece.Count >= 3) next.AddRange(Polygon.Subtract(piece, hole));
                    pieces = next;
                }
                foreach (var piece in pieces)
                    if (piece.Count >= 3 && Polygon.Area(piece) > 0.01f) plan.Porches.Add(piece);
            }
        }

        /// <summary>The floor an exterior flight climbs to: the second, or the first under stilts.</summary>
        public static int FlightFloor(BuildingPlan plan) => plan.Placement == FirstFloor.Stilts ? 0 : 1;

        /// <summary>The flight's own rectangle (without its landing).</summary>
        public static List<Vector2> FlightRect(BuildingPlan plan)
        {
            var f = plan.Flight;
            return Rect(f.Low, f.Uphill, SideOutward(plan.Footprint, f.Side), f.Run, f.Width);
        }

        /// <summary>Where the exterior flight's landing meets the wall: the door into the floor it climbs to.</summary>
        private static Vector2 FlightDoor(BuildingPlan plan)
        {
            var f = plan.Flight;
            Side(plan.Footprint, f.Side, out var a, out var dir, out _, out var outward);
            var landingCentre = Polygon.Centroid(f.Landing);
            return a + dir * Vector2.Dot(landingCentre - a, dir);
        }

        // ---------------------------------------------------------------- balconies and terraces

        /// <summary>
        /// On each elevated floor, 1–3 sides open onto the outside: onto the terrace when the floor is smaller than the
        /// one below and the terrace there is wide enough to walk, otherwise onto a slab balcony <c>balconyDepth</c> (or as deep as fits, at least a walkway)
        /// deep (railed on its open edges) if it fits on the plot. The exterior flight's landing gets its door too.
        /// </summary>
        private static void PlanBalconies(BuildingPlan plan, List<Vector2> room, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            if (plan.Flight != null && !plan.RoofByFlight)
            {
                int floor = FlightFloor(plan);
                var door = FlightDoor(plan);
                var landing = new BalconyPlan
                {
                    Floor = floor, Landing = true, Polygon = plan.Flight.Landing, Door = door,
                    Outward = SideOutward(plan.FloorPlans[floor].Footprint, plan.Flight.Side),
                };
                if (AddDoor(plan, floor, plan.Flight.Side, door, s)) plan.Balconies.Add(landing);
            }

            for (int k = 0; k < plan.Floors; k++)
            {
                if (!plan.Elevated(k)) continue;
                var floor = plan.FloorPlans[k].Footprint;
                bool terrace = k > 0 && plan.FloorPlans[k].Scale < 1f;
                int wanted = rng.Range(1, 4);
                float depthChoice = rng.Range(3f, 6f);
                int added = 0;
                foreach (int side in Shuffled(floor.Count, ref rng))
                {
                    if (added >= wanted) break;
                    if (plan.Flight != null && side == plan.Flight.Side) continue;
                    Side(floor, side, out var a, out var dir, out float length, out var outward);
                    if (plan.Balconies.Exists(x => x.Floor == k && Vector2.Dot(x.Outward, outward) > 0.9999f)) continue;
                    var middle = a + dir * (length * 0.5f);
                    if (terrace)
                    {
                        // Sides are parallel to the floor below's, so the terrace is as deep all along this side.
                        var lower = plan.FloorPlans[k - 1].Footprint;
                        float terraceDepth = Vector2.Dot(outward, lower[side] - a);
                        if (terraceDepth < LevelGenSettings.MinWalkWidth + WallThickness) continue;
                        if (!AddDoor(plan, k, side, middle, s)) continue;
                        plan.Balconies.Add(new BalconyPlan { Floor = k, Terrace = true, Polygon = lower, Door = middle, Outward = outward });
                        added++;
                        continue;
                    }

                    float span = Mathf.Min(length - 2f * OpeningGap, depthChoice);
                    if (span < b.DoorWidth + 1f) continue;
                    var face = middle + outward * (WallThickness * 0.5f);
                    // As deep as fits, down to a walkway's width.
                    float depth = b.BalconyDepth;
                    var slab = Rect(face - dir * (span * 0.5f) + outward * (depth * 0.5f), dir, outward, span, depth);
                    while (!Inside(slab, room) && depth - 0.25f >= LevelGenSettings.MinWalkWidth - 1e-4f)
                    {
                        depth -= 0.25f;
                        slab = Rect(face - dir * (span * 0.5f) + outward * (depth * 0.5f), dir, outward, span, depth);
                    }
                    if (!Inside(slab, room)) continue;
                    if (plan.Flight != null && (Overlap(slab, plan.Flight.Landing) > 0.01f || Overlap(slab, FlightRect(plan)) > 0.01f))
                        continue;
                    if (!AddDoor(plan, k, side, middle, s)) continue;
                    var balcony = new BalconyPlan { Floor = k, Polygon = slab, Door = middle, Outward = outward };
                    var near0 = face - dir * (span * 0.5f);
                    var near1 = face + dir * (span * 0.5f);
                    var far0 = near0 + outward * depth;
                    var far1 = near1 + outward * depth;
                    balcony.Railings.Add((near0, far0));
                    balcony.Railings.Add((far0, far1));
                    balcony.Railings.Add((far1, near1));
                    plan.Balconies.Add(balcony);
                    added++;
                }
            }
        }

        /// <summary>Adds a door at the point nearest <paramref name="at"/> on a side of a floor, if it keeps clear of the
        /// corners, the other openings, the interior walls and the core.</summary>
        private static bool AddDoor(BuildingPlan plan, int k, int side, Vector2 at, in LevelGenSettings s)
        {
            var b = s.Buildings;
            var floor = plan.FloorPlans[k];
            Side(floor.Footprint, side, out var a, out var dir, out float length, out _);
            float half = b.DoorWidth * 0.5f;
            float t = Vector2.Dot(at - a, dir);
            foreach (float shift in new[] { 0f, 1f, -1f, 2f, -2f, 3f, -3f })
            {
                float candidate = t + shift;
                if (!OpeningFits(plan, k, side, candidate, half)) continue;
                floor.Openings.Add(new WallOpening { Side = side, T = candidate, HalfWidth = half, Bottom = 0f, Top = b.DoorHeight, Door = true });
                return true;
            }
            return false;
        }

        /// <summary>
        /// Windows along each side's free wall: <c>windowWidth</c> wide, at <c>windowSill</c>, <c>windowHeight</c> tall,
        /// spaced by a random gap, wherever they keep clear of corners, doors, interior walls and the core.
        /// </summary>
        private static void PlanWindows(BuildingPlan plan, int k, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            var floor = plan.FloorPlans[k];
            for (int side = 0; side < floor.Footprint.Count; side++)
            {
                Side(floor.Footprint, side, out _, out _, out float length, out _);
                float t = OpeningGap + WallThickness;
                while (t < length)
                {
                    float width = rng.Range(b.MinWindowWidth, b.MaxWindowWidth);
                    float gap = rng.Range(1f, 2.5f);
                    float centre = t + width * 0.5f;
                    if (OpeningFits(plan, k, side, centre, width * 0.5f))
                    {
                        floor.Openings.Add(new WallOpening
                        {
                            Side = side, T = centre, HalfWidth = width * 0.5f, Bottom = b.WindowSill, Top = b.WindowSill + b.WindowHeight,
                        });
                        t += width + gap;
                    }
                    else t += 0.5f;
                }
            }
        }

        private static bool OpeningFits(BuildingPlan plan, int k, int side, float t, float half)
        {
            var floor = plan.FloorPlans[k];
            Side(floor.Footprint, side, out var a, out var dir, out float length, out var outward);
            float corner = WallThickness + OpeningGap;
            if (t - half < corner || t + half > length - corner) return false;
            foreach (var o in floor.Openings)
                if (o.Side == side && Mathf.Abs(o.T - t) < o.HalfWidth + half + OpeningGap) return false;
            foreach (var wall in floor.Walls)
                foreach (var end in new[] { wall.From, wall.To })
                    if (Mathf.Abs(Cross(dir, end - a)) < 1f && Mathf.Abs(Vector2.Dot(end - a, dir) - t) < half + OpeningGap + InteriorWallThickness)
                        return false;
            if (plan.HasCore)
            {
                // The core's span along this side, when it stands within a metre of the wall.
                var rect = CoreRect(plan.Core);
                float near = float.PositiveInfinity, lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                foreach (var p in rect)
                {
                    near = Mathf.Min(near, Mathf.Abs(Cross(dir, p - a)));
                    float u = Vector2.Dot(p - a, dir);
                    lo = Mathf.Min(lo, u);
                    hi = Mathf.Max(hi, u);
                }
                if (near < 1f && t + half > lo - OpeningGap && t - half < hi + OpeningGap) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- polygon helpers

        private static void Side(List<Vector2> polygon, int side, out Vector2 a, out Vector2 dir, out float length, out Vector2 outward)
        {
            a = polygon[side];
            var e = polygon[(side + 1) % polygon.Count];
            length = (e - a).magnitude;
            dir = (e - a) / length;
            outward = new Vector2(dir.y, -dir.x);
        }

        private static Vector2 SideOutward(List<Vector2> polygon, int side)
        {
            Side(polygon, side, out _, out _, out _, out var outward);
            return outward;
        }

        /// <summary>A counter-clockwise rectangle with one end's middle at <paramref name="start"/>, running
        /// <paramref name="length"/> along <paramref name="along"/>, <paramref name="width"/> across.</summary>
        private static List<Vector2> Rect(Vector2 start, Vector2 along, Vector2 across, float length, float width)
        {
            var half = across * (width * 0.5f);
            var rect = new List<Vector2> { start - half, start + along * length - half, start + along * length + half, start + half };
            if (Polygon.SignedArea(rect) < 0f) rect.Reverse();
            return rect;
        }

        private static bool Inside(List<Vector2> polygon, List<Vector2> container)
        {
            if (container.Count < 3) return false;
            foreach (var p in polygon)
                if (!Polygon.Contains(container, p)) return false;
            return true;
        }

        private static float Overlap(List<Vector2> a, List<Vector2> b) => Polygon.Area(Polygon.ClipConvex(a, b));

        private static List<int> Shuffled(int count, ref Pcg32 rng)
        {
            var list = new List<int>();
            for (int i = 0; i < count; i++) list.Add(i);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        /// <summary>The chord of a convex polygon along the line through <paramref name="p"/> with <paramref name="dir"/>.</summary>
        private static bool Chord(List<Vector2> polygon, Vector2 p, Vector2 dir, out Vector2 from, out Vector2 to)
        {
            from = to = p;
            return ClipLine(p, dir, polygon, out float t0, out float t1) && (from = p + dir * t0) != (to = p + dir * t1);
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
                if (Mathf.Abs(den) < 1e-9f)
                {
                    if (num < 0f) return false;
                    continue;
                }
                float t = num / den;
                if (den > 0f) t1 = Mathf.Min(t1, t);
                else t0 = Mathf.Max(t0, t);
            }
            return t1 > t0;
        }

        /// <summary>The part of segment <paramref name="from"/>–<paramref name="to"/> inside a convex polygon, as distances
        /// from <paramref name="from"/>.</summary>
        private static bool ClipSegment(Vector2 from, Vector2 to, List<Vector2> polygon, out float t0, out float t1)
        {
            float length = (to - from).magnitude;
            var dir = (to - from) / length;
            if (!ClipLine(from, dir, polygon, out t0, out t1)) return false;
            t0 = Mathf.Max(t0, 0f);
            t1 = Mathf.Min(t1, length);
            return t1 > t0;
        }

        private static bool SegmentHitsPolygon(Vector2 from, Vector2 to, List<Vector2> polygon) =>
            ClipSegment(from, to, polygon, out _, out _);

        private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float d1 = Cross(b - a, c - a), d2 = Cross(b - a, d - a), d3 = Cross(d - c, a - c), d4 = Cross(d - c, b - c);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        private static void MergeGaps(List<Vector2> gaps)
        {
            gaps.Sort((x, y) => x.x.CompareTo(y.x));
            for (int i = gaps.Count - 1; i > 0; i--)
            {
                if (gaps[i].x > gaps[i - 1].y) continue;
                gaps[i - 1] = new Vector2(gaps[i - 1].x, Mathf.Max(gaps[i - 1].y, gaps[i].y));
                gaps.RemoveAt(i);
            }
        }

        /// <summary>
        /// A convex footprint with the vertices dropped that end sides shorter than <see cref="MinSide"/> (slivers a slope's
        /// clear-out leaves at a corner), whose mitred wall bands would cross. Dropping a vertex only shrinks the polygon.
        /// </summary>
        private static List<Vector2> WithoutShortSides(List<Vector2> polygon)
        {
            var result = new List<Vector2>(polygon);
            for (int i = 0; i < result.Count && result.Count > 3;)
            {
                if ((result[(i + 1) % result.Count] - result[i]).magnitude >= MinSide) i++;
                else result.RemoveAt((i + 1) % result.Count);
            }
            return result;
        }

        /// <summary>Footprint sides are at least this long.</summary>
        public const float MinSide = 1f;

        /// <summary>The steepest rise steps can climb within <paramref name="run"/>, one riser per cell.</summary>
        private static float StepsRise(float run, in LevelGenSettings s) =>
            Mathf.Floor(run / s.CellSize) * s.TransitionSizes.StairRiserHeight;

        /// <summary>
        /// How far steps can run out from the doors before any part of them (spine-wide) would leave the plot, less a
        /// cell: the least over the entrances.
        /// </summary>
        private static float StepsRoom(BuildingPlan plan, List<Vector2> plot, in LevelGenSettings s)
        {
            float best = float.PositiveInfinity;
            float half = s.Buildings.SpineWidth * 0.5f;
            for (int i = 0; i < plan.Entrances.Count; i++)
            {
                var outward = plan.Outward[i];
                var across = new Vector2(-outward.y, outward.x);
                var face = plan.Entrances[i] + outward * (WallThickness * 0.5f);
                float run = 0f;
                for (float d = s.CellSize; d <= 4f; d += s.CellSize)
                {
                    var end = face + outward * (d + s.CellSize);
                    if (!Polygon.Contains(plot, end - across * half) || !Polygon.Contains(plot, end + across * half)) break;
                    run = d;
                }
                best = Mathf.Min(best, run);
            }
            return best;
        }

        /// <summary>The polygon with every ramp and bank cut into this plot (plus <paramref name="margin"/>) clipped off.</summary>
        private static List<Vector2> ClearOfSlopes(List<Vector2> polygon, int plot, PlotPlan plots, float margin)
        {
            foreach (var segment in plots.Segments)
            {
                if (segment.Lower != plot || !TransitionStage.IsSlope(segment.Type) || polygon.Count == 0) continue;
                float run = segment.Type == TransitionType.Ramp ? segment.Ramp.Run : segment.Bank.Run;
                var into = (segment.Type == TransitionType.Ramp ? segment.Ramp : segment.Bank).Centre - (segment.From + segment.To) * 0.5f;
                into.Normalize();
                // Keep dot(into, p - from) ≥ run + margin.
                polygon = Polygon.ClipHalfPlane(polygon, -into, -(Vector2.Dot(into, segment.From) + run + margin));
            }
            return polygon;
        }

        /// <summary>
        /// A rectangle at <paramref name="angle"/>, centred on the room's centroid, as large as fits times
        /// <paramref name="scale"/>, with up to two corners cut (a 5- or 6-sided convex polygon).
        /// </summary>
        private static List<Vector2> Inscribed(List<Vector2> room, float angle, float aspect, float scale, int chamfers,
            float chamferT)
        {
            if (room.Count < 3) return room;
            var c = Polygon.Centroid(room);
            var u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var v = new Vector2(-u.y, u.x);
            List<Vector2> Rect(float k) => new()
            {
                c - u * aspect * k - v * k, c + u * aspect * k - v * k, c + u * aspect * k + v * k, c - u * aspect * k + v * k,
            };
            float lo = 0f, hi = 100f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) * 0.5f;
                bool fits = true;
                foreach (var p in Rect(mid)) fits &= Polygon.Contains(room, p);
                if (fits) lo = mid;
                else hi = mid;
            }
            var rect = Rect(lo * scale);
            float shorter = 2f * lo * scale;
            var shape = new List<Vector2>();
            for (int k = 0; k < 4; k++)
            {
                // Cut corners 0 and 2 (opposite, so the shape stays roomy).
                bool cut = (k == 0 && chamfers >= 1) || (k == 2 && chamfers >= 2);
                if (!cut)
                {
                    shape.Add(rect[k]);
                    continue;
                }
                var previous = rect[(k + 3) % 4];
                var next = rect[(k + 1) % 4];
                float d = chamferT * shorter;
                shape.Add(rect[k] + (previous - rect[k]).normalized * d);
                shape.Add(rect[k] + (next - rect[k]).normalized * d);
            }
            return shape;
        }

        /// <summary>
        /// One entrance on the footprint side nearest each two-way transition of the plot (and, for a full-plot
        /// building, each cliff that drops into it), at most one per side; then, if fewer than two, one on the side
        /// farthest from the first.
        /// </summary>
        private static bool PlaceEntrances(BuildingPlan plan, int plot, PlotPlan plots, in LevelGenSettings s)
        {
            var b = s.Buildings;
            var footprint = plan.Footprint;
            float clear = Mathf.Max(b.SpineWidth * 0.5f + PillarSide, b.DoorWidth * 0.5f + WallThickness);

            bool TryAdd(Vector2 target, List<int> sides)
            {
                foreach (int side in sides)
                {
                    var a = footprint[side];
                    var e = footprint[(side + 1) % footprint.Count];
                    float length = (e - a).magnitude;
                    if (length < 2f * clear) continue;
                    var dir = (e - a) / length;
                    var outward = new Vector2(dir.y, -dir.x);
                    // One door per side: a side that already has one serves this transition too.
                    foreach (var existing in plan.Outward)
                        if (Vector2.Dot(existing, outward) > 0.9999f) return true;
                    float t = Mathf.Clamp(Vector2.Dot(target - a, dir), clear, length - clear);
                    plan.Entrances.Add(a + dir * t);
                    plan.Outward.Add(outward);
                    return true;
                }
                return false;
            }

            List<int> SidesByDistance(Vector2 target)
            {
                var sides = new List<int>();
                for (int k = 0; k < footprint.Count; k++) sides.Add(k);
                sides.Sort((x, y) => SideDistance(footprint, x, target).CompareTo(SideDistance(footprint, y, target)));
                return sides;
            }

            foreach (var segment in plots.Segments)
            {
                if (segment.PlotA != plot && segment.PlotB != plot) continue;
                bool twoWay = TransitionTypes.IsTwoWay(segment.Type);
                bool dropsIn = plan.FullPlot && segment.Type == TransitionType.Cliff && segment.Lower == plot;
                if (!twoWay && !dropsIn) continue;
                var target = (segment.From + segment.To) * 0.5f;
                TryAdd(target, SidesByDistance(target));
            }

            if (plan.Entrances.Count < 2)
            {
                // Opposite the first entrance, or across the footprint if there is none.
                var centre = Polygon.Centroid(footprint);
                var away = plan.Entrances.Count > 0 ? centre + (centre - plan.Entrances[0]) * 4f : centre + Vector2.right * 100f;
                TryAdd(away, SidesByDistance(away));
                if (plan.Entrances.Count < 2) TryAdd(centre * 2f - away, SidesByDistance(centre * 2f - away));
            }
            return plan.Entrances.Count >= 2;
        }

        private static float SideDistance(List<Vector2> polygon, int side, Vector2 p)
        {
            var a = polygon[side];
            var b = polygon[(side + 1) % polygon.Count];
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        /// <summary>A spine-wide lobby square on the footprint's centroid, and a spine-wide strip from it to each
        /// entrance, running through the wall to its outer face.</summary>
        private static void BuildSpine(BuildingPlan plan, in LevelGenSettings s)
        {
            float half = s.Buildings.SpineWidth * 0.5f;
            var centre = Polygon.Centroid(plan.Footprint);
            var u = (plan.Footprint[1] - plan.Footprint[0]).normalized;
            var v = new Vector2(-u.y, u.x);
            plan.Spine.Add(new List<Vector2> { centre - u * half - v * half, centre + u * half - v * half, centre + u * half + v * half, centre - u * half + v * half });
            for (int i = 0; i < plan.Entrances.Count; i++)
            {
                var door = plan.Entrances[i] + plan.Outward[i] * (WallThickness * 0.5f);
                var along = (centre - door).normalized;
                var across = new Vector2(-along.y, along.x);
                var strip = new List<Vector2> { door - across * half, centre - across * half, centre + across * half, door + across * half };
                if (Polygon.SignedArea(strip) < 0f) strip.Reverse();
                plan.Spine.Add(strip);
            }
        }

        /// <summary>
        /// Tries every side, in a random order, at positions a unit apart, end-on (the landing against the wall) or
        /// side-on (a flight along it): the core and the clear approach at its open end inside the walls, the core off
        /// the spine (the approach may share it), and both clear of the pillars under stilts.
        /// </summary>
        private static bool PlaceCore(BuildingPlan plan, in LevelGenSettings s, ref Pcg32 rng)
        {
            var b = s.Buildings;
            var footprint = plan.Footprint;
            float width = 2f * b.DoorWidth;
            float run = FlightRun(plan.FloorHeight, s);
            float length = CoreLanding + run;
            var inner = Polygon.Inset(footprint, WallThickness * 0.5f - 0.01f);
            if (inner.Count == 0) return false;

            var sides = new List<int>();
            for (int k = 0; k < footprint.Count; k++) sides.Add(k);
            for (int k = sides.Count - 1; k > 0; k--)
            {
                int j = rng.Range(0, k + 1);
                (sides[k], sides[j]) = (sides[j], sides[k]);
            }
            foreach (int side in sides)
            {
                var a = footprint[side];
                var e = footprint[(side + 1) % footprint.Count];
                float sideLength = (e - a).magnitude;
                var along = (e - a) / sideLength;
                var inward = new Vector2(-along.y, along.x);
                float gap = plan.Placement == FirstFloor.Stilts ? StiltsCoreGap : 0f;
                var wall = a + inward * (WallThickness * 0.5f + gap);
                // End-on (the landing against the wall) or side-on (a flight along the wall, either way round).
                var candidates = new List<StairCoreDescription>();
                for (float t = 0f; t <= sideLength; t += 1f)
                {
                    candidates.Add(new StairCoreDescription
                    {
                        Origin = wall + along * t, Along = along, Inward = inward, Width = width, Length = length,
                    });
                    candidates.Add(new StairCoreDescription
                    {
                        Origin = wall + along * t + inward * (width * 0.5f), Along = inward, Inward = along, Width = width, Length = length,
                    });
                    candidates.Add(new StairCoreDescription
                    {
                        Origin = wall + along * t + inward * (width * 0.5f), Along = -inward, Inward = -along, Width = width, Length = length,
                    });
                }
                foreach (var core in candidates)
                {
                    var reserved = CoreRect(core, CoreApproach);
                    bool fits = true;
                    foreach (var p in reserved) fits &= Polygon.Contains(inner, p);
                    if (!fits) continue;
                    // The core keeps off the spine; its approach is clear floor and may share it.
                    var body = CoreRect(core);
                    foreach (var spine in plan.Spine) fits &= Polygon.Area(Polygon.ClipConvex(body, spine)) < 0.01f;
                    if (plan.Placement == FirstFloor.Stilts)
                        foreach (var pillar in Pillars(footprint)) fits &= Polygon.Area(Polygon.ClipConvex(reserved, pillar)) < 0.01f;
                    if (!fits) continue;
                    plan.HasCore = true;
                    plan.Core = core;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Run of one flight of a floor-height climb: half the risers, one riser per cell.</summary>
        public static float FlightRun(float rise, in LevelGenSettings s) =>
            Mathf.Ceil(Risers(rise, s) / 2f) * s.CellSize;

        private static int Risers(float rise, in LevelGenSettings s) =>
            Mathf.CeilToInt(rise / s.TransitionSizes.StairRiserHeight - 1e-4f);

        /// <summary>The core's rectangle from the wall, <paramref name="extra"/> longer at its open end, counter-clockwise.</summary>
        public static List<Vector2> CoreRect(in StairCoreDescription core, float extra = 0f)
        {
            var o = core.Origin;
            float half = core.Width * 0.5f, length = core.Length + extra;
            var rect = new List<Vector2>
            {
                o - core.Along * half, o + core.Along * half,
                o + core.Along * half + core.Inward * length, o - core.Along * half + core.Inward * length,
            };
            if (Polygon.SignedArea(rect) < 0f) rect.Reverse();
            return rect;
        }

        /// <summary>Squares at the footprint's corners, inside it.</summary>
        public static List<List<Vector2>> Pillars(List<Vector2> footprint)
        {
            var pillars = new List<List<Vector2>>();
            float half = PillarSide * 0.5f;
            for (int k = 0; k < footprint.Count; k++)
            {
                var previous = footprint[(k + footprint.Count - 1) % footprint.Count];
                var next = footprint[(k + 1) % footprint.Count];
                var bisector = ((previous - footprint[k]).normalized + (next - footprint[k]).normalized).normalized;
                var c = footprint[k] + bisector * (half * 1.5f + WallThickness);
                pillars.Add(new List<Vector2> { c + new Vector2(-half, -half), c + new Vector2(half, -half), c + new Vector2(half, half), c + new Vector2(-half, half) });
            }
            return pillars;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
