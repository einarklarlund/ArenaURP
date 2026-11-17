using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class BuildPlanTests
    {
        private static LevelDescription Generate(ulong seed)
        {
            var result = LevelGenerator.Generate(LevelGenSettings.Default, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        [Test]
        public void RealLights_AreTheHighestPriority_TiesInDescriptionOrder()
        {
            var level = new LevelDescription();
            foreach (float priority in new[] { 1f, 3f, 3f, 2f, 0f })
                level.Lights.Add(new LightDescription { Priority = priority });
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, BuildPlan.Create(level, new BuildOptions { MaxRealLights = 3 }).RealLights);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 0, 4 }, BuildPlan.Create(level, new BuildOptions { MaxRealLights = 9 }).RealLights);
            CollectionAssert.IsEmpty(BuildPlan.Create(level, new BuildOptions { MaxRealLights = 0 }).RealLights);
        }

        [Test]
        public void Fixtures_AreInstancesOrPlaceholders_NeverMeshGroups()
        {
            // Only the first ceiling light variant has a prefab.
            var art = new PropArt();
            art.Set(PropKind.CeilingLight, new[] { PropShape.Default(PropKind.CeilingLight) }, new[] { true });
            int fixtures = 0, placeholders = 0;
            for (ulong seed = 0; seed < 3; seed++)
            {
                var level = Generate(seed);
                var plan = BuildPlan.Create(level, new BuildOptions { Props = art });
                var expected = level.Props.FindAll(p => PropKinds.IsFixture(p.Kind));
                Assert.AreEqual(expected.Count, plan.Fixtures.Count, "a fixture per fixture prop");
                for (int i = 0; i < expected.Count; i++)
                {
                    var fixture = plan.Fixtures[i];
                    Assert.AreSame(expected[i], fixture.Prop, "fixtures in description order");
                    bool prefabbed = fixture.Prop.Kind == PropKind.CeilingLight && fixture.Prop.Variant == 0;
                    Assert.AreEqual(prefabbed, fixture.Prefab);
                    Assert.AreEqual(prefabbed, fixture.Placeholder == null, "a placeholder box only without a prefab");
                    Assert.IsFalse(plan.Groups.Exists(g => g.Kind == OwnerKind.Prop && g.Id == fixture.Prop.Id),
                        "a fixture has no mesh group, so no collider");
                    fixtures++;
                    if (fixture.Placeholder == null) continue;
                    placeholders++;
                    Assert.AreEqual(36, fixture.Placeholder.Vertices.Count, "a box: twelve triangles");
                    CollectionAssert.AreEqual(new[] { SurfaceKey.Of(SurfaceTag.PropPlaceholder, default) }, fixture.Placeholder.Keys);
                }
            }
            Assert.Greater(fixtures, 0);
            Assert.Greater(placeholders, 0, "wall lights have no prefab here");
        }

        [Test]
        public void Groups_FollowTheDescriptionsOwners_AndOnlyTheBoundaryCastsNoShadow([Values(0, 1)] int seed)
        {
            var level = Generate((ulong)seed);
            var plan = BuildPlan.Create(level, new BuildOptions());
            var owners = new List<(OwnerKind, int)>();
            foreach (var p in level.Primitives)
                if (!owners.Contains((p.OwnerKind, p.OwnerId))) owners.Add((p.OwnerKind, p.OwnerId));
            CollectionAssert.AreEqual(owners, plan.Groups.ConvertAll(g => (g.Kind, g.Id)), "a group per owner, in first-appearance order");
            Assert.IsTrue(plan.Groups.Exists(g => g.Kind == OwnerKind.Boundary));
            foreach (var g in plan.Groups)
            {
                Assert.AreEqual(g.Kind != OwnerKind.Boundary, g.CastShadows, g.Name);
                Assert.AreEqual(g.Mesh.TriangleCount * 3, g.Mesh.Triangles.Count);
                Assert.AreEqual(g.Mesh.Vertices.Count, g.Mesh.Uvs.Count);
                Assert.AreEqual(g.Kind == OwnerKind.Building ? ArtDraws.ProfileIndex(level.Seed, g.Id, 0) : -1, g.Profile);
            }
        }

        [Test]
        public void BuildingGroups_DrawTheirProfile()
        {
            var level = Generate(2);
            var plan = BuildPlan.Create(level, new BuildOptions { ProfileCount = 3 });
            int buildings = 0;
            foreach (var g in plan.Groups)
            {
                if (g.Kind != OwnerKind.Building) continue;
                Assert.AreEqual(ArtDraws.ProfileIndex(level.Seed, g.Id, 3), g.Profile);
                buildings++;
            }
            Assert.Greater(buildings, 0);
        }

        [Test]
        public void Decals_LeaveOutGroundEdges_AndBuildingStripsCarryTheirProfile()
        {
            var level = Generate(2);
            var plan = BuildPlan.Create(level, new BuildOptions { ProfileCount = 3 });
            Assert.IsFalse(plan.Decals.Exists(s => s.Type is DecalType.Ledge or DecalType.DipEdge or DecalType.MoundEdge),
                "the blended ground draws ledges and sub-room edges");
            Assert.AreEqual(plan.Decals.Count, plan.DecalProfiles.Count);
            int building = 0;
            for (int i = 0; i < plan.Decals.Count; i++)
            {
                var strip = plan.Decals[i];
                int expected = DecalTypes.IsBuilding(strip.Type) ? ArtDraws.ProfileIndex(level.Seed, strip.Building, 3) : -1;
                Assert.AreEqual(expected, plan.DecalProfiles[i], $"{strip.Type}");
                if (expected >= 0) building++;
            }
            Assert.Greater(building, 0);
            Assert.IsNull(BuildPlan.Create(level, new BuildOptions { BuildDecals = false }).Decals);
        }

        [Test]
        public void Buildings_AreDrawnPerRoom_WithTheShellApart([Values(false, true)] bool tall)
        {
            var s = LevelGenSettings.Default;
            if (tall)
            {
                s.MaxHeight = 20f;
                s.Buildings.MinFloors = s.Buildings.MaxFloors = 3;
                s.Width = s.Depth = 100f;
                s.PlotCount = 8;
            }
            int split = 0, upperRooms = 0;
            for (ulong seed = 0; seed < 3; seed++)
            {
                var result = LevelGenerator.Generate(s, seed);
                Assert.IsTrue(result.Success, result.Reason);
                var level = result.Level;
                foreach (var g in BuildPlan.Create(level, new BuildOptions()).Groups)
                {
                    if (g.Kind != OwnerKind.Building) continue;
                    if (g.PieceNames == null) continue;
                    split++;
                    var building = level.Buildings.Find(b => b.Id == g.Id);
                    Assert.AreEqual(g.Mesh.TriangleCount, g.TrianglePieces.Count, g.Name);
                    var used = new bool[g.PieceNames.Count];
                    for (int t = 0; t < g.Mesh.TriangleCount; t++)
                    {
                        var piece = g.PieceNames[g.TrianglePieces[t]];
                        used[g.TrianglePieces[t]] = true;
                        var tag = g.Mesh.Keys[g.Mesh.TriangleKeys[t]].Tag;
                        var normal = g.Mesh.Normals[g.Mesh.Triangles[3 * t]];
                        // Not a wall's bottom, which sits buried in the slab under it.
                        if (tag == SurfaceTag.Roof || tag == SurfaceTag.OuterWallOutside && Mathf.Abs(normal.y) < 0.99f)
                            Assert.AreEqual("Shell", piece, $"{g.Name}: {tag} faces out");
                        // A room's floor: a slab top inside a room at its height. Upper floors are smaller, so a lower
                        // slab's top outside them is an open terrace, drawn by the shell.
                        if (tag == SurfaceTag.BuildingFloor && normal.y > 0.99f)
                        {
                            var a = g.Mesh.Vertices[g.Mesh.Triangles[3 * t]];
                            var c = (a + g.Mesh.Vertices[g.Mesh.Triangles[3 * t + 1]] + g.Mesh.Vertices[g.Mesh.Triangles[3 * t + 2]]) / 3f;
                            bool roomFloor = building.Rooms.Exists(r => Mathf.Abs(r.Height - c.y) < 0.01f
                                && Polygon.Contains(level.Polygons[r.Polygon], c.x, c.z));
                            Assert.AreEqual(roomFloor, piece.StartsWith("Room "), $"{g.Name}: floor top at {c} in {piece}");
                        }
                    }
                    CollectionAssert.DoesNotContain(used, false, $"{g.Name}: every piece has a triangle");
                    foreach (var name in g.PieceNames)
                        if (name.StartsWith("Room ") && !name.EndsWith("(floor 0)")) upperRooms++;
                    Assert.GreaterOrEqual(g.PieceNames.Count, building.Rooms.Count, $"{g.Name}: a piece per room");
                }
            }
            Assert.Greater(split, 0);
            if (tall) Assert.Greater(upperRooms, 0, "upper floors' rooms are drawn apart");
        }
    }
}
