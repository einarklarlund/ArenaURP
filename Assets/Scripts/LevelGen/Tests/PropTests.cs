using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PropTests
    {
        [Test]
        public void Props_StayOnTheirLeaf_OutOfKeepOutZones_AndApart([Values(0, 1, 2, 3, 4)] int seed)
        {
            var s = LevelGenSettings.Default;
            s.Props.Density = 8f;
            var trace = new GenerationTrace();
            Assert.IsTrue(LevelGenerator.Generate(s, (ulong)seed, trace).Success);
            var sanitized = s.Sanitized();
            var zones = PropStage.KeepOut(trace.Partition, trace.Plots, trace.Buildings, sanitized);
            var gates = PropStage.KeepOut(trace.Partition, trace.Plots, trace.Buildings, sanitized, gates: true);
            // Outdoors: the interior props and fixtures stand in buildings (see InteriorTests).
            var outdoor = trace.Props.FindAll(p => p.Kind is PropKind.Fence or PropKind.Car or PropKind.Tree);
            Assert.Greater(outdoor.Count, 0);
            for (int i = 0; i < outdoor.Count; i++)
            {
                var prop = outdoor[i];
                foreach (var v in prop.Footprint) Assert.IsTrue(Polygon.Contains(trace.Partition.Polygons[prop.Leaf], v), "on its leaf");
                foreach (var zone in prop.Kind == PropKind.Fence ? gates : zones)
                    Assert.Less(Polygon.Area(Polygon.ClipConvex(prop.Footprint, zone)), 1e-3f, $"{prop.Kind} in a keep-out zone");
                for (int j = i + 1; j < outdoor.Count; j++)
                {
                    // Fence runs meeting at a room's corner may cross; everything else keeps apart.
                    if (prop.Kind == PropKind.Fence && outdoor[j].Kind == PropKind.Fence) continue;
                    Assert.Less(Polygon.Area(Polygon.ClipConvex(prop.Footprint, outdoor[j].Footprint)), 1e-3f, "props apart");
                }
            }
        }

        [Test]
        public void Density_ScalesTheCarsAndTrees()
        {
            int Count(float density)
            {
                var s = LevelGenSettings.Default;
                s.Props.Density = density;
                s.Props.BoundaryCarOdds = 0f;
                int total = 0;
                for (ulong seed = 0; seed < 4; seed++)
                {
                    var trace = new GenerationTrace();
                    Assert.IsTrue(LevelGenerator.Generate(s, seed, trace).Success);
                    total += trace.Props.FindAll(p => p.Kind is PropKind.Car or PropKind.Tree).Count;
                }
                return total;
            }
            Assert.AreEqual(0, Count(0f));
            Assert.Less(Count(2f), Count(8f));
        }

        [Test]
        public void EveryKind_Appears()
        {
            var s = LevelGenSettings.Default;
            s.Props.Density = 8f;
            var kinds = new System.Collections.Generic.HashSet<PropKind>();
            for (ulong seed = 0; seed < 6; seed++)
            {
                var trace = new GenerationTrace();
                LevelGenerator.Generate(s, seed, trace);
                foreach (var prop in trace.Props) kinds.Add(prop.Kind);
            }
            CollectionAssert.AreEquivalent(PropKinds.All, kinds);
        }

        private static GenerationTrace Generate(ulong seed, System.Func<LevelGenSettings, LevelGenSettings> tweak)
        {
            var s = tweak(LevelGenSettings.Default);
            var trace = new GenerationTrace();
            Assert.IsTrue(LevelGenerator.Generate(s, seed, trace).Success);
            return trace;
        }

        [Test]
        public void Fences_LineRoomSides_AtTheMargin([Values(0, 1, 2, 3)] int seed)
        {
            var trace = Generate((ulong)seed, t =>
            {
                t.Props.FencedRoomOdds = 1f;
                t.Props.BuildingFenceOdds = 0f;
                t.Props.SinkholeFenceOdds = 0f;
                return t;
            });
            var s = LevelGenSettings.Default.Sanitized();
            var fences = trace.Props.FindAll(p => p.Kind == PropKind.Fence);
            Assert.Greater(fences.Count, 0);
            foreach (var fence in fences)
            {
                // Parallel to one of its room's sides, the margin in from it.
                var room = trace.Partition.Polygons[fence.Leaf];
                bool found = false;
                for (int k = 0; k < room.Count && !found; k++)
                {
                    var a = room[k];
                    var dir = (room[(k + 1) % room.Count] - a).normalized;
                    if (Mathf.Abs(dir.x * fence.Along.y - dir.y * fence.Along.x) > 1e-3f) continue;
                    float distance = Mathf.Abs(dir.x * (fence.Centre - a).y - dir.y * (fence.Centre - a).x);
                    found = Mathf.Abs(distance - s.Props.FenceMargin) < 1e-2f;
                }
                Assert.IsTrue(found, $"fence at {fence.Centre} lines a side of room {fence.Leaf}");
            }
        }

        [Test]
        public void FencedRoomOdds_DecidesWhichRoomsHaveFences()
        {
            int withFences = 0, rooms = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                var none = Generate(seed, Quiet);
                Assert.AreEqual(0, none.Props.FindAll(p => p.Kind == PropKind.Fence).Count);

                var half = Generate(seed, t =>
                {
                    t = Quiet(t);
                    t.Props.FencedRoomOdds = 0.5f;
                    return t;
                });
                var fenced = new System.Collections.Generic.HashSet<int>();
                foreach (var p in half.Props)
                    if (p.Kind == PropKind.Fence) fenced.Add(p.Leaf);
                withFences += fenced.Count;
                rooms += half.Partition.Polygons.Count;
            }
            Assert.Greater(withFences, 0, "some rooms have fences");
            Assert.Less(withFences, rooms, "and some don't");
        }

        [Test]
        public void OldFences_HaveSmallGaps()
        {
            var s = LevelGenSettings.Default.Sanitized();
            int smallGaps = 0;
            for (ulong seed = 0; seed < 4; seed++)
            {
                var trace = Generate(seed, t => { t.Props.FencedRoomOdds = 1f; t.Props.OldFenceOdds = 1f; return t; });
                var fences = trace.Props.FindAll(p => p.Kind == PropKind.Fence);
                foreach (var a in fences)
                    foreach (var b in fences)
                    {
                        if (a == b || a.Leaf != b.Leaf || Vector2.Dot(a.Along, b.Along) < 0.9999f) continue;
                        // On one line, b after a: the gap between a's end and b's start.
                        var offset = b.Centre - a.Centre;
                        if (Mathf.Abs(a.Along.x * offset.y - a.Along.y * offset.x) > 1e-2f) continue;
                        float gap = Vector2.Dot(offset, a.Along) - (a.Length + b.Length) * 0.5f;
                        if (gap > 0.05f && gap <= s.Props.MaxOldGapWidth + 1e-3f) smallGaps++;
                    }
            }
            Assert.Greater(smallGaps, 0);
        }

        /// <summary>No fences, cars or trees, so a test can turn on just the ones it checks.</summary>
        private static LevelGenSettings Quiet(LevelGenSettings t)
        {
            t.Props.Density = 0f;
            t.Props.FencedRoomOdds = 0f;
            t.Props.EnclosedRoomOdds = 0f;
            t.Props.OldFenceOdds = 0f;
            t.Props.BuildingFenceOdds = 0f;
            t.Props.SinkholeFenceOdds = 0f;
            t.Props.BoundaryCarOdds = 0f;
            return t;
        }

        /// <summary>The distance from a point to a polygon's outline.</summary>
        private static float ToOutline(List<Vector2> polygon, Vector2 p)
        {
            float best = float.PositiveInfinity;
            for (int k = 0; k < polygon.Count; k++) best = Mathf.Min(best, ToSegment(polygon[k], polygon[(k + 1) % polygon.Count], p));
            return best;
        }

        private static float ToSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
            return (a + ab * t - p).magnitude;
        }

        private static GroundSurface Ground(GenerationTrace trace)
        {
            var terrains = new Dictionary<int, TerrainPlan>();
            foreach (var t in trace.Terrain) terrains[t.Plot] = t;
            return new GroundSurface(trace.Plots, terrains);
        }

        [Test]
        public void BuildingFences_RingInscribedBuildings_OutsideTheirPorch([Values(0, 1, 2, 3, 4, 5)] int seed)
        {
            var trace = Generate((ulong)seed, t =>
            {
                t = Quiet(t);
                t.Props.BuildingFenceOdds = 1f;
                return t;
            });
            var s = LevelGenSettings.Default.Sanitized();
            var fences = trace.Props.FindAll(p => p.Kind == PropKind.Fence);
            foreach (var building in trace.Buildings)
            {
                if (building.Dropped != null) continue;
                var own = fences.FindAll(f => f.Leaf == building.Plot);
                if (building.FullPlot)
                {
                    Assert.AreEqual(0, own.Count, "a full-plot building has no ring");
                    continue;
                }
                var porch = PropStage.Porch(building);
                var ring = Polygon.ClipConvex(Polygon.Outset(porch, s.Props.BuildingFenceMargin),
                    Polygon.Inset(trace.Partition.Polygons[building.Plot], s.Props.FenceMargin));
                foreach (var fence in own)
                {
                    Assert.Less(ToOutline(ring, fence.Centre), 1e-2f, $"fence at {fence.Centre} stands on the ring");
                    // A ring cut back to the plot's inset can come closer than the margin, but never onto the porch.
                    Assert.Less(Polygon.Area(Polygon.ClipConvex(fence.Footprint, porch)), 1e-4f, "outside the porch");
                }

                // A gate where each entrance's outward line crosses the ring: no piece near that point.
                for (int e = 0; e < building.Entrances.Count && own.Count > 0; e++)
                {
                    var from = building.Entrances[e];
                    var outward = building.Outward[e];
                    float exit = 0f;
                    while (exit < 100f && Polygon.Contains(ring, from + outward * (exit + 0.01f))) exit += 0.01f;
                    var gate = from + outward * exit;
                    foreach (var fence in own)
                    {
                        var half = fence.Along * (fence.Length * 0.5f);
                        // The gate is spineWidth + 2 along the ring; an acute corner near it brings a piece closer.
                        Assert.Greater(ToSegment(fence.Centre - half, fence.Centre + half, gate), 1f,
                            $"entrance {e}'s gate at {gate} is open");
                    }
                }
            }
        }

        [Test]
        public void BuildingFences_Appear()
        {
            int rings = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                var trace = Generate(seed, t =>
                {
                    t = Quiet(t);
                    t.Props.BuildingFenceOdds = 1f;
                    return t;
                });
                foreach (var building in trace.Buildings)
                    if (building.Dropped == null && !building.FullPlot &&
                        trace.Props.Exists(p => p.Kind == PropKind.Fence && p.Leaf == building.Plot))
                        rings++;
            }
            Assert.Greater(rings, 0);
        }

        [Test]
        public void EnclosedRooms_AreFencedOnAllSides()
        {
            // Per room, the directions its fences run in: one per fenced side.
            List<int> Directions(GenerationTrace trace)
            {
                var counts = new List<int>();
                var built = new HashSet<int>();
                foreach (var b in trace.Buildings) built.Add(b.Plot);
                for (int room = 0; room < trace.Partition.Polygons.Count; room++)
                {
                    if (built.Contains(room)) continue;
                    var directions = new List<Vector2>();
                    foreach (var p in trace.Props)
                        if (p.Kind == PropKind.Fence && p.Leaf == room &&
                            !directions.Exists(d => Mathf.Abs(d.x * p.Along.y - d.y * p.Along.x) < 1e-3f))
                            directions.Add(p.Along);
                    counts.Add(directions.Count);
                }
                return counts;
            }
            int enclosed = 0, partial = 0, threeOrMore = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                foreach (int n in Directions(Generate(seed, t =>
                         {
                             t = Quiet(t);
                             t.Props.EnclosedRoomOdds = 1f;
                             return t;
                         })))
                {
                    enclosed += n;
                    if (n >= 3) threeOrMore++;
                }
                foreach (int n in Directions(Generate(seed, t =>
                         {
                             t = Quiet(t);
                             t.Props.FencedRoomOdds = 1f;
                             t.Props.MinFencedSides = 1;
                             t.Props.MaxFencedSides = 1;
                             return t;
                         })))
                {
                    Assert.LessOrEqual(n, 1, "a partial fence on one side");
                    partial += n;
                }
            }
            Assert.Greater(enclosed, partial);
            Assert.Greater(threeOrMore, 0, "enclosed rooms are fenced round");
        }

        /// <summary>Plenty of sinkholes, every one ringed, and no other props.</summary>
        private static LevelGenSettings SinkholeRings(LevelGenSettings t)
        {
            t = Quiet(t);
            t.Terrain.SinkholeWeight = 4f;
            t.Props.SinkholeFenceOdds = 1f;
            return t;
        }

        [Test]
        public void SinkholeFences_RingTheRim_WithGaps([Values(0, 1, 2, 3, 4, 5)] int seed)
        {
            var trace = Generate((ulong)seed, SinkholeRings);
            var s = SinkholeRings(LevelGenSettings.Default).Sanitized();
            // The stage itself, given the terrain (the ring needs the sinkholes).
            var props = PropStage.Build(trace.Partition, trace.Plots, trace.Buildings, Ground(trace), s, (ulong)seed,
                trace.Terrain);
            var fences = props.FindAll(p => p.Kind == PropKind.Fence);
            foreach (var terrain in trace.Terrain)
                foreach (var cell in terrain.Cells)
                {
                    if (!cell.Sinkhole) continue;
                    var rim = new List<Vector2>(cell.Polygon);
                    if (Polygon.SignedArea(rim) < 0f) rim.Reverse();
                    var ring = Polygon.Outset(rim, s.Props.SinkholeFenceMargin);
                    var inside = Polygon.Outset(rim, s.Props.SinkholeFenceMargin - 0.06f);
                    float perimeter = 0f;
                    for (int k = 0; k < ring.Count; k++) perimeter += (ring[(k + 1) % ring.Count] - ring[k]).magnitude;
                    float length = 0f;
                    foreach (var fence in fences)
                    {
                        if (fence.Leaf != terrain.Plot || ToOutline(ring, fence.Centre) > 1e-2f) continue;
                        Assert.Less(Polygon.Area(Polygon.ClipConvex(fence.Footprint, inside)), 1e-4f, "outside the rim");
                        length += fence.Length;
                    }
                    // Corners add a little: a butt joint runs on past the corner by part of the fence's thickness.
                    Assert.LessOrEqual(length, perimeter - s.Props.MinSinkholeGaps * s.Buildings.DoorWidth + 0.5f,
                        "the ring has its gaps");
                }
        }

        [Test]
        public void SinkholeFences_Appear()
        {
            int rings = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                var trace = Generate(seed, SinkholeRings);
                var s = SinkholeRings(LevelGenSettings.Default).Sanitized();
                rings += PropStage.Build(trace.Partition, trace.Plots, trace.Buildings, Ground(trace), s, seed, trace.Terrain)
                    .FindAll(p => p.Kind == PropKind.Fence).Count;
            }
            Assert.Greater(rings, 0);
        }

        [Test]
        public void BoundaryCars_ParkInRowsAlongTheWalls([Values(0, 1, 2, 3)] int seed)
        {
            var trace = Generate((ulong)seed, t =>
            {
                t = Quiet(t);
                t.Props.BoundaryCarOdds = 1f;
                t.Props.CarSkipOdds = 0f;
                return t;
            });
            var s = LevelGenSettings.Default.Sanitized();
            var ground = Ground(trace);
            // Cars need level ground under their whole footprint, so some levels have none (see BoundaryCars_Appear).
            var cars = trace.Props.FindAll(p => p.Kind == PropKind.Car);
            var clear = new List<List<Vector2>>();
            foreach (var building in trace.Buildings)
                if (building.Dropped == null)
                    clear.Add(Polygon.Outset(PropStage.Porch(building), s.Buildings.SpineWidth - 0.01f));
            foreach (var car in cars)
            {
                PlotEdge wall = default;
                bool found = false;
                foreach (var edge in trace.Partition.Outline)
                {
                    if (edge.A != car.Leaf) continue;
                    var along = (edge.To - edge.From).normalized;
                    if (Vector2.Dot(along, car.Along) < 0.9999f) continue;
                    var offset = car.Centre - edge.From;
                    float off = along.x * offset.y - along.y * offset.x;
                    if (Mathf.Abs(off - (s.Props.CarWallGap + car.Width * 0.5f)) > 1e-3f) continue;
                    wall = edge;
                    found = true;
                }
                Assert.IsTrue(found, $"car at {car.Centre} is parked along a wall");
                var dir = (wall.To - wall.From).normalized;
                float t = Vector2.Dot(car.Centre - wall.From, dir);
                Assert.GreaterOrEqual(t - car.Length * 0.5f, PropStage.CarRowEndGap - 1e-3f, "short of the wall's start");
                Assert.LessOrEqual(t + car.Length * 0.5f, (wall.To - wall.From).magnitude - PropStage.CarRowEndGap + 1e-3f,
                    "short of the wall's end");
                foreach (var zone in clear)
                    Assert.Less(Polygon.Area(Polygon.ClipConvex(car.Footprint, zone)), 1e-3f, "spine-wide clear of buildings");
                // Level ground under the whole footprint, so it stacks with no gap.
                Assert.IsTrue(ground.Level(car.Leaf, car.Footprint, out float level), "on level ground");
                Assert.AreEqual(level, car.Bottom, 1e-4f, "standing on it");
            }
            // Neighbours in a row keep at least the minimum spacing.
            foreach (var a in cars)
                foreach (var b in cars)
                {
                    if (a == b || Vector2.Dot(a.Along, b.Along) < 0.9999f) continue;
                    var offset = b.Centre - a.Centre;
                    if (Mathf.Abs(a.Along.x * offset.y - a.Along.y * offset.x) > 1e-2f) continue;
                    float gap = Vector2.Dot(offset, a.Along) - (a.Length + b.Length) * 0.5f;
                    if (gap > -1e-3f) Assert.GreaterOrEqual(gap, s.Props.MinCarSpacing - 1e-3f);
                }
        }

        [Test]
        public void BoundaryCars_Appear()
        {
            int cars = 0;
            for (ulong seed = 0; seed < 4; seed++)
                cars += Generate(seed, t =>
                {
                    t = Quiet(t);
                    t.Props.BoundaryCarOdds = 1f;
                    t.Props.CarSkipOdds = 0f;
                    return t;
                }).Props.FindAll(p => p.Kind == PropKind.Car).Count;
            Assert.Greater(cars, 0);
        }

        [Test]
        public void CarSkipOdds_LeavesHolesInTheRows()
        {
            int Count(float skip)
            {
                int total = 0;
                for (ulong seed = 0; seed < 4; seed++)
                    total += Generate(seed, t =>
                    {
                        t = Quiet(t);
                        t.Props.BoundaryCarOdds = 1f;
                        t.Props.CarSkipOdds = skip;
                        return t;
                    }).Props.FindAll(p => p.Kind == PropKind.Car).Count;
                return total;
            }
            Assert.AreEqual(0, Count(1f));
            Assert.Less(Count(0.5f), Count(0f));
        }
    }
}
