using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class DeterminismTests
    {
        private static readonly ulong[] Seeds = { 0UL, 1UL, 42UL, 0xDEADBEEFUL, ulong.MaxValue };

        [Test]
        public void SameSeedAndSettings_GiveByteIdenticalDescription([ValueSource(nameof(Seeds))] ulong seed)
        {
            var settings = LevelGenSettings.Default;
            byte[] first = LevelGenerator.Generate(settings, seed).Level.Serialize();
            for (int run = 0; run < 3; run++)
                CollectionAssert.AreEqual(first, LevelGenerator.Generate(settings, seed).Level.Serialize(), $"run {run + 2}");
        }

        [Test]
        public void SameSeedAndSettings_GiveIdenticalSpanGrid()
        {
            var settings = LevelGenSettings.Default;
            ulong a = Rasterizer.Rasterize(LevelGenerator.Generate(settings, 7UL).Level, settings.CellSize).ComputeHash();
            ulong b = Rasterizer.Rasterize(LevelGenerator.Generate(settings, 7UL).Level, settings.CellSize).ComputeHash();
            Assert.AreEqual(a, b);
        }

        [Test]
        public void DifferentSeeds_GiveDifferentDescriptions()
        {
            var settings = LevelGenSettings.Default;
            var a = LevelGenerator.Generate(settings, 1UL).Level;
            var b = LevelGenerator.Generate(settings, 2UL).Level;
            b.Seed = a.Seed;
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash(), "more than the seed field differs");
        }

        [Test]
        public void Generator_IsVersion18()
        {
            Assert.AreEqual(18, LevelGenerator.Generate(LevelGenSettings.Default, 3UL).Level.GeneratorVersion);
        }

        [Test]
        public void Serialize_CoversPolygonsAndPrismFields()
        {
            var level = LevelGenerator.Generate(LevelGenSettings.Default, 5UL).Level;
            ulong original = level.ComputeHash();

            var moved = level.Polygons[0][1];
            level.Polygons[0][1] = moved + new Vector2(0.01f, 0f);
            Assert.AreNotEqual(original, level.ComputeHash(), "a polygon vertex");
            level.Polygons[0][1] = moved;

            int prism = level.Primitives.FindIndex(p => p.Shape == PrimitiveShape.Prism);
            var kept = level.Primitives[prism];
            var p = kept;
            p.Slope += new Vector2(0f, 0.01f);
            level.Primitives[prism] = p;
            Assert.AreNotEqual(original, level.ComputeHash(), "a prism slope");
            level.Primitives[prism] = kept;
            Assert.AreEqual(original, level.ComputeHash());
        }

        [Test]
        public void ChangedSettings_ChangeConfigHash()
        {
            var a = LevelGenSettings.Default;
            var b = a;
            b.Buildings.RoofAccessOdds = 0.75f;
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void SeedTree_StreamsAreIndependentPerIndex()
        {
            var plot3 = SeedTree.Stream(99UL, GenStage.Allocation, 3);
            var plot4 = SeedTree.Stream(99UL, GenStage.Allocation, 4);
            Assert.AreNotEqual(plot3.NextUInt(), plot4.NextUInt());

            // Drawing more from plot 3 must not move plot 4's stream.
            var plot4Again = SeedTree.Stream(99UL, GenStage.Allocation, 4);
            plot4Again.NextUInt();
            var expected = plot4Again.NextUInt();
            plot3.NextUInt();
            Assert.AreEqual(expected, plot4.NextUInt());
        }

        [Test]
        public void Pcg32_MatchesReferenceSequence()
        {
            // pcg32_srandom(42, 54) from the PCG reference implementation.
            var rng = new Pcg32(42UL, 54UL);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (uint e in expected) Assert.AreEqual(e, rng.NextUInt());
        }

        [Test]
        public void SpanGrid_MergesTouchingSpans_KeepingTopRole()
        {
            var grid = new SpanGrid(1f, 1f, 1f);
            grid.AddSpan(0, 0, 0f, 0.5f, PrimitiveRole.Floor);
            grid.AddSpan(0, 0, 0.5f, 1.5f, PrimitiveRole.Cover);
            grid.AddSpan(0, 0, 4f, 4.5f, PrimitiveRole.Platform);
            var column = grid.GetColumn(0, 0);
            Assert.AreEqual(2, column.Count);
            Assert.AreEqual(0f, column[0].Bottom);
            Assert.AreEqual(1.5f, column[0].Top);
            Assert.AreEqual(PrimitiveRole.Cover, column[0].Role);
            Assert.AreEqual(PrimitiveRole.Platform, column[1].Role);
        }
    }
}
