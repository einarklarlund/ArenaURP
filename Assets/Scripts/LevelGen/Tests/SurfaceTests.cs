using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class SurfaceTests
    {
        private static (LevelDescription level, GenerationTrace trace) Generate(ulong seed)
        {
            var trace = new GenerationTrace();
            var result = LevelGenerator.Generate(LevelGenSettings.Default, seed, trace);
            Assert.IsTrue(result.Success, result.Reason);
            return (result.Level, trace);
        }

        [Test]
        public void EveryFace_HasASurfaceTag([Values(0, 1, 2, 3, 4)] int seed)
        {
            var (level, trace) = Generate((ulong)seed);
            // Interior walls come only from wall edges; a seed whose rooms meet by open or railed edges has none.
            bool wallEdges = trace.Buildings.Exists(plan => plan.FloorPlans.Exists(f => f.Walls.Exists(e => e.Kind == EdgeKind.Wall)));
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var seen = new HashSet<SurfaceTag>();
            foreach (var p in level.Primitives)
            {
                var primitive = p;
                PrimitiveMesher.Append(p, level, vertices, normals, tag =>
                {
                    Assert.AreNotEqual(SurfaceTag.None, tag, $"{primitive.OwnerKind} {primitive.OwnerId} {primitive.Role} {primitive.Shape}");
                    // A building's tags only on its own primitives, where a profile can resolve them.
                    Assert.AreEqual(primitive.OwnerKind == OwnerKind.Building, SurfaceTags.IsBuilding(tag),
                        $"{primitive.OwnerKind} {primitive.Role} has {tag}");
                    seen.Add(tag);
                    return triangles;
                });
            }
            foreach (var tag in new[] { SurfaceTag.Ground, SurfaceTag.CliffFace, SurfaceTag.BoundaryWall, SurfaceTag.BuildingFloor,
                         SurfaceTag.OuterWallOutside, SurfaceTag.OuterWallInside })
                Assert.IsTrue(seen.Contains(tag), $"seed {seed} has {tag} faces");
            Assert.AreEqual(wallEdges, seen.Contains(SurfaceTag.InteriorWall), $"seed {seed} has InteriorWall faces where it has wall edges");
        }

        [Test]
        public void OuterWalls_FaceOutsideWithTheirFront([Values(0, 1, 2)] int seed)
        {
            var (level, _) = Generate((ulong)seed);
            int walls = 0;
            foreach (var p in level.Primitives)
            {
                if (p.OwnerKind != OwnerKind.Building || p.Surfaces.Front != SurfaceTag.OuterWallOutside) continue;
                var building = level.Buildings[p.OwnerId];
                // The wall's own floor: the one whose footprint the wall stands on at its height.
                int floor = Mathf.Clamp(Mathf.FloorToInt((p.Position.y + p.Size.y * 0.5f - building.FirstFloorHeight) / building.FloorHeight),
                    0, building.Floors - 1);
                var centre = Polygon.Centroid(level.Polygons[building.FloorFootprints[floor]]);
                if (p.Shape == PrimitiveShape.Prism)
                {
                    // A prism wall's sides are front but for its BackSides: the inside faces turn to the middle, and its
                    // longest front side (the outside face, not an end) turns away.
                    var outline = level.Polygons[p.Polygon];
                    var own = Polygon.Centroid(outline);
                    float longest = 0f, outward = 0f;
                    for (int i = 0; i < outline.Count; i++)
                    {
                        Vector2 a = outline[i], b = outline[(i + 1) % outline.Count], mid = (a + b) * 0.5f;
                        var normal = new Vector2(b.y - a.y, a.x - b.x).normalized;
                        if (Vector2.Dot(normal, mid - own) < 0f) normal = -normal;
                        float facing = Vector2.Dot(normal, mid - centre);
                        if ((p.BackSides >> i & 1u) != 0) Assert.Less(facing, 0f, "an outer wall's back faces the middle");
                        else if ((b - a).sqrMagnitude > longest)
                        {
                            longest = (b - a).sqrMagnitude;
                            outward = facing;
                        }
                    }
                    Assert.Greater(outward, 0f, "an outer wall's front faces away from the middle");
                    walls++;
                    continue;
                }
                // Local +X, the front, in level space.
                var front = Quaternion.Euler(0f, p.YawDegrees, 0f) * Vector3.right;
                var toWall = new Vector2(p.Position.x, p.Position.z) - centre;
                if (toWall.sqrMagnitude < 1e-4f) continue;
                Assert.Greater(Vector2.Dot(new Vector2(front.x, front.z), toWall), 0f, "an outer wall's front faces away from the middle");
                walls++;
            }
            Assert.Greater(walls, 0);
        }

        [Test]
        public void Terrain_MoundsAndDips_FollowTheBulgeRule()
        {
            int mounds = 0, dips = 0;
            for (ulong seed = 0; seed < 8; seed++)
            {
                var (_, trace) = Generate(seed);
                foreach (var terrain in trace.Terrain)
                    for (int i = 0; i < terrain.Cells.Count; i++)
                    {
                        var cell = terrain.Cells[i];
                        var tag = terrain.SurfaceOf(i);
                        if (cell.Sinkhole)
                        {
                            Assert.AreEqual(SurfaceTag.GroundSinkhole, tag);
                            continue;
                        }
                        if (!cell.Picked)
                        {
                            Assert.AreEqual(SurfaceTag.Ground, tag, "only a sub-room an operation picked is a mound or a dip");
                            continue;
                        }
                        float site = 0f, corners = 0f;
                        int count = 0;
                        for (int t = 0; t < terrain.Triangles.Count; t++)
                            if (terrain.TriangleCells[t] == i)
                            {
                                site = terrain.Triangles[t].a.y;
                                corners += terrain.Triangles[t].b.y;
                                count++;
                            }
                        float bulge = site - corners / count;
                        var expected = bulge > TerrainPlan.BulgeThreshold ? SurfaceTag.GroundMound
                            : bulge < -TerrainPlan.BulgeThreshold ? SurfaceTag.GroundDip : SurfaceTag.Ground;
                        Assert.AreEqual(expected, tag);
                        if (tag == SurfaceTag.GroundMound) mounds++;
                        else if (tag == SurfaceTag.GroundDip) dips++;
                    }
            }
            Assert.Greater(mounds + dips, 0, "some picked sub-rooms bulge");
        }

        [Test]
        public void Theme_LeavesHashAndColliderGeometryUnchanged()
        {
            var (level, _) = Generate(3);
            ulong hash = level.ComputeHash();
            List<List<Vector3>> reference = null;
            foreach (int profiles in new[] { 0, 1, 3 })
                foreach (bool blended in new[] { false, true })
                {
                    var plan = BuildPlan.Create(level, new BuildOptions { ProfileCount = profiles, BlendedGround = blended });
                    Assert.AreEqual(hash, level.ComputeHash(), "planning with a theme leaves the description alone");
                    var colliders = plan.Groups.FindAll(g => g.Mesh != null).ConvertAll(g => g.CollisionVertices ?? g.Mesh.Vertices);
                    if (reference == null) reference = colliders;
                    else
                    {
                        Assert.AreEqual(reference.Count, colliders.Count);
                        for (int i = 0; i < colliders.Count; i++) CollectionAssert.AreEqual(reference[i], colliders[i]);
                    }
                }
        }

        [Test]
        public void ProfilePicks_AreDeterministicAndSpread()
        {
            var hits = new int[3];
            for (int building = 0; building < 60; building++)
            {
                int pick = ArtDraws.ProfileIndex(42, building, 3);
                Assert.AreEqual(pick, ArtDraws.ProfileIndex(42, building, 3));
                Assert.That(pick, Is.InRange(0, 2));
                hits[pick]++;
            }
            foreach (int h in hits) Assert.Greater(h, 0, "every profile gets picked");
            Assert.AreEqual(-1, ArtDraws.ProfileIndex(42, 0, 0));
        }
    }
}
