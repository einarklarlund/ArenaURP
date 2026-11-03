using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    /// <summary>Milestone 13: tables, shelves and crates in rooms, light fixtures, and the lights they list.</summary>
    public class InteriorTests
    {
        private static GenerationResult Generate(ulong seed, GenerationTrace trace, System.Func<LevelGenSettings, LevelGenSettings> tweak = null)
        {
            var s = LevelGenSettings.Default;
            if (tweak != null) s = tweak(s);
            var result = LevelGenerator.Generate(s, seed, trace);
            Assert.IsTrue(result.Success, result.Reason);
            return result;
        }

        /// <summary>A room's floor inside its walls, and its walking height.</summary>
        private static List<(BuildingPlan plan, int floor, List<Vector2> region, float y)> Rooms(GenerationTrace trace)
        {
            var rooms = new List<(BuildingPlan, int, List<Vector2>, float)>();
            foreach (var plan in trace.Buildings)
                for (int k = 0; k < plan.Floors; k++)
                {
                    var floor = plan.FloorPlans[k];
                    var inner = Polygon.Inset(floor.Footprint, BuildingStage.WallThickness * 0.5f);
                    if (floor.Rooms.Count == 0) rooms.Add((plan, k, inner, plan.FloorY(k)));
                    for (int r = 0; r < floor.Rooms.Count; r++)
                        rooms.Add((plan, k, Polygon.ClipConvex(BuildingStage.RoomSlab(floor, r), inner), plan.FloorY(k) + floor.Rooms[r].Offset));
                }
            return rooms;
        }

        [Test]
        public void InteriorProps_StandOnTheirRoomsSlab_ClearOfTheCoreAndSpine([Values(0, 1, 2, 3, 4)] int seed)
        {
            var trace = new GenerationTrace();
            Generate((ulong)seed, trace);
            var rooms = Rooms(trace);
            var inside = trace.Props.FindAll(p => PropKinds.IsInterior(p.Kind));
            Assert.Greater(inside.Count, 0);
            foreach (var prop in inside)
            {
                var home = rooms.Find(room =>
                    Mathf.Abs(room.y - prop.Bottom) < 1e-4f && prop.Footprint.TrueForAll(v => Polygon.Contains(room.region, v)));
                Assert.IsNotNull(home.region, $"{prop.Kind} at {prop.Centre} stands in a room, on its slab");
                var plan = home.plan;
                if (plan.HasCore)
                    Assert.Less(Polygon.Area(Polygon.ClipConvex(prop.Footprint, BuildingStage.CoreRect(plan.Core))), 1e-4f, "out of the core");
                if (home.floor == 0 && plan.Placement != FirstFloor.Stilts)
                    foreach (var spine in plan.Spine)
                        Assert.Less(Polygon.Area(Polygon.ClipConvex(prop.Footprint, spine)), 1e-4f, "off the spine");
            }
        }

        [Test]
        public void InteriorPropDensity_ScalesTheProps()
        {
            int Count(float density)
            {
                int total = 0;
                for (ulong seed = 0; seed < 4; seed++)
                {
                    var trace = new GenerationTrace();
                    Generate(seed, trace, t =>
                    {
                        t.Props.InteriorPropDensity = density;
                        return t;
                    });
                    total += trace.Props.FindAll(p => PropKinds.IsInterior(p.Kind)).Count;
                }
                return total;
            }
            Assert.AreEqual(0, Count(0f));
            Assert.Less(Count(0.5f), Count(2f));
        }

        [Test]
        public void Shelves_StandAgainstAWall([Values(0, 1, 2, 3)] int seed)
        {
            var trace = new GenerationTrace();
            Generate((ulong)seed, trace, t =>
            {
                t.Props.InteriorPropDensity = 2f;
                t.Props.TableWeight = t.Props.CrateWeight = 0f;
                return t;
            });
            var shelves = trace.Props.FindAll(p => p.Kind == PropKind.Shelf);
            Assert.Greater(shelves.Count, 0);
            foreach (var shelf in shelves)
            {
                // Its back: behind its front (+X, right of its length).
                var front = new Vector2(shelf.Along.y, -shelf.Along.x);
                var back = shelf.Centre - front * (shelf.Width * 0.5f);
                bool against = false;
                foreach (var plan in trace.Buildings)
                    foreach (var floor in plan.FloorPlans)
                    {
                        var inner = Polygon.Inset(floor.Footprint, BuildingStage.WallThickness * 0.5f);
                        for (int k = 0; k < inner.Count; k++)
                            if (ToSegment(inner[k], inner[(k + 1) % inner.Count], back) < InteriorStage.WallGap + 1e-3f) against = true;
                        foreach (var e in floor.Walls)
                        {
                            if (e.Kind != EdgeKind.Wall) continue;
                            var dir = (e.To - e.From).normalized;
                            float off = Mathf.Abs(dir.x * (back - e.From).y - dir.y * (back - e.From).x);
                            float t = Vector2.Dot(back - e.From, dir);
                            if (t >= 0f && t <= (e.To - e.From).magnitude
                                && Mathf.Abs(off - BuildingStage.InteriorWallThickness * 0.5f - InteriorStage.WallGap) < 1e-3f)
                                against = true;
                        }
                    }
                Assert.IsTrue(against, $"shelf at {shelf.Centre} has its back to a wall");
            }
        }

        private static float ToSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
            return (a + ab * t - p).magnitude;
        }

        [Test]
        public void CeilingLights_HangFlushUnderTheSlabAbove([Values(0, 1, 2, 3)] int seed)
        {
            var trace = new GenerationTrace();
            var level = Generate((ulong)seed, trace).Level;
            int rooms = Rooms(trace).Count;
            var lights = level.Props.FindAll(p => p.Kind == PropKind.CeilingLight);
            Assert.LessOrEqual(lights.Count, rooms, "one per room at most");
            Assert.GreaterOrEqual(lights.Count, rooms / 2, "most rooms have one");
            var shape = PropShape.DefaultCeilingLight;
            foreach (var light in lights)
            {
                float top = light.Position.y + shape.Height;
                var at = new Vector2(light.Position.x, light.Position.z);
                bool flush = level.Primitives.Exists(p => p.OwnerKind == OwnerKind.Building && p.Shape == PrimitiveShape.Prism
                    && Mathf.Abs(p.Position.y - top) < 1e-4f && Polygon.Contains(level.Polygons[p.Polygon], at));
                Assert.IsTrue(flush, $"ceiling light at {light.Position} has a slab's bottom at its top");
            }
        }

        [Test]
        public void Fixtures_HaveNoPrimitives_AndOverlapNothing([Values(0, 1, 2, 3, 4, 5)] int seed)
        {
            var level = Generate((ulong)seed, new GenerationTrace()).Level;
            var fixtures = level.Props.FindAll(p => PropKinds.IsFixture(p.Kind));
            Assert.Greater(fixtures.Count, 0);
            foreach (var fixture in fixtures)
                Assert.IsFalse(level.Primitives.Exists(p => p.OwnerKind == OwnerKind.Prop && p.OwnerId == fixture.Id),
                    "no collider, so no primitives");
            // As solids, they would still overlap nothing: they stack under slabs and stop short at walls.
            foreach (var fixture in fixtures)
            {
                var shape = PropShape.Default(fixture.Kind);
                level.Primitives.Add(new Primitive
                {
                    Role = PrimitiveRole.Cover, Shape = PrimitiveShape.Box, Position = fixture.Position,
                    Size = new Vector3(shape.Width, shape.Height, shape.Length), YawDegrees = fixture.YawDegrees,
                    OwnerKind = OwnerKind.Prop, OwnerId = fixture.Id,
                });
            }
            var check = OverlapCheck.Check(level);
            Assert.IsTrue(check.Passed, check.Detail);
        }

        [Test]
        public void Lights_ListEveryFixture_InPriorityOrder([Values(0, 1, 2)] int seed)
        {
            var level = Generate((ulong)seed, new GenerationTrace()).Level;
            var fixtures = level.Props.FindAll(p => PropKinds.IsFixture(p.Kind));
            Assert.AreEqual(fixtures.Count, level.Lights.Count);
            var s = LevelGenSettings.Default.Sanitized();
            foreach (var light in level.Lights)
            {
                var prop = level.Props[light.Prop];
                Assert.IsTrue(PropKinds.IsFixture(prop.Kind));
                Assert.Greater(light.Range, 0f);
                switch (light.Slot)
                {
                    case LightSlot.Room:
                        Assert.AreEqual(PropKind.CeilingLight, prop.Kind);
                        Assert.Greater(light.Priority, InteriorStage.RoomPriority);
                        Assert.Less(light.Position.y, prop.Position.y, "under its fixture");
                        break;
                    case LightSlot.Entrance:
                        Assert.AreEqual(PropKind.WallLight, prop.Kind);
                        Assert.AreEqual(InteriorStage.EntrancePriority, light.Priority);
                        Assert.AreEqual(s.Props.LampRange, light.Range);
                        break;
                    default:
                        Assert.AreEqual(PropKind.WallLight, prop.Kind);
                        Assert.AreEqual(InteriorStage.PorchPriority, light.Priority);
                        break;
                }
            }
            Assert.IsTrue(level.Lights.Exists(l => l.Slot == LightSlot.Room));
            Assert.IsTrue(level.Lights.Exists(l => l.Slot != LightSlot.Room));
        }

        [Test]
        public void Lights_SurviveSerialization()
        {
            var level = Generate(1, new GenerationTrace()).Level;
            var bytes = level.Serialize();
            var copy = LevelDescription.Deserialize(bytes);
            Assert.AreEqual(level.Lights.Count, copy.Lights.Count);
            for (int i = 0; i < level.Lights.Count; i++)
            {
                Assert.AreEqual(level.Lights[i].Prop, copy.Lights[i].Prop);
                Assert.AreEqual(level.Lights[i].Slot, copy.Lights[i].Slot);
                Assert.AreEqual(level.Lights[i].Priority, copy.Lights[i].Priority);
            }
            CollectionAssert.AreEqual(bytes, copy.Serialize());
        }

        [Test]
        public void WallLamps_StandOnTheOuterFace([Values(0, 1, 2, 3)] int seed)
        {
            var trace = new GenerationTrace();
            Generate((ulong)seed, trace);
            var lamps = trace.Props.FindAll(p => p.Kind == PropKind.WallLight);
            Assert.Greater(lamps.Count, 0);
            foreach (var lamp in lamps)
            {
                var plan = trace.Buildings.Find(b => b.Plot == lamp.Leaf);
                var front = new Vector2(lamp.Along.y, -lamp.Along.x);
                var back = lamp.Centre - front * (lamp.Width * 0.5f);
                var footprint = plan.FloorPlans[0].Footprint;
                // Its back on the wall's outer face, half a wall out from the footprint, facing out.
                var face = Polygon.Outset(footprint, BuildingStage.WallThickness * 0.5f);
                Assert.IsFalse(Polygon.Contains(face, back + front * 0.01f), "outside the wall");
                Assert.IsTrue(Polygon.Contains(Polygon.Outset(face, 0.01f), back), "against the wall");
                Assert.Less(lamp.Bottom + lamp.Height, plan.FloorY(1) - LevelGenSettings.SlabThickness + 1e-4f, "under the floor above");
            }
        }

        [Test]
        public void InteriorProps_KeepLevelsValid_WithoutRepairs([Values(0, 1, 2, 3, 4, 5)] int seed)
        {
            var trace = new GenerationTrace();
            Generate((ulong)seed, trace, t =>
            {
                t.Props.InteriorPropDensity = 2f;
                return t;
            });
            Assert.IsFalse(trace.Repairs.Exists(r => r.Contains("prop(s) removed")), string.Join("; ", trace.Repairs));
        }

        [Test]
        public void Headroom_SaysWhereItFails()
        {
            // A box hanging a metre over the floor: too low to stand under.
            var report = new Fixture()
                .Plot(0, 10, 10, 20, 20)
                .Box(PrimitiveRole.Cover, 14f, 14f, 15f, 15f, 1f, 2f)
                .Measure();
            var headroom = Fixture.Constraint(report, Constraint.HeadroomAndSizes);
            Assert.IsFalse(headroom.Passed);
            Assert.Greater(headroom.Where.Count, 0);
            foreach (var at in headroom.Where)
            {
                Assert.That(at.x, Is.InRange(14f, 15f));
                Assert.That(at.y, Is.InRange(14f, 15f));
            }
        }
    }
}
