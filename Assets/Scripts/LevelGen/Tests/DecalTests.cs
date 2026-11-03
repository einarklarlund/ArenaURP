using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class DecalTests
    {
        private static LevelDescription Generate(ulong seed)
        {
            var result = LevelGenerator.Generate(LevelGenSettings.Default, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        [Test]
        public void EveryType_Appears_AndEveryStripIsWellFormed()
        {
            var seen = new HashSet<DecalType>();
            for (ulong seed = 0; seed < 6; seed++)
                foreach (var strip in DecalStage.Build(Generate(seed), DecalSettings.Default))
                {
                    seen.Add(strip.Type);
                    Assert.GreaterOrEqual(strip.Points.Count, 2);
                    Assert.AreEqual(strip.Points.Count, strip.Left.Count);
                    Assert.AreEqual(strip.Points.Count, strip.Right.Count);
                    Assert.Greater(strip.Width, 0f);
                    Assert.AreEqual(1f, strip.Normal.magnitude, 1e-4f);
                    Assert.AreEqual(DecalTypes.IsBuilding(strip.Type), strip.Building >= 0, $"{strip.Type} names its building");
                    foreach (var p in strip.Left) Assert.IsFalse(float.IsNaN(p.y) || float.IsInfinity(p.y));
                }
            for (int t = 0; t < DecalTypes.Count; t++)
            {
                // The blended ground draws ledges and sub-room edges itself.
                bool drawn = (DecalType)t is not (DecalType.Ledge or DecalType.DipEdge or DecalType.MoundEdge);
                Assert.AreEqual(drawn, seen.Contains((DecalType)t), $"{(DecalType)t} {(drawn ? "appears" : "doesn't appear")}");
            }
        }

        [Test]
        public void DecalPass_IsPure()
        {
            var level = Generate(4);
            ulong hash = level.ComputeHash();
            var a = DecalStage.Build(level, DecalSettings.Default);
            var b = DecalStage.Build(level, DecalSettings.Default);
            Assert.AreEqual(hash, level.ComputeHash(), "the pass leaves the description alone");
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Type, b[i].Type);
                CollectionAssert.AreEqual(a[i].Left, b[i].Left);
                CollectionAssert.AreEqual(a[i].Right, b[i].Right);
            }
        }

        [Test]
        public void Frames_OnBothFaces_OfEveryDoorAndWindow([Values(0, 1, 2, 3)] int seed)
        {
            var level = Generate((ulong)seed);
            var settings = DecalSettings.Default;
            var strips = DecalStage.Build(level, settings);
            float w = settings.Width(DecalType.DoorFrame);
            foreach (var b in level.Buildings)
            {
                int doors = 0, windows = 0;
                foreach (var o in b.Openings)
                    if (o.Door) doors += 6; // a jamb each side and a head, on each face
                    else windows += 8; // and a sill
                foreach (var wall in b.Walls)
                {
                    float length = (wall.To - wall.From).magnitude;
                    foreach (var gap in wall.Gaps)
                        doors += 2 * (1 + (gap.x - w >= 0f ? 1 : 0) + (gap.y + w <= length ? 1 : 0));
                }
                Assert.AreEqual(doors, strips.FindAll(s => s.Building == b.Id && s.Type == DecalType.DoorFrame).Count, $"building {b.Id} door frames");
                Assert.AreEqual(windows, strips.FindAll(s => s.Building == b.Id && s.Type == DecalType.WindowFrame).Count, $"building {b.Id} window frames");
            }
        }

        [Test]
        public void FloorSkirting_StopsAtDoors([Values(0, 1, 2, 3)] int seed)
        {
            var level = Generate((ulong)seed);
            var strips = DecalStage.Build(level, DecalSettings.Default);
            foreach (var b in level.Buildings)
                foreach (var o in b.Openings)
                {
                    if (!o.Door) continue;
                    var footprint = level.Polygons[b.FloorFootprints[o.Floor]];
                    var a = footprint[o.Side];
                    var dir = (footprint[(o.Side + 1) % footprint.Count] - a).normalized;
                    var door = a + dir * o.T;
                    float y = b.FloorY(o.Floor);
                    foreach (var strip in strips)
                    {
                        if (strip.Type != DecalType.FloorSkirting || strip.Building != b.Id) continue;
                        if (Mathf.Abs(strip.Points[0].y - y) > 0.01f) continue;
                        // Sample the strip's middle line: nothing of it lies in the doorway.
                        for (int i = 0; i + 1 < strip.Points.Count; i++)
                            for (int k = 0; k <= 10; k++)
                            {
                                var p3 = Vector3.Lerp(strip.Points[i], strip.Points[i + 1], k / 10f);
                                var p = new Vector2(p3.x, p3.z) - door;
                                bool inDoorway = Mathf.Abs(Vector2.Dot(p, dir)) < o.HalfWidth - 0.05f
                                    && Mathf.Abs(dir.x * p.y - dir.y * p.x) < 0.5f;
                                Assert.IsFalse(inDoorway, $"building {b.Id} floor {o.Floor}: skirting across a door");
                            }
                    }
                }
        }
    }
}
