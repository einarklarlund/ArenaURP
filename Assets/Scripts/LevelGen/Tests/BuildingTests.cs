using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class BuildingTests
    {
        /// <summary>A 14 × 14 building at (10..24, 10..24) on the fixture's ground, doors west and east, and a stair core
        /// end-on against the south wall when <paramref name="core"/>.</summary>
        private static (Fixture fixture, BuildingPlan plan) Building(FirstFloor placement, int floors, bool core)
        {
            var fixture = new Fixture().Plot(0, 0f, 0f, Fixture.Size, Fixture.Size);
            var s = fixture.Settings.Sanitized();
            var plan = new BuildingPlan
            {
                Plot = 0, Ground = 0f, Placement = placement, Floors = floors, FloorHeight = s.Buildings.SmallFloorHeight,
                Footprint = new List<Vector2> { new(10f, 10f), new(24f, 10f), new(24f, 24f), new(10f, 24f) },
                FirstFloorY = placement switch
                {
                    FirstFloor.Raised => 1f,
                    FirstFloor.Stilts => s.Buildings.SmallFloorHeight,
                    _ => BuildingStage.GroundFloorRise,
                },
                HasCore = core,
                Core = new StairCoreDescription
                {
                    Origin = new Vector2(17f, 10f + BuildingStage.WallThickness * 0.5f
                        + (placement == FirstFloor.Stilts ? BuildingStage.StiltsCoreGap : 0f)),
                    Along = Vector2.right, Inward = Vector2.up,
                    Width = 2f * s.Buildings.DoorWidth, Length = BuildingStage.CoreLanding + BuildingStage.FlightRun(s.Buildings.SmallFloorHeight, s),
                },
            };
            plan.Entrances.Add(new Vector2(10f, 18f));
            plan.Outward.Add(Vector2.left);
            plan.Entrances.Add(new Vector2(24f, 18f));
            plan.Outward.Add(Vector2.right);
            for (int k = 0; k < floors; k++)
            {
                var floor = new FloorPlan { Footprint = plan.Footprint };
                // Doors at the entrances on a ground-level first floor: west side is index 3, east is index 1.
                if (k == 0 && placement != FirstFloor.Stilts)
                {
                    floor.Openings.Add(new WallOpening { Side = 3, T = 6f, HalfWidth = s.Buildings.DoorWidth * 0.5f, Top = s.Buildings.DoorHeight, Door = true });
                    floor.Openings.Add(new WallOpening { Side = 1, T = 8f, HalfWidth = s.Buildings.DoorWidth * 0.5f, Top = s.Buildings.DoorHeight, Door = true });
                }
                plan.FloorPlans.Add(floor);
            }
            BuildingStage.Emit(fixture.Level, new List<BuildingPlan> { plan }, s);
            return (fixture, plan);
        }

        [Test]
        public void GroundBuilding_IsWalkThrough([Values(FirstFloor.Ground, FirstFloor.Raised)] FirstFloor placement)
        {
            var report = Building(placement, 1, core: false).fixture.Measure();
            var walk = Fixture.Constraint(report, Constraint.EveryBuildingIsWalkThrough);
            Assert.IsTrue(walk.Passed, walk.Detail);
            var stuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsTrue(stuck.Passed, stuck.Detail);
        }

        [Test]
        public void WallAcrossTheBuilding_BreaksWalkThrough()
        {
            var (fixture, _) = Building(FirstFloor.Ground, 1, core: false);
            fixture.Box(PrimitiveRole.Wall, 16.8f, 10f, 17.2f, 24f, 0f, 3.5f);
            var walk = Fixture.Constraint(fixture.Measure(), Constraint.EveryBuildingIsWalkThrough);
            Assert.IsFalse(walk.Passed);
            CollectionAssert.AreEqual(new[] { 0 }, walk.Buildings);
        }

        [Test]
        public void StairCore_PutsEveryFloorInR([Values(FirstFloor.Ground, FirstFloor.Raised, FirstFloor.Stilts)] FirstFloor placement)
        {
            int floors = placement == FirstFloor.Stilts ? 1 : 2;
            var report = Building(placement, floors, core: true).fixture.Measure();
            foreach (var c in report.Constraints)
                if (c.Constraint is Constraint.NoOneGetsStuck or Constraint.EveryBuildingIsWalkThrough or Constraint.HeadroomAndSizes)
                    Assert.IsTrue(c.Passed, $"{c.Constraint}: {c.Detail}");

            var map = report.Debug.Buildings;
            Assert.AreEqual(floors, map.FloorRegions[0].Length);
            foreach (var region in map.FloorRegions[0])
            {
                Assert.Greater(region.Count, 50);
                Assert.IsTrue(report.Debug.Reachability.InR[region[0]]);
            }
        }

        [Test]
        public void UpperFloorWithoutACore_IsUnreachableAndBlamesTheBuilding()
        {
            var stuck = Fixture.Constraint(Building(FirstFloor.Ground, 2, core: false).fixture.Measure(), Constraint.NoOneGetsStuck);
            Assert.IsFalse(stuck.Passed);
            StringAssert.Contains("building 0 floor 1", stuck.Detail);
            CollectionAssert.Contains(stuck.Buildings, 0);
        }

        [Test]
        public void GeneratedBuildings_HaveEntrancesCoresAndPassTheirChecks()
        {
            var s = LevelGenSettings.Default;
            s.MaxHeight = 16f;
            int buildings = 0, cores = 0;
            for (ulong seed = 0; seed < 12; seed++)
            {
                var trace = new GenerationTrace();
                var result = LevelGenerator.Generate(s, seed, trace);
                Assert.IsTrue(result.Success, result.Reason);
                foreach (var b in result.Level.Buildings)
                {
                    buildings++;
                    if (b.HasCore) cores++;
                    Assert.GreaterOrEqual(b.Entrances.Count, 2);
                    Assert.IsTrue(Polygon.IsConvex(result.Level.Polygons[b.Footprint]));
                    if (b.Floors > 1 || b.Placement == FirstFloor.Stilts) Assert.IsTrue(b.HasCore, "every upper level has a way up");
                }
                foreach (var plan in trace.Buildings) Assert.IsNull(plan.Dropped, "no building is left out of the mix");
            }
            Assert.Greater(buildings, 12);
            Assert.Greater(cores, 0);
        }

        [Test]
        public void Window_IsAJumpEdgeBothWays()
        {
            var (fixture, plan) = Building(FirstFloor.Ground, 1, core: false);
            fixture.Level.Primitives.RemoveAll(p => p.OwnerKind == OwnerKind.Building);
            fixture.Level.Buildings.Clear();
            var s = fixture.Settings.Sanitized();
            // A window 2 wide in the south wall (side 0, z = 10) centred at x = 14.
            plan.FloorPlans[0].Openings.Add(new WallOpening
            {
                Side = 0, T = 4f, HalfWidth = 1f, Bottom = s.Buildings.WindowSill, Top = s.Buildings.WindowSill + s.Buildings.WindowHeight,
            });
            BuildingStage.Emit(fixture.Level, new List<BuildingPlan> { plan }, s);
            var report = fixture.Measure();
            // The surfaces at floor and ground height (the roof is above the inside cell).
            int inside = report.Debug.Surfaces.FindAt(28, 21, BuildingStage.GroundFloorRise);
            int outside = report.Debug.Surfaces.FindAt(28, 18, 0f);
            Assert.GreaterOrEqual(inside, 0);
            Assert.GreaterOrEqual(outside, 0);

            var kinds = new HashSet<MoveKind>();
            foreach (var e in report.Debug.Graph.Edges)
                if (e.From == inside && report.Debug.Surfaces[e.To].Z == report.Debug.Surfaces[inside].Z - 1) kinds.Add(e.Kind);
            CollectionAssert.Contains(kinds, MoveKind.Jump, "onto the sill from inside");
            var distance = new float[report.Debug.Surfaces.Count];
            report.Debug.Graph.Distances(inside, distance);
            Assert.Less(distance[outside], 2f, "out through the window, not round by a door");
            report.Debug.Graph.Distances(outside, distance);
            Assert.Less(distance[inside], 2f, "and back in");
        }

        [Test]
        public void ExteriorFlight_PutsTheSecondFloorInR()
        {
            var (fixture, plan) = Building(FirstFloor.Ground, 2, core: false);
            fixture.Level.Primitives.RemoveAll(p => p.OwnerKind == OwnerKind.Building);
            fixture.Level.Buildings.Clear();
            var s = fixture.Settings.Sanitized();
            float top = plan.FloorY(1);
            int risers = Mathf.CeilToInt(top / s.TransitionSizes.StairRiserHeight - 1e-4f);
            // Along the south wall, outside it, climbing east to a landing at x = 19..21 and a door at x = 20.
            float face = 10f - BuildingStage.WallThickness * 0.5f;
            plan.Flight = new ExteriorFlightPlan
            {
                Side = 0, Low = new Vector2(10.5f, face - 1f), Uphill = Vector2.right, Width = 2f, Run = risers * s.CellSize,
                Risers = risers, BottomY = 0f, TopY = top,
                Landing = new List<Vector2> { new(10.5f + risers * s.CellSize, face - 2f), new(12.5f + risers * s.CellSize, face - 2f), new(12.5f + risers * s.CellSize, face), new(10.5f + risers * s.CellSize, face) },
            };
            float doorX = 11.5f + risers * s.CellSize;
            plan.FloorPlans[1].Openings.Add(new WallOpening { Side = 0, T = doorX - 10f, HalfWidth = 1f, Top = s.Buildings.DoorHeight, Door = true });
            plan.Balconies.Add(new BalconyPlan { Floor = 1, Landing = true, Polygon = plan.Flight.Landing, Door = new Vector2(doorX, 10f), Outward = Vector2.down });
            BuildingStage.Emit(fixture.Level, new List<BuildingPlan> { plan }, s);

            var report = fixture.Measure();
            var stuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsTrue(stuck.Passed, stuck.Detail);
            Assert.IsTrue(Fixture.Constraint(report, Constraint.HeadroomAndSizes).Passed);
        }

        [Test]
        public void ReachableRoof_IsInRAndRailed()
        {
            var (fixture, plan) = Building(FirstFloor.Ground, 1, core: true);
            fixture.Level.Primitives.RemoveAll(p => p.OwnerKind == OwnerKind.Building);
            fixture.Level.Buildings.Clear();
            plan.RoofReachable = true;
            BuildingStage.Emit(fixture.Level, new List<BuildingPlan> { plan }, fixture.Settings.Sanitized());
            var report = fixture.Measure();
            var stuck = Fixture.Constraint(report, Constraint.NoOneGetsStuck);
            Assert.IsTrue(stuck.Passed, stuck.Detail);
            var roof = report.Debug.Buildings.RoofRegions[0];
            Assert.Greater(roof.Count, 50);
            Assert.IsTrue(report.Debug.Reachability.InR[roof[0]]);
            Assert.IsTrue(fixture.Level.Primitives.Exists(p => p.Role == PrimitiveRole.Railing), "the roof has a parapet");
        }

        [Test]
        public void UpperFloors_AreTheFloorBelowScaled_MostlySmaller()
        {
            var s = LevelGenSettings.Default;
            s.MaxHeight = 20f;
            s.Buildings.MinFloors = 3;
            s.Buildings.MaxFloors = 3;
            s.Width = s.Depth = 100f;
            s.PlotCount = 8;
            var b = s.Sanitized().Buildings;
            int smaller = 0, bigger = 0;
            for (ulong seed = 0; seed < 10; seed++)
            {
                var trace = new GenerationTrace();
                Assert.IsTrue(LevelGenerator.Generate(s, seed, trace).Success);
                foreach (var plan in trace.Buildings)
                {
                    if (plan.Dropped != null) continue;
                    for (int k = 1; k < plan.FloorPlans.Count; k++)
                    {
                        var below = plan.FloorPlans[k - 1].Footprint;
                        var floor = plan.FloorPlans[k];
                        Assert.AreEqual(below.Count, floor.Footprint.Count, "same shape");
                        for (int v = 0; v < below.Count; v++)
                        {
                            var expected = plan.Anchor + (below[v] - plan.Anchor) * floor.Scale;
                            Assert.Less((floor.Footprint[v] - expected).magnitude, 1e-3f, "scaled about the anchor");
                        }
                        float change = Mathf.Abs(floor.Scale - 1f);
                        if (change > 0f) Assert.That(change, Is.InRange(b.FloorResizeMin * 0.25f - 1e-4f, b.FloorResizeMax + 1e-4f));
                        if (floor.Scale < 1f) smaller++;
                        if (floor.Scale > 1f) bigger++;
                    }
                }
            }
            Assert.Greater(smaller, bigger, "floors are mostly smaller than the one below");
            Assert.Greater(bigger, 0, "and sometimes bigger");
        }

        [Test]
        public void GeneratedBuildings_HaveFloorPlansWindowsAndOutdoorSpaces()
        {
            var s = LevelGenSettings.Default;
            s.MaxHeight = 20f;
            s.Width = s.Depth = 100f;
            s.PlotCount = 8;
            int walls = 0, windows = 0, outdoor = 0, roofs = 0;
            for (ulong seed = 0; seed < 8; seed++)
            {
                var result = LevelGenerator.Generate(s, seed);
                Assert.IsTrue(result.Success, result.Reason);
                var check = LevelEvaluator.Validate(result.Level, s);
                Assert.AreEqual(0, check.Failures);
                foreach (var building in result.Level.Buildings)
                {
                    outdoor += building.Balconies.Count;
                    if (building.RoofReachable) roofs++;
                }
                foreach (var p in result.Level.Primitives)
                    if (p.OwnerKind == OwnerKind.Building && p.Role == PrimitiveRole.Wall && p.Size.x < BuildingStage.WallThickness) walls++;
                var trace = new GenerationTrace();
                LevelGenerator.Generate(s, seed, trace);
                foreach (var plan in trace.Buildings)
                    if (plan.Dropped == null)
                        foreach (var floor in plan.FloorPlans) windows += floor.Openings.FindAll(o => !o.Door).Count;
            }
            Assert.Greater(walls, 0, "interior walls");
            Assert.Greater(windows, 0);
            Assert.Greater(outdoor, 0, "balconies, terraces or landings");
            Assert.Greater(roofs, 0, "a reachable roof");
        }
    
        // ---------------------------------------------------------------- round 2: mix, classes, rooms

        /// <summary>The 14 × 14 building cut by chords at x = 15 and x = 19 into a west room (west door), a middle room
        /// raised a metre, and an east room (east door), with gaps across the spine at z = 18 drawn as
        /// <paramref name="kind"/>, and the flights between them.</summary>
        private static (Fixture fixture, BuildingPlan plan) RoomsBuilding(EdgeKind kind)
        {
            var fixture = new Fixture().Plot(0, 0f, 0f, Fixture.Size, Fixture.Size);
            var s = fixture.Settings.Sanitized();
            var (_, plan) = Building(FirstFloor.Ground, 1, core: false);
            float inner = BuildingStage.WallThickness * 0.5f;
            var floor = plan.FloorPlans[0];
            floor.Rooms.Add(new RoomPlan { Polygon = new List<Vector2> { new(10f + inner, 10f + inner), new(15f, 10f + inner), new(15f, 24f - inner), new(10f + inner, 24f - inner) }, Fixed = true });
            floor.Rooms.Add(new RoomPlan { Polygon = new List<Vector2> { new(15f, 10f + inner), new(19f, 10f + inner), new(19f, 24f - inner), new(15f, 24f - inner) }, Offset = 1f });
            floor.Rooms.Add(new RoomPlan { Polygon = new List<Vector2> { new(19f, 10f + inner), new(24f - inner, 10f + inner), new(24f - inner, 24f - inner), new(19f, 24f - inner) }, Fixed = true });
            // Chords run north, so the room to their left is the one to the west.
            float gapFrom = 18f - 1.6f - (10f + inner), gapTo = 18f + 1.6f - (10f + inner);
            foreach (var (x, left, right) in new[] { (15f, 0, 1), (19f, 1, 2) })
            {
                var wall = new InteriorWall { From = new Vector2(x, 10f + inner), To = new Vector2(x, 24f - inner), Kind = kind, Left = left, Right = right };
                wall.Gaps.Add(new Vector2(gapFrom, gapTo));
                floor.Walls.Add(wall);
            }
            BuildingStage.PlanFlights(floor, s);
            BuildingStage.Emit(fixture.Level, new List<BuildingPlan> { plan }, s);
            return (fixture, plan);
        }

        [Test]
        public void RoomsAtDifferentLevels_AreJoinedByFlights([Values(EdgeKind.Wall, EdgeKind.Open, EdgeKind.Railing)] EdgeKind kind)
        {
            var (fixture, plan) = RoomsBuilding(kind);
            Assert.AreEqual(2, fixture.Level.Primitives.FindAll(p => p.Role == PrimitiveRole.Stairs).Count, "one flight per edge");
            foreach (var wall in plan.FloorPlans[0].Walls)
            {
                Assert.AreEqual(1, wall.Flights.Count);
                Assert.AreEqual(4, wall.Flights[0].Risers, "a metre in quarter risers");
            }
            var report = fixture.Measure();
            foreach (var c in report.Constraints)
                if (c.Constraint is Constraint.NoOneGetsStuck or Constraint.EveryBuildingIsWalkThrough or Constraint.HeadroomAndSizes
                        or Constraint.NoOverlap)
                    Assert.IsTrue(c.Passed, $"{kind} {c.Constraint}: {c.Detail}");

            var rooms = report.Debug.Buildings.RoomRegions[0];
            Assert.AreEqual(3, rooms.Length);
            foreach (var region in rooms)
            {
                Assert.Greater(region.Count, 20);
                Assert.IsTrue(report.Debug.Reachability.InR[region[0]]);
            }
            Assert.AreEqual(BuildingStage.GroundFloorRise + 1f, report.Debug.Surfaces[rooms[1][0]].Height, 1e-3f, "the middle room is a metre up");
            Assert.AreEqual(kind == EdgeKind.Wall ? 2 : 0, fixture.Level.Buildings[0].Walls.Count, "only wall edges are walls");
            Assert.AreEqual(2, fixture.Level.Buildings[0].Edges.Count);
            Assert.AreEqual(kind == EdgeKind.Railing, fixture.Level.Primitives.Exists(p => p.Role == PrimitiveRole.Railing));
        }

        [Test]
        public void ChooseMix_GivesBigBuildingsTheLargestPlots()
        {
            var s = LevelGenSettings.Default.Sanitized();
            List<Vector2> Square(float x, float side) => new() { new(x, 0f), new(x + side, 0f), new(x + side, side), new(x, side) };
            // Two plots big enough for a big building (the 30 one largest), two only for a small one, one for neither.
            var polygons = new List<List<Vector2>> { Square(0f, 20f), Square(30f, 14f), Square(50f, 30f), Square(90f, 14f), Square(110f, 6f) };
            Assert.IsTrue(BuildingStage.Fits(polygons[2], BuildingClass.Big, s));
            Assert.IsFalse(BuildingStage.Fits(polygons[1], BuildingClass.Big, s));
            Assert.IsTrue(BuildingStage.Fits(polygons[1], BuildingClass.Small, s));
            Assert.IsFalse(BuildingStage.Fits(polygons[4], BuildingClass.Small, s));

            var seen = new HashSet<BuildingMix>();
            for (ulong seed = 0; seed < 32; seed++)
            {
                var rng = SeedTree.Stream(seed, GenStage.Buildings);
                var plots = new List<int>();
                var classes = new List<BuildingClass>();
                var mix = BuildingStage.ChooseMix(polygons, s, ref rng, plots, classes);
                seen.Add(mix);
                BuildingStage.MixCounts(mix, out int big, out int small);
                Assert.AreEqual(big + small, plots.Count);
                CollectionAssert.AllItemsAreUnique(plots);
                CollectionAssert.DoesNotContain(plots, 4);
                if (big >= 1) Assert.AreEqual(2, plots[0], "the largest eligible plot is big");
                if (big == 2) Assert.AreEqual(0, plots[1]);
                for (int i = 0; i < plots.Count; i++)
                    Assert.IsTrue(BuildingStage.Fits(polygons[plots[i]], classes[i], s));
            }
            CollectionAssert.AreEquivalent(new[] { BuildingMix.OneBigTwoSmall, BuildingMix.TwoBig, BuildingMix.FourSmall }, seen);

            // With only three eligible plots, four small buildings can't be drawn.
            var three = polygons.GetRange(0, 3);
            for (ulong seed = 0; seed < 16; seed++)
            {
                var rng = SeedTree.Stream(seed, GenStage.Buildings);
                Assert.AreNotEqual(BuildingMix.FourSmall, BuildingStage.ChooseMix(three, s, ref rng, new List<int>(), new List<BuildingClass>()));
            }
        }

        [Test]
        public void GeneratedLevels_BuildTheirMixOfBigAndSmallBuildings()
        {
            var s = LevelGenSettings.Default;
            var b = s.Sanitized().Buildings;
            var mixes = new HashSet<BuildingMix>();
            for (ulong seed = 0; seed < 12; seed++)
            {
                var trace = new GenerationTrace();
                var result = LevelGenerator.Generate(s, seed, trace);
                Assert.IsTrue(result.Success, result.Reason);
                var level = result.Level;
                Assert.AreNotEqual(BuildingMix.None, level.Mix);
                mixes.Add(level.Mix);
                Assert.IsTrue(Validator.BuildingMixMatches(level).Passed);
                BuildingStage.MixCounts(level.Mix, out int big, out int small);
                Assert.AreEqual(big + small, level.Buildings.Count);
                foreach (var plan in trace.Buildings)
                {
                    Assert.IsNull(plan.Dropped);
                    if (plan.Class == BuildingClass.Big)
                    {
                        Assert.AreEqual(b.BigFloorHeight, plan.FloorHeight);
                        Assert.That(plan.Floors, Is.InRange(BuildingStage.MinBigFloors, b.MaxFloors));
                        Assert.IsTrue(plan.Balconies.Exists(x => x.Floor >= 1 && !x.Landing), "a big building has an upper balcony");
                        if (plan.HasCore) Assert.AreEqual(BuildingStage.CoreLanding + BuildingStage.FlightRun(b.BigFloorHeight, s.Sanitized()), plan.Core.Length, 1e-4f);
                    }
                    else
                    {
                        Assert.AreEqual(b.SmallFloorHeight, plan.FloorHeight);
                        Assert.AreEqual(1, plan.Floors);
                        if (plan.RoofReachable) Assert.IsTrue(plan.RoofByFlight, "a small roof is reached by an exterior flight");
                    }
                    bool porch = plan.Placement == FirstFloor.Stilts
                        ? plan.Balconies.Exists(x => x.Floor == 0 && !x.Landing && !x.Terrace)
                        : plan.Porches.Count > 0;
                    Assert.IsTrue(porch, "every building has a porch");
                }
            }
            Assert.Greater(mixes.Count, 1, "more than one mix is drawn");
        }

        [Test]
        public void GeneratedRooms_KeepTheirMinimumsAndOffsetRules()
        {
            var s = LevelGenSettings.Default;
            s.Width = s.Depth = 100f;
            s.PlotCount = 8;
            var settings = s.Sanitized();
            var b = settings.Buildings;
            float riser = settings.TransitionSizes.StairRiserHeight;
            int raised = 0, lowered = 0, flights = 0;
            var kinds = new HashSet<EdgeKind>();
            for (ulong seed = 0; seed < 10; seed++)
            {
                var trace = new GenerationTrace();
                Assert.IsTrue(LevelGenerator.Generate(s, seed, trace).Success);
                foreach (var plan in trace.Buildings)
                    for (int k = 0; k < plan.Floors; k++)
                    {
                        var floor = plan.FloorPlans[k];
                        Assert.Greater(floor.Rooms.Count, 0);
                        foreach (var room in floor.Rooms)
                        {
                            if (floor.Rooms.Count > 1)
                            {
                                Assert.GreaterOrEqual(Polygon.Area(room.Polygon), b.MinRoomArea - 1e-3f);
                                Assert.GreaterOrEqual(Polygon.MinWidth(room.Polygon), b.MinRoomWidth - 1e-3f);
                            }
                            float steps = room.Offset / riser;
                            Assert.AreEqual(Mathf.Round(steps), steps, 1e-3f, "whole risers");
                            if (room.Offset != 0f)
                                Assert.That(Mathf.Abs(room.Offset), Is.InRange(riser - 1e-4f, b.MaxRoomOffset + 1e-4f));
                            if (room.Fixed) Assert.AreEqual(0f, room.Offset, "door and core rooms keep the floor's level");
                            if (k == 0) Assert.GreaterOrEqual(room.Offset, 0f, "a first floor only raises its rooms");
                            if (room.Offset > 0f) raised++;
                            if (room.Offset < 0f) lowered++;
                        }
                        foreach (var e in floor.Walls)
                        {
                            kinds.Add(e.Kind);
                            bool differ = floor.Rooms[e.Left].Offset != floor.Rooms[e.Right].Offset;
                            Assert.AreEqual(differ ? e.Gaps.Count : 0, e.Flights.Count, "a flight fills each gap between levels");
                            flights += e.Flights.Count;
                        }
                    }
            }
            Assert.Greater(raised, 0);
            Assert.Greater(flights, 0);
            Assert.AreEqual(3, kinds.Count, "walls, open edges and railings");
            TestContext.WriteLine($"{raised} raised and {lowered} lowered rooms, {flights} flights");
        }
    }
}
