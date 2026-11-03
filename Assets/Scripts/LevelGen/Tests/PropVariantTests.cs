using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class PropVariantTests
    {
        private static readonly PropShape Van = new() { Weight = 1f, Length = 5f, Width = 2f, Height = 2.3f };
        private static readonly PropShape Wall = new() { Weight = 1f, Length = 3f, Width = 0.3f, Height = 1f };

        private static readonly PropShape Pine = new()
        {
            Weight = 2f, Length = 0.35f, Width = 0.35f, Height = 3.4f,
            CanopyLength = 3.1f, CanopyWidth = 3.1f, CanopyBottom = 3.4f, CanopyHeight = 2.5f,
        };

        private static LevelGenSettings TwoOfEach()
        {
            var s = LevelGenSettings.Default;
            s.Props.Cars = new[] { PropShape.DefaultCar, Van };
            s.Props.Trees = new[] { PropShape.DefaultTree, Pine };
            s.Props.Fences = new[] { PropShape.DefaultFence, Wall };
            s.Props.FencedRoomOdds = 1f;
            return s;
        }

        private static (LevelDescription level, GenerationTrace trace) Generate(LevelGenSettings s, ulong seed)
        {
            var trace = new GenerationTrace();
            var result = LevelGenerator.Generate(s, seed, trace);
            Assert.IsTrue(result.Success, result.Reason);
            return (result.Level, trace);
        }

        [Test]
        public void EveryVariant_IsDrawn_AndPlacedByItsFootprint()
        {
            var s = TwoOfEach();
            var seen = new HashSet<(PropKind, int)>();
            // Buildings, their porches and fence rings leave a 50 x 50 level little edge ground for band props, so
            // search seeds until every variant has turned up.
            for (ulong seed = 0; seed < 24 && seen.Count < 6; seed++)
            {
                var (level, _) = Generate(s, seed);
                // Light fixtures have no collider, so no placeholders in the description (see InteriorTests).
                var solid = level.Props.FindAll(p => !PropKinds.IsFixture(p.Kind));
                Assert.AreEqual(solid.Count, CountOwners(level), "a record per prop, and placeholders per solid prop");
                foreach (var prop in solid)
                {
                    if (prop.Kind is PropKind.Fence or PropKind.Car or PropKind.Tree) seen.Add((prop.Kind, prop.Variant));
                    var shape = s.Sanitized().Props.Variants(prop.Kind)[prop.Variant];
                    var boxes = level.Primitives.FindAll(p => p.OwnerKind == OwnerKind.Prop && p.OwnerId == prop.Id);
                    var body = boxes[0];
                    Assert.AreEqual(shape.Width, body.Size.x, 1e-4f, $"{prop.Kind} {prop.Variant} width");
                    if (prop.Kind != PropKind.Fence) Assert.AreEqual(shape.Length, body.Size.z, 1e-4f, "length");
                    else Assert.AreEqual(shape.Length * prop.Stretch, body.Size.z, 1e-4f, "a fence piece is its variant, stretched");
                    // A car parks only on level ground, so it stands on it with no sink (see Cars along the boundary walls).
                    if (prop.Kind == PropKind.Car) Assert.AreEqual(shape.Height, body.Size.y, 1e-4f);
                    if (prop.Kind == PropKind.Tree)
                    {
                        Assert.AreEqual(2, boxes.Count);
                        Assert.AreEqual(prop.Position.y + shape.CanopyBottom, boxes[1].Position.y, 1e-4f, "canopy bottom");
                        Assert.AreEqual(shape.CanopyLength, boxes[1].Size.z, 1e-4f);
                    }
                    Assert.AreEqual(prop.Position.x, body.Position.x, 1e-4f, "the pivot is the footprint's centre");
                    Assert.AreEqual(prop.Position.z, body.Position.z, 1e-4f);
                }
            }
            foreach (var kind in new[] { PropKind.Fence, PropKind.Car, PropKind.Tree })
                for (int v = 0; v < 2; v++)
                    Assert.IsTrue(seen.Contains((kind, v)), $"{kind} variant {v} is drawn");
        }

        private static int CountOwners(LevelDescription level)
        {
            var ids = new HashSet<int>();
            foreach (var p in level.Primitives)
                if (p.OwnerKind == OwnerKind.Prop) ids.Add(p.OwnerId);
            return ids.Count;
        }

        [Test]
        public void FencePieces_FitTheirRun_OneVariantPerRun([Values(0, 1, 2, 3)] int seed)
        {
            var s = TwoOfEach();
            var (level, trace) = Generate(s, (ulong)seed);
            var fences = trace.Props.FindAll(p => p.Kind == PropKind.Fence);
            Assert.Greater(fences.Count, 0);
            foreach (var f in fences)
            {
                Assert.That(f.Stretch, Is.InRange(1f - PropStage.MaxFenceStretch - 1e-4f, 1f + PropStage.MaxFenceStretch + 1e-4f));
                Assert.AreEqual(f.Shape.Length * f.Stretch, f.Length, 1e-4f);
            }
            // Pieces that touch end to end on one line are one run: one variant, one stretch.
            for (int i = 0; i < fences.Count; i++)
                for (int j = i + 1; j < fences.Count; j++)
                {
                    var a = fences[i];
                    var b = fences[j];
                    if (Mathf.Abs(Vector2.Dot(a.Along, b.Along)) < 0.9999f) continue;
                    var offset = b.Centre - a.Centre;
                    if (Mathf.Abs(a.Along.x * offset.y - a.Along.y * offset.x) > 1e-3f) continue;
                    if (Mathf.Abs(offset.magnitude - (a.Length + b.Length) * 0.5f) > 1e-3f) continue;
                    Assert.AreEqual(a.Variant, b.Variant, "a run's pieces share a variant");
                    Assert.AreEqual(a.Stretch, b.Stretch, 1e-5f);
                }
        }

        [Test]
        public void Plan_PrefabsStandInForPlaceholders()
        {
            var s = TwoOfEach();
            var (level, _) = Generate(s, 2);
            // Only the first car variant has a prefab; everything else stays a placeholder.
            var art = new PropArt();
            art.Set(PropKind.Car, new[] { PropShape.DefaultCar }, new[] { true });
            var plan = BuildPlan.Create(level, new BuildOptions { Props = art });
            int prefabs = 0;
            foreach (var prop in level.Props)
            {
                if (PropKinds.IsFixture(prop.Kind)) continue;
                bool prefabbed = prop.Kind == PropKind.Car && prop.Variant == 0;
                var group = plan.Groups.Find(g => g.Kind == OwnerKind.Prop && g.Id == prop.Id);
                Assert.IsNotNull(group, $"prop {prop.Id} has its group");
                Assert.AreEqual(prefabbed, group.Prefab != null, $"prop {prop.Id}: a prefab only for car variant 0");
                Assert.AreEqual(prefabbed, group.Mesh == null, $"prop {prop.Id}: a placeholder mesh only without a prefab");
                if (!prefabbed) continue;
                Assert.AreSame(prop, group.Prefab);
                prefabs++;
            }
            Assert.Greater(prefabs, 0, "seed 2 has a first-variant car");
        }
    }
}
