using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class SiteTests
    {
        private const ulong Seeds = 6;

        /// <summary>The surface a site or line point stands on: its cell, at its height.</summary>
        private static void AssertStandable(ValidationResult check, Vector3 p, string what)
        {
            var grid = check.Surfaces.Grid;
            int x = Mathf.FloorToInt(p.x / grid.CellSize), z = Mathf.FloorToInt(p.z / grid.CellSize);
            int surface = check.Surfaces.FindAt(x, z, p.y);
            Assert.GreaterOrEqual(surface, 0, $"{what} at {p} is on no surface");
            Assert.IsTrue(check.Surfaces[surface].HasWalkableRole, $"{what} at {p} is not on walkable ground");
            Assert.IsTrue(check.Reachability.InR[surface], $"{what} at {p} is not in R");
            Assert.IsFalse(check.Reachability.Sink[surface], $"{what} at {p} is in a sink");
        }

        [Test]
        public void SitesAndLines_StandOnReachableGroundInTheirRoom([Values(0, 1, 2, 3, 4)] int preset)
        {
            var s = GuaranteeTests.Preset(preset);
            for (ulong seed = 0; seed < Seeds; seed++)
            {
                var level = LevelGenerator.Generate(s, seed).Level;
                var check = LevelEvaluator.Validate(level, s, overlaps: false);
                var rooms = new RoomGraph(level);
                string where = $"preset {preset} seed {seed}";
                foreach (var site in level.ChestSites)
                {
                    AssertStandable(check, site.Position, $"{where}: {site.Kind} site");
                    Assert.AreEqual(rooms.RoomAt(site.Position), site.Room, $"{where}: {site.Kind} site's room");
                }
                foreach (var line in level.PickupLines)
                {
                    Assert.AreEqual(SiteStage.LinePoints, line.Points.Count, $"{where}: {line.Kind} line");
                    foreach (var p in line.Points) AssertStandable(check, p, $"{where}: {line.Kind} line point");
                    Assert.AreEqual(rooms.RoomAt(line.Points[line.Points.Count / 2]), line.Room, $"{where}: {line.Kind} line's room");
                }
                for (int room = 0; room < rooms.RoomCount; room++)
                    Assert.GreaterOrEqual(rooms.ChestSitesIn(room).Count, SiteStage.MinSitesPerRoom, $"{where}: room {room}");
            }
        }

        [Test]
        public void ChestSites_KeepTheirGap()
        {
            var level = LevelGenerator.Generate(LevelGenSettings.Default, 4UL).Level;
            var sites = level.ChestSites;
            for (int i = 0; i < sites.Count; i++)
            for (int j = i + 1; j < sites.Count; j++)
            {
                var a = sites[i].Position;
                var b = sites[j].Position;
                bool apart = (new Vector2(a.x - b.x, a.z - b.z)).magnitude >= SiteStage.MinSiteGap - 1e-4f || Mathf.Abs(a.y - b.y) >= 2f;
                Assert.IsTrue(apart, $"{sites[i].Kind} at {a} and {sites[j].Kind} at {b}");
            }
        }

        [Test]
        public void EveryFeatureKind_TurnsUpAcrossSeeds()
        {
            var sites = new HashSet<ChestSiteKind>();
            var lines = new HashSet<PickupLineKind>();
            foreach (int preset in new[] { 0, 2 })
                for (ulong seed = 0; seed < 12; seed++)
                {
                    var level = LevelGenerator.Generate(GuaranteeTests.Preset(preset), seed).Level;
                    foreach (var site in level.ChestSites) sites.Add(site.Kind);
                    foreach (var line in level.PickupLines) lines.Add(line.Kind);
                }
            foreach (ChestSiteKind kind in System.Enum.GetValues(typeof(ChestSiteKind)))
                if (kind != ChestSiteKind.OpenGround) Assert.Contains(kind, new List<ChestSiteKind>(sites), "chest site");
            foreach (PickupLineKind kind in System.Enum.GetValues(typeof(PickupLineKind)))
                Assert.Contains(kind, new List<PickupLineKind>(lines), "pickup line");
        }

        [Test]
        public void RoomLinks_CoverEveryTransitionPairOnce()
        {
            var level = LevelGenerator.Generate(LevelGenSettings.Default, 2UL).Level;
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < level.RoomLinks.Count; i++)
            {
                var link = level.RoomLinks[i];
                Assert.Less(link.RoomA, link.RoomB);
                Assert.IsTrue(pairs.Add((link.RoomA, link.RoomB)), "one link per pair");
                if (i > 0)
                {
                    var before = level.RoomLinks[i - 1];
                    Assert.IsTrue(before.RoomA < link.RoomA || (before.RoomA == link.RoomA && before.RoomB < link.RoomB), "sorted");
                }
            }
            foreach (var t in level.Transitions)
            {
                var key = (Mathf.Min(t.PlotA, t.PlotB), Mathf.Max(t.PlotA, t.PlotB));
                Assert.IsTrue(pairs.Contains(key), $"transition {t.Id}");
                if (!TransitionTypes.IsTwoWay(t.Type)) continue;
                var link = level.RoomLinks.Find(l => (l.RoomA, l.RoomB) == key);
                Assert.IsTrue(link.AToB && link.BToA, $"two-way transition {t.Id}");
            }
        }

        [Test]
        public void Sites_RoundTripThroughSerialization()
        {
            var level = LevelGenerator.Generate(LevelGenSettings.Default, 9UL).Level;
            Assert.IsNotEmpty(level.ChestSites);
            Assert.IsNotEmpty(level.RoomLinks);
            var copy = LevelDescription.Deserialize(level.Serialize());
            Assert.AreEqual(level.ComputeHash(), copy.ComputeHash());
            Assert.AreEqual(level.ChestSites.Count, copy.ChestSites.Count);
            Assert.AreEqual(level.PickupLines.Count, copy.PickupLines.Count);
            Assert.AreEqual(level.RoomLinks.Count, copy.RoomLinks.Count);

            var site = level.ChestSites[0];
            site.Position += Vector3.up * 0.01f;
            level.ChestSites[0] = site;
            Assert.AreNotEqual(copy.ComputeHash(), level.ComputeHash(), "a site's position is in the hash");
        }

        [Test]
        public void SteppedRun_GivesTheSameLevelStageByStage()
        {
            var s = LevelGenSettings.Default;
            var run = LevelGenerator.Start(s, 6UL);
            var stages = new List<string>();
            while (run.MoveNext())
            {
                Assert.IsNull(run.Result);
                stages.Add(run.Current.Stage);
            }
            Assert.IsTrue(run.Result.Success);
            CollectionAssert.AreEqual(LevelGenerator.Generate(s, 6UL).Level.Serialize(), run.Result.Level.Serialize());
            CollectionAssert.IsSubsetOf(new[] { "Partition", "Buildings", "Terrain", "Props", LevelGenerator.SitesStage }, stages);
            Assert.AreEqual(LevelGenerator.SitesStage, stages[stages.Count - 1]);
        }
    }
}
