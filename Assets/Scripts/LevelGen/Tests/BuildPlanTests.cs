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
    }
}
