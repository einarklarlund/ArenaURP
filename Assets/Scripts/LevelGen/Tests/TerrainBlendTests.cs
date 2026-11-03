using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class TerrainBlendTests
    {
        private static LevelDescription Generate(ulong seed)
        {
            var result = LevelGenerator.Generate(LevelGenSettings.Default, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        private static float Area(Vector3 a, Vector3 b, Vector3 c) =>
            Mathf.Abs((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * 0.5f;

        private static float[] Mix(in BlendTriangle t, int corner)
        {
            var mix = new float[TerrainLayers.Count];
            for (int l = 0; l < mix.Length; l++) mix[l] = t.Weight(corner, (TerrainLayer)l);
            return mix;
        }

        private static bool Blends(in BlendTriangle t)
        {
            for (int k = 0; k < 3; k++)
            {
                var mix = Mix(t, k);
                if (System.Array.Exists(mix, w => w > 1e-3f && w < 1f - 1e-3f) || System.Array.IndexOf(mix, 1f) != System.Array.IndexOf(Mix(t, 0), 1f))
                    return true;
            }
            return false;
        }

        [Test]
        public void SplitTops_TileTheirPrism_InItsPlane([Values(0, 1, 2)] int seed)
        {
            var level = Generate((ulong)seed);
            var split = TerrainBlendStage.Build(level, TerrainBlendSettings.Default);
            Assert.Greater(split.Count, 0);
            foreach (var (index, pieces) in split)
            {
                var p = level.Primitives[index];
                float whole = Polygon.Area(level.Polygons[p.Polygon]), sum = 0f;
                foreach (var t in pieces)
                {
                    sum += Area(t.A, t.B, t.C);
                    for (int k = 0; k < 3; k++)
                    {
                        var v = t.Corner(k);
                        Assert.AreEqual(p.PrismTop(v.x, v.z), v.y, 1e-4f, "a piece lies in its prism's plane");
                        float total = 0f;
                        foreach (float w in Mix(t, k)) total += w;
                        Assert.AreEqual(1f, total, 1e-4f, "a corner's weights sum to one");
                    }
                }
                Assert.AreEqual(whole, sum, 1e-3f * Mathf.Max(1f, whole), "the pieces cover the prism's top once");
            }
        }

        /// <summary>
        /// Every ground triangle the builder draws, split or not: wherever corners meet, they carry the same mix of
        /// layers, so the blend has no seams.
        /// </summary>
        [Test]
        public void Corners_HaveOneMixWhereverTheyMeet([Values(0, 1, 2, 3, 4)] int seed)
        {
            var level = Generate((ulong)seed);
            var split = TerrainBlendStage.Build(level, TerrainBlendSettings.Default);
            var seen = new Dictionary<Vector3, float[]>();
            int shared = 0, blended = 0;

            void Check(Vector3 position, float[] mix, string what)
            {
                if (seen.TryGetValue(position, out var other))
                {
                    for (int l = 0; l < mix.Length; l++)
                        Assert.AreEqual(other[l], mix[l], 1e-3f, $"{what} at {position}: {(TerrainLayer)l}");
                    shared++;
                }
                else seen[position] = mix;
            }

            for (int i = 0; i < level.Primitives.Count; i++)
            {
                var p = level.Primitives[i];
                if (split.TryGetValue(i, out var pieces))
                {
                    foreach (var t in pieces)
                    {
                        for (int k = 0; k < 3; k++) Check(t.Corner(k), Mix(t, k), "split");
                        if (Blends(t)) blended++;
                    }
                    continue;
                }
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var tags = new List<SurfaceTag>();
                var triangles = new List<int>();
                PrimitiveMesher.Append(p, level, vertices, normals, tag =>
                {
                    tags.Add(tag);
                    return triangles;
                });
                for (int t = 0; t < tags.Count; t++)
                {
                    if (!TerrainLayers.TryOf(tags[t], out var layer) || tags[t] != p.Surfaces.Top) continue;
                    var mix = new float[TerrainLayers.Count];
                    mix[(int)layer] = 1f;
                    for (int k = 0; k < 3; k++) Check(vertices[triangles[3 * t + k]], mix, $"{p.OwnerKind} top");
                }
            }
            Assert.Greater(shared, 0);
            Assert.Greater(blended, 0);
        }

        private static LevelDescription HeightFixture(float deviation, Vector2 deviationSlope, float baseline = 3f,
            Vector2 baselineSlope = default, SurfaceTag tag = SurfaceTag.Ground)
        {
            var level = new LevelDescription();
            int polygon = level.AddPolygon(new[] { new Vector2(10, 20), new Vector2(14, 20),
                new Vector2(14, 24), new Vector2(10, 24) });
            level.Plots.Add(new PlotDescription { Id = 0, Polygon = polygon, BaseHeight = baseline, Slope = baselineSlope });
            level.AddPrism(PrimitiveRole.Floor, polygon, -10f, baseline + deviation,
                baselineSlope + deviationSlope, OwnerKind.Plot, 0,
                Surfaces.Of(tag, SurfaceTag.CliffFace, SurfaceTag.CliffFace));
            return level;
        }

        [TestCase(0f, TerrainLayer.Ground)]
        [TestCase(0.2f, TerrainLayer.Ground)]
        [TestCase(0.5f, TerrainLayer.Mound)]
        [TestCase(1f, TerrainLayer.Mound)]
        [TestCase(-0.5f, TerrainLayer.Dip)]
        [TestCase(-1f, TerrainLayer.Dip)]
        public void Heights_UseTheOriginalSlopedRoomPlane(float deviation, TerrainLayer expected)
        {
            var level = HeightFixture(deviation, Vector2.zero, 8f, new Vector2(0.3f, -0.2f));
            foreach (var t in TerrainBlendStage.Build(level, TerrainBlendSettings.Default)[0])
                for (int k = 0; k < 3; k++) Assert.AreEqual(1f, t.Weight(k, expected), 1e-4f);
        }

        [Test]
        public void FaceCrossingHillAndValley_HasExactHeightBandsAndContinuousBlends()
        {
            var level = HeightFixture(0f, Vector2.right);
            var settings = TerrainBlendSettings.Default;
            var pieces = TerrainBlendStage.Build(level, settings)[0];
            var seen = new HashSet<TerrainLayer>();
            float area = 0f;
            foreach (var t in pieces)
            {
                area += Area(t.A, t.B, t.C);
                // Test the interior as well as corners: interpolation must follow the height function across a face.
                for (int sample = 0; sample < 4; sample++)
                {
                    float x = sample == 3 ? (t.A.x + t.B.x + t.C.x) / 3f : t.Corner(sample).x;
                    float d = x - 12f;
                    float hill = Mathf.Clamp01((d - 0.25f) / 0.25f);
                    float valley = Mathf.Clamp01((-d - 0.25f) / 0.25f);
                    var expected = new[] { 1f - hill - valley, hill, valley };
                    for (int l = 0; l < 3; l++)
                    {
                        var layer = (TerrainLayer)l;
                        float actual = sample == 3 ? (t.Weight(0, layer) + t.Weight(1, layer) + t.Weight(2, layer)) / 3f
                            : t.Weight(sample, layer);
                        Assert.AreEqual(expected[l], actual, 1e-4f, $"{layer} at deviation {d}");
                        if (actual > 0f) seen.Add(layer);
                    }
                }
            }
            Assert.AreEqual(16f, area, 1e-4f);
            CollectionAssert.AreEquivalent(new[] { TerrainLayer.Ground, TerrainLayer.Mound, TerrainLayer.Dip }, seen);
        }

        [Test]
        public void Thresholds_AreIndependent_AndZeroWidthBlendsAreHardBoundaries()
        {
            var level = HeightFixture(0f, Vector2.right);
            var settings = new TerrainBlendSettings { HillThreshold = 1f, ValleyThreshold = 0.3f };
            float area = 0f;
            foreach (var t in TerrainBlendStage.Build(level, settings)[0])
            {
                area += Area(t.A, t.B, t.C);
                float d = (t.A.x + t.B.x + t.C.x) / 3f - 12f;
                var expected = d >= 1f ? TerrainLayer.Mound : d <= -0.3f ? TerrainLayer.Dip : TerrainLayer.Ground;
                for (int k = 0; k < 3; k++) Assert.AreEqual(1f, t.Weight(k, expected));
            }
            Assert.AreEqual(16f, area, 1e-4f);
        }

        [Test]
        public void WideBlends_KeepOriginalGroundNeutral()
        {
            var settings = TerrainBlendSettings.Default;
            settings.HillBlendWidth = settings.ValleyBlendWidth = 20f;
            var level = HeightFixture(0f, Vector2.zero);
            var pieces = TerrainBlendStage.Build(level, settings)[0];
            float area = 0f;
            foreach (var t in pieces)
            {
                area += Area(t.A, t.B, t.C);
                for (int k = 0; k < 3; k++) Assert.AreEqual(1f, t.Weight(k, TerrainLayer.Ground));
            }
            Assert.AreEqual(16f, area, 1e-4f, "a face on a split height is emitted exactly once");
        }

        [Test]
        public void HeightMaterials_IgnoreOperationTags_ButPreserveSinkholes()
        {
            foreach (var tag in new[] { SurfaceTag.Ground, SurfaceTag.GroundMound, SurfaceTag.GroundDip })
            {
                var level = HeightFixture(1f, Vector2.zero, tag: tag);
                foreach (var t in TerrainBlendStage.Build(level, TerrainBlendSettings.Default)[0])
                    for (int k = 0; k < 3; k++) Assert.AreEqual(1f, t.Weight(k, TerrainLayer.Mound));
            }
            var sinkhole = HeightFixture(-2f, Vector2.zero, tag: SurfaceTag.GroundSinkhole);
            Assert.IsEmpty(TerrainBlendStage.Build(sinkhole, TerrainBlendSettings.Default), "sinkhole floor retains its own material");
        }

        [Test]
        public void Rim_StaysNearTheSinkholes([Values(0, 1, 2, 3, 4, 5)] int seed)
        {
            // Wherever a piece has rim at a corner, that corner is on a sinkhole's outline (or within the rim's width of
            // one, on a cut), and the piece stays near it, so the rim never runs out along the edges leading away. (Its
            // cuts can be a few rim widths out, along the rays to sites past sharp corners: 2.7 widths at most over ten
            // seeds.)
            var settings = TerrainBlendSettings.Default;
            var level = Generate((ulong)seed);
            var sinkholes = level.SubRooms.FindAll(r => r.Surface == SurfaceTag.GroundSinkhole)
                .ConvertAll(r => level.Polygons[r.Polygon]);
            float Distance(Vector3 v)
            {
                var p = new Vector2(v.x, v.z);
                float best = float.PositiveInfinity;
                foreach (var outline in sinkholes)
                    for (int k = 0; k < outline.Count; k++)
                    {
                        var a = outline[k];
                        var ab = outline[(k + 1) % outline.Count] - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                        best = Mathf.Min(best, (a + ab * t - p).magnitude);
                    }
                return best;
            }
            foreach (var pieces in TerrainBlendStage.Build(level, settings).Values)
                foreach (var t in pieces)
                {
                    bool rim = false;
                    for (int k = 0; k < 3; k++) rim |= t.Weight(k, TerrainLayer.Rim) > 1e-3f;
                    if (!rim) continue;
                    for (int k = 0; k < 3; k++)
                        if (t.Weight(k, TerrainLayer.Rim) > 0.999f) Assert.Less(Distance(t.Corner(k)), 1e-3f, "a rim corner is on a sinkhole");
                        else Assert.Less(Distance(t.Corner(k)), 4f * settings.RimWidth, "the rim fades out near the sinkhole");
                }
        }

        [Test]
        public void BlendedGround_LeavesOutEdgeAndRimDecals()
        {
            var level = Generate(2);
            var settings = DecalSettings.Default;
            settings.BlendedGround = true;
            var plain = DecalStage.Build(level, DecalSettings.Default);
            var blended = DecalStage.Build(level, settings);
            Assert.IsTrue(plain.Exists(s => s.Type is DecalType.DipEdge or DecalType.MoundEdge));
            Assert.IsFalse(blended.Exists(s => s.Type is DecalType.DipEdge or DecalType.MoundEdge));
            Assert.Less(blended.FindAll(s => s.Type == DecalType.Ledge).Count, plain.FindAll(s => s.Type == DecalType.Ledge).Count,
                "no sinkhole rims");
        }

        [Test]
        public void Plan_UsesHeightMaterialsWithoutTheBlendShader()
        {
            var level = HeightFixture(0f, Vector2.right);
            var keys = BuildPlan.Create(level, new BuildOptions { BuildDecals = false }).Groups[0].Mesh.Keys;
            CollectionAssert.Contains(keys, SurfaceKey.Of(SurfaceTag.Ground, default));
            CollectionAssert.Contains(keys, SurfaceKey.Of(SurfaceTag.GroundMound, default));
            CollectionAssert.Contains(keys, SurfaceKey.Of(SurfaceTag.GroundDip, default));
            CollectionAssert.DoesNotContain(keys, SurfaceKey.BlendedGround);
        }

        [Test]
        public void Plan_ThemeThresholdsAndBlendingNeverChangeCollisionGeometry()
        {
            var level = HeightFixture(0f, Vector2.right);
            var hash = level.ComputeHash();
            var originalVertices = new List<Vector3>();
            var originalNormals = new List<Vector3>();
            var originalTriangles = new List<int>();
            PrimitiveMesher.Append(level.Primitives[0], level, originalVertices, originalNormals, originalTriangles);
            for (int mode = 0; mode < 3; mode++)
            {
                var blend = TerrainBlendSettings.Default;
                blend.HillThreshold = mode == 2 ? 1.2f : 0.5f;
                blend.ValleyThreshold = mode == 2 ? 0.8f : 0.5f;
                var group = BuildPlan.Create(level, new BuildOptions { BlendedGround = mode != 0, Blend = blend, BuildDecals = false }).Groups[0];
                Assert.IsNotNull(group.CollisionVertices, "split tops get a collision mesh of their own");
                CollectionAssert.AreEqual(originalVertices, group.CollisionVertices);
                CollectionAssert.AreEqual(originalTriangles, group.CollisionTriangles);
                Assert.AreEqual(hash, level.ComputeHash());
            }
        }

        [TestCase(0.9f)]
        [TestCase(2.5f)]
        public void SinkholeWalls_ExtendBelowTheLoweredFloor(float depth)
        {
            var high = new List<Vector2> { new(0, 0), new(4, 0), new(0, 4) };
            var low = new List<Vector2> { new(4, 0), new(4, 4), new(0, 4) };
            var terrain = new TerrainPlan { Plot = 0 };
            terrain.Cells.Add(new SubRoom { Polygon = high, Site = Polygon.Centroid(high) });
            terrain.Cells.Add(new SubRoom { Polygon = low, Site = Polygon.Centroid(low), Sinkhole = true });
            terrain.Triangles.Add((new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 0, 4)));
            terrain.Triangles.Add((new Vector3(4, -depth, 0), new Vector3(4, -depth, 4), new Vector3(0, -depth, 4)));
            terrain.TriangleCells.Add(0); terrain.TriangleCells.Add(1);
            var level = new LevelDescription();
            TerrainStage.Emit(level, new List<TerrainPlan> { terrain }, new PlotPartition());
            foreach (var prism in level.Primitives)
                Assert.LessOrEqual(prism.Position.y, -depth - LevelGenSettings.SlabThickness,
                    "the upper slab's cliff must reach below the adjacent sinkhole floor");
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var indices = new List<int>();
            PrimitiveMesher.Append(level.Primitives[0], level, vertices, normals, tag =>
                tag == SurfaceTag.CliffFace ? indices : null);
            // The shared edge x+z=4 must contain wall geometry at every depth down to the lower floor.
            for (int step = 1; step < 10; step++)
            {
                var at = new Vector3(2f, -depth * step / 10f, 2f);
                bool covered = false;
                for (int t = 0; t < indices.Count; t += 3)
                {
                    var a = vertices[indices[t]]; var b = vertices[indices[t + 1]]; var c = vertices[indices[t + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    if (Mathf.Abs(Vector3.Dot(normal.normalized, at - a)) > 1e-5f) continue;
                    covered |= Vector3.Dot(Vector3.Cross(b - a, at - a), normal) >= -1e-5f &&
                        Vector3.Dot(Vector3.Cross(c - b, at - b), normal) >= -1e-5f &&
                        Vector3.Dot(Vector3.Cross(a - c, at - c), normal) >= -1e-5f;
                }
                Assert.IsTrue(covered, $"sinkhole wall has no gap at {at}");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisconnectedSinkholes_DoNotPaintTheEdgeBetweenTheirCorners(bool actualSinkholeEdge)
        {
            var level = new LevelDescription();
            var ground = new List<Vector2> { new(0, 0), new(10, 0), new(10, 4), new(0, 4) };
            int roomPolygon = level.AddPolygon(ground);
            var site = new Vector2(5, 2);
            level.Plots.Add(new PlotDescription { Id = 0, Polygon = roomPolygon });
            level.SubRooms.Add(new SubRoomDescription { Plot = 0, Polygon = roomPolygon, Site = site, Surface = SurfaceTag.Ground });
            var holes = actualSinkholeEdge
                ? new[] { new List<Vector2> { new(0, 0), new(0, -2), new(10, -2), new(10, 0) } }
                : new[] { new List<Vector2> { new(-2, 0), new(-2, -2), new(0, -2), new(0, 0) },
                    new List<Vector2> { new(10, 0), new(10, -2), new(12, -2), new(12, 0) } };
            foreach (var hole in holes)
                level.SubRooms.Add(new SubRoomDescription { Plot = 0, Polygon = level.AddPolygon(hole),
                    Site = Polygon.Centroid(hole), Surface = SurfaceTag.GroundSinkhole });
            level.AddPrism(PrimitiveRole.Floor, level.AddPolygon(new[] { site, ground[0], ground[1] }),
                -2f, 0f, Vector2.zero, OwnerKind.Plot, 0, TransitionStage.GroundSurfaces);
            var pieces = TerrainBlendStage.Build(level, TerrainBlendSettings.Default)[0];
            float RimAt(Vector2 at)
            {
                float weight = -1f;
                foreach (var t in pieces)
                {
                    var a = new Vector2(t.A.x, t.A.z); var b = new Vector2(t.B.x, t.B.z); var c = new Vector2(t.C.x, t.C.z);
                    float Cross(Vector2 x, Vector2 y) => x.x * y.y - x.y * y.x;
                    float area = Cross(b - a, c - a);
                    float wb = Cross(at - a, c - a) / area, wc = Cross(b - a, at - a) / area, wa = 1f - wb - wc;
                    if (wa < -1e-5f || wb < -1e-5f || wc < -1e-5f) continue;
                    float value = wa * t.Weight(0, TerrainLayer.Rim) + wb * t.Weight(1, TerrainLayer.Rim) + wc * t.Weight(2, TerrainLayer.Rim);
                    if (weight >= 0f) Assert.AreEqual(weight, value, 1e-4f, "the rim is continuous on split edges");
                    weight = value;
                }
                Assert.GreaterOrEqual(weight, 0f, "the rim pieces must cover the original face");
                return weight;
            }
            Assert.AreEqual(actualSinkholeEdge ? 1f : 0f, RimAt(new Vector2(5f, 0f)), 1e-4f);
            if (!actualSinkholeEdge)
            {
                Assert.AreEqual(0f, RimAt(new Vector2(5f, 0.1f)), 1e-4f);
                Assert.Greater(RimAt(new Vector2(0.2f, 0f)), 0f, "a short rim cap remains at the first corner");
                Assert.Greater(RimAt(new Vector2(9.8f, 0f)), 0f, "a short rim cap remains at the second corner");
            }
            float areaSum = 0f;
            foreach (var t in pieces) areaSum += Area(t.A, t.B, t.C);
            Assert.AreEqual(10f, areaSum, 1e-4f, "rim caps cover the ground once without gaps or overlaps");
        }

        [Test]
        public void Plan_DrawsGroundWithLayers()
        {
            var level = Generate(1);
            var plan = BuildPlan.Create(level, new BuildOptions { BlendedGround = true, BuildDecals = false });
            int groundTriangles = 0, mixed = 0;
            foreach (var group in plan.Groups)
            {
                var mesh = group.Mesh;
                if (mesh == null || mesh.LayerIds == null) continue;
                Assert.AreEqual(mesh.Vertices.Count, mesh.LayerIds.Count);
                Assert.AreEqual(mesh.Vertices.Count, mesh.LayerWeights.Count);
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    if (!mesh.Keys[mesh.TriangleKeys[t]].Ground) continue;
                    groundTriangles++;
                    var id = mesh.LayerIds[mesh.Triangles[3 * t]];
                    for (int k = 0; k < 3; k++)
                    {
                        int v = mesh.Triangles[3 * t + k];
                        Assert.AreEqual(id, mesh.LayerIds[v], "a triangle's vertices agree on its layers");
                        var w = mesh.LayerWeights[v];
                        Assert.AreEqual(1f, w.x + w.y + w.z, 1e-4f, "each vertex's weights sum to one");
                        Assert.AreEqual(1f, w.w, "ground vertices are marked");
                    }
                    if (id.x != id.y || id.y != id.z) mixed++;
                }
            }
            Assert.Greater(groundTriangles, 0);
            Assert.Greater(mixed, 0);
        }

        [Test]
        public void Slopes_FadeIntoTheGroundAndTheirSides([Values(0, 1, 2)] int seed)
        {
            // A ramp's or bank's top is its own layer in the middle, plain ground where it meets the ground at its top
            // edge and foot, and its sides' layer where it meets a flare; a flare is that layer at the slope's edge and
            // ground at its foot.
            var level = Generate((ulong)seed);
            var split = TerrainBlendStage.Build(level, TerrainBlendSettings.Default);
            float topMax = 0f, groundMax = 0f, sideOnTop = 0f, groundOnFlare = 0f;
            int faceCorners = 0;
            foreach (var (index, pieces) in split)
            {
                var p = level.Primitives[index];
                if (p.OwnerKind != OwnerKind.Transition) continue;
                var t = level.Transitions[p.OwnerId];
                var dir = (t.To - t.From).normalized;
                foreach (var b in pieces)
                    for (int k = 0; k < 3; k++)
                    {
                        if (p.Surfaces.Top == SurfaceTag.RampSide)
                        {
                            groundOnFlare = Mathf.Max(groundOnFlare, b.Weight(k, TerrainLayer.Ground));
                            continue;
                        }
                        topMax = Mathf.Max(topMax, b.Weight(k, TerrainLayer.Ramp) + b.Weight(k, TerrainLayer.Bank));
                        groundMax = Mathf.Max(groundMax, b.Weight(k, TerrainLayer.Ground));
                        sideOnTop = Mathf.Max(sideOnTop, b.Weight(k, TerrainLayer.RampSide));
                        var v = b.Corner(k);
                        var r = new Vector2(v.x, v.z) - t.From;
                        if (Mathf.Abs(dir.x * r.y - dir.y * r.x) > 1e-3f) continue;
                        Assert.AreEqual(1f, b.Weight(k, TerrainLayer.Ground), 1e-3f, "a top's corner on its face is ground");
                        faceCorners++;
                    }
            }
            Assert.AreEqual(1f, topMax, 1e-3f, "the middle of a top is the slope's own layer");
            Assert.AreEqual(1f, groundMax, 1e-3f);
            Assert.Greater(sideOnTop, 0.9f, "a top fades into its sides");
            Assert.AreEqual(1f, groundOnFlare, 1e-3f, "a flare's foot is ground");
            Assert.Greater(faceCorners, 0);
        }

        [Test]
        public void LedgesAndCliffs_HaveALipOnTheirHigherGround()
        {
            // Ground corners on a ledge's or cliff's top edge are the cliff's layer, away from the edge's tapered ends.
            int onEdge = 0;
            for (ulong seed = 0; seed < 4; seed++)
            {
                var level = Generate(seed);
                var settings = TerrainBlendSettings.Default;
                var split = TerrainBlendStage.Build(level, settings);
                foreach (var (index, pieces) in split)
                {
                    var p = level.Primitives[index];
                    if (p.OwnerKind != OwnerKind.Plot) continue;
                    foreach (var b in pieces)
                        for (int k = 0; k < 3; k++)
                        {
                            var v = b.Corner(k);
                            var xz = new Vector2(v.x, v.z);
                            foreach (var t in level.Transitions)
                            {
                                if (t.Type is not (TransitionType.Ledge or TransitionType.Cliff or TransitionType.Blocked)) continue;
                                if (t.PlotA != p.OwnerId && t.PlotB != p.OwnerId) continue;
                                var dir = (t.To - t.From).normalized;
                                var r = xz - t.From;
                                float along = Vector2.Dot(dir, r), length = (t.To - t.From).magnitude;
                                if (Mathf.Abs(dir.x * r.y - dir.y * r.x) > 1e-3f || along < settings.LipWidth || along > length - settings.LipWidth)
                                    continue;
                                // Only the higher side gets the lip.
                                if (b.Weight(k, TerrainLayer.Cliff) < 1e-3f) continue;
                                Assert.AreEqual(1f, b.Weight(k, TerrainLayer.Cliff), 1e-3f, $"transition {t.Id}'s lip at {v}");
                                onEdge++;
                            }
                        }
                }
            }
            Assert.Greater(onEdge, 0);
        }
    }
}
