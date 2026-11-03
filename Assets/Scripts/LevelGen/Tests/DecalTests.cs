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

        private static DecalSettings WithCliffTops()
        {
            var s = DecalSettings.Default;
            s.CliffTops = true;
            return s;
        }

        [Test]
        public void EveryType_Appears_AndEveryStripIsWellFormed()
        {
            var seen = new HashSet<DecalType>();
            for (ulong seed = 0; seed < 6; seed++)
                foreach (var strip in DecalStage.Build(Generate(seed), WithCliffTops()))
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
            for (int t = 0; t < DecalTypes.Count; t++) Assert.IsTrue(seen.Contains((DecalType)t), $"{(DecalType)t} appears");
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

        [Test]
        public void Ledges_LieOnTheHigherPlot()
        {
            int checkedPoints = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                var level = Generate(seed);
                foreach (var strip in DecalStage.Build(level, DecalSettings.Default))
                {
                    if (strip.Type != DecalType.Ledge) continue;
                    // The ledge the strip runs along: one that touches it only at an end (two ledges meeting at a
                    // corner) is another ledge's.
                    foreach (var t in level.Transitions)
                    {
                        if (t.Type != TransitionType.Ledge
                            || !strip.Left.TrueForAll(p => Distance(new Vector2(p.x, p.z), t.From, t.To) <= 1e-3f)) continue;
                        foreach (var p in strip.Left)
                        {
                            var xz = new Vector2(p.x, p.z);
                            // The higher side's ground: its plot's plane, or a ramp or bank beside the ledge.
                            var normal = (t.To - t.From).normalized;
                            normal = new Vector2(-normal.y, normal.x) * 0.05f;
                            float high = Mathf.Max(GroundAt(level, xz + normal), GroundAt(level, xz - normal));
                            Assert.AreEqual(high, p.y, 0.02f, "a ledge decal lies on the higher plot");
                            checkedPoints++;
                        }
                    }
                }
            }
            Assert.Greater(checkedPoints, 0);
        }

        /// <summary>The top of the plot and slope prisms over a point (the ground decals are draped on).</summary>
        private static float GroundAt(LevelDescription level, Vector2 p)
        {
            float best = float.NegativeInfinity;
            foreach (var prim in level.Primitives)
                if (prim.Shape == PrimitiveShape.Prism && prim.OwnerKind is OwnerKind.Plot or OwnerKind.Transition
                    && Polygon.Contains(level.Polygons[prim.Polygon], p))
                    best = Mathf.Max(best, prim.PrismTop(p.x, p.y));
            return best;
        }

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }
    }
}
