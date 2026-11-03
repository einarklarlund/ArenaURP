using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class SharingTests
    {
        private static LevelGenSettings WithCatalogue()
        {
            var s = LevelGenSettings.Default;
            s.Props.Cars = new[] { PropShape.DefaultCar, new PropShape { Weight = 2f, Length = 5f, Width = 2f, Height = 2.3f } };
            s.Width = 80f;
            return s.Sanitized();
        }

        [Test]
        public void Settings_RoundTripThroughBytes()
        {
            foreach (var s in new[] { LevelGenSettings.Default.Sanitized(), WithCatalogue() })
            {
                var bytes = s.ToBytes();
                var back = LevelGenSettings.FromBytes(bytes);
                CollectionAssert.AreEqual(bytes, back.ToBytes());
                Assert.AreEqual(s.ComputeHash(), back.ComputeHash());
            }
        }

        [Test]
        public void Description_RoundTripsThroughBytes([Values(0, 1, 2)] int seed)
        {
            var level = LevelGenerator.Generate(WithCatalogue(), (ulong)seed).Level;
            var bytes = level.Serialize();
            var back = LevelDescription.Deserialize(bytes);
            CollectionAssert.AreEqual(bytes, back.Serialize());
            Assert.AreEqual(level.Primitives.Count, back.Primitives.Count);
        }

        [Test]
        public void ShareCode_RebuildsTheSameLevel()
        {
            var settings = WithCatalogue();
            string code = ShareCode.Encode(LevelGenerator.GeneratorVersion, 1234567890123UL, settings);
            Assert.IsTrue(ShareCode.TryDecode(code, out ulong seed, out var decoded, out string error), error);
            Assert.AreEqual(1234567890123UL, seed);
            Assert.AreEqual(settings.ComputeHash(), decoded.ComputeHash());
            Assert.AreEqual(LevelGenerator.Generate(settings, seed).Level.ComputeHash(),
                LevelGenerator.Generate(decoded, seed).Level.ComputeHash());
            Assert.Less(code.Length, 600, "short enough to paste");
        }

        [Test]
        public void ShareCode_RefusesDamagedAndOtherVersionCodes()
        {
            var settings = LevelGenSettings.Default.Sanitized();
            Assert.IsFalse(ShareCode.TryDecode("hello", out _, out _, out _));
            Assert.IsFalse(ShareCode.TryDecode("L!!!not-base64", out _, out _, out _));
            string code = ShareCode.Encode(LevelGenerator.GeneratorVersion, 5, settings);
            Assert.IsFalse(ShareCode.TryDecode(code.Substring(0, code.Length / 2), out _, out _, out _), "truncated");
            string other = ShareCode.Encode(LevelGenerator.GeneratorVersion - 1, 5, settings);
            Assert.IsFalse(ShareCode.TryDecode(other, out _, out _, out string error));
            StringAssert.Contains("version", error);
        }

        [Test]
        public void Ticket_RoundTripsThroughBytes()
        {
            var ticket = new LevelTicket
            {
                GeneratorVersion = LevelGenerator.GeneratorVersion, Seed = ulong.MaxValue - 3, Config = WithCatalogue().ToBytes(),
                Preset = 2, Theme = 1, Hash = 0xDEADBEEFCAFEUL, Match = 7,
            };
            var back = LevelTicket.FromBytes(ticket.ToBytes());
            Assert.AreEqual(ticket.Seed, back.Seed);
            Assert.AreEqual(ticket.Preset, back.Preset);
            Assert.AreEqual(ticket.Theme, back.Theme);
            Assert.AreEqual(ticket.Hash, back.Hash);
            Assert.AreEqual(ticket.Match, back.Match);
            CollectionAssert.AreEqual(ticket.Config, back.Config);
            Assert.AreEqual(WithCatalogue().ComputeHash(), back.Settings.ComputeHash());
        }

        [Test]
        public void SpawnPoints_AreOnOpenReachableGround_AndSpreadOut([Values(0, 1, 2, 3, 4)] int seed)
        {
            var settings = LevelGenSettings.Default.Sanitized();
            var level = LevelGenerator.Generate(settings, (ulong)seed).Level;
            Assert.AreEqual(settings.SpawnPoints, level.SpawnPoints.Count, "the default level has room for every point");

            var check = LevelEvaluator.Validate(level, settings);
            var grid = check.Surfaces.Grid;
            var picked = new List<Vector2>();
            foreach (var spawn in level.SpawnPoints)
            {
                int x = Mathf.FloorToInt(spawn.Position.x / grid.CellSize), z = Mathf.FloorToInt(spawn.Position.z / grid.CellSize);
                int surface = check.Surfaces.FindAt(x, z, spawn.Position.y);
                Assert.GreaterOrEqual(surface, 0, "a spawn point stands on a surface");
                Assert.IsTrue(check.Reachability.InR[surface] && !check.Reachability.Sink[surface], "in R, not a sink");
                Assert.IsTrue(float.IsPositiveInfinity(check.Surfaces[surface].Ceiling), "under open sky");
                Assert.GreaterOrEqual(check.Regions.SurfaceRegion[surface], 0, "outdoors");
                picked.Add(new Vector2(spawn.Position.x, spawn.Position.z));
            }
            float closest = float.PositiveInfinity;
            for (int i = 0; i < picked.Count; i++)
                for (int j = i + 1; j < picked.Count; j++)
                    closest = Mathf.Min(closest, (picked[i] - picked[j]).magnitude);
            Assert.Greater(closest, 4f, "farthest-point sampling keeps them apart");
        }
    }
}
