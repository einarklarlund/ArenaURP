using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    /// <summary>
    /// Hard constraint 7 (the design's "No overlapping geometry"): the check itself on hand-made solids, then each of the
    /// rules on generated levels.
    /// </summary>
    public class OverlapTests
    {
        private static Primitive Box(Vector3 min, Vector3 max, OwnerKind owner = OwnerKind.Level, int id = 0) => new()
        {
            Role = PrimitiveRole.Cover, Shape = PrimitiveShape.Box,
            Position = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f),
            Size = max - min, OwnerKind = owner, OwnerId = id,
        };

        private static LevelDescription Of(params Primitive[] primitives)
        {
            var level = new LevelDescription();
            level.Primitives.AddRange(primitives);
            return level;
        }

        // ------------------------------------------------------------------ the check

        [Test]
        public void Check_FindsSolidsThatInterpenetrate()
        {
            var found = OverlapCheck.Find(Of(Box(Vector3.zero, Vector3.one), Box(new Vector3(0.9f, 0f, 0f), new Vector3(2f, 1f, 1f))));
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(0.1, found[0].Volume, 1e-4);
        }

        [Test]
        public void Check_AcceptsSolidsThatOnlyTouch()
        {
            // Side by side, stacked, and corner to corner.
            Assert.AreEqual(0, OverlapCheck.Find(Of(
                Box(Vector3.zero, Vector3.one),
                Box(new Vector3(1f, 0f, 0f), new Vector3(2f, 1f, 1f)),
                Box(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f)),
                Box(new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f)))).Count);
        }

        [Test]
        public void Check_FindsAFaceLyingOnAnother()
        {
            // A sheet too thin to count by volume, lying on a box's top: the same plane, facing the same way.
            var found = OverlapCheck.Find(Of(Box(Vector3.zero, Vector3.one), Box(new Vector3(0f, 0.99995f, 0f), new Vector3(1f, 1f, 1f))));
            Assert.AreEqual(1, found.Count);
            Assert.Greater(found[0].Area, 0.9);
        }

        [Test]
        public void Check_IgnoresRoundingAlongASharedFace()
        {
            // Two boxes crossing their shared face by a float's rounding.
            Assert.AreEqual(0, OverlapCheck.Find(Of(
                Box(new Vector3(40f, 0f, 40f), new Vector3(41.000004f, 3f, 43f)),
                Box(new Vector3(41f, 0f, 40f), new Vector3(42f, 3f, 43f)))).Count);
        }

        [Test]
        public void Check_IsAHardConstraint()
        {
            var level = Of(Box(Vector3.zero, Vector3.one), Box(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 1f, 1f)));
            var result = OverlapCheck.Check(level);
            Assert.IsFalse(result.Passed);
            Assert.AreEqual(Constraint.NoOverlap, result.Constraint);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, result.Items);
        }

        // ------------------------------------------------------------------ the rules, on generated levels

        private static LevelDescription Generate(LevelGenSettings s, ulong seed)
        {
            var result = LevelGenerator.Generate(s, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        private static float GroundAt(LevelDescription level, int plot, Vector2 p)
        {
            // The highest ground prism of the plot over the point.
            float best = float.NegativeInfinity;
            foreach (var q in level.Primitives)
                if (q.OwnerKind == OwnerKind.Plot && q.OwnerId == plot && q.Shape == PrimitiveShape.Prism
                    && Polygon.Contains(level.Polygons[q.Polygon], p))
                    best = Mathf.Max(best, q.PrismTop(p.x, p.y));
            return best;
        }

        [Test]
        public void Slopes_CarveTheGround_AndMeetItExactly([Values(0, 1, 2)] int seed)
        {
            // Ramps and banks are carved out of the ground under them, and their top meets the higher plot's ground
            // along the face: no step where the slope reaches the top.
            var level = Generate(LevelGenSettings.Default, (ulong)seed);
            foreach (var o in OverlapCheck.Find(level))
                Assert.Fail($"{OverlapCheck.Describe(level, o.A)} overlaps {OverlapCheck.Describe(level, o.B)} at {o.Where}");
            int checkedCorners = 0;
            foreach (var t in level.Transitions)
            {
                if (t.Type is not (TransitionType.Ramp or TransitionType.Bank)) continue;
                var mid = (t.From + t.To) * 0.5f;
                var dir = (t.To - t.From).normalized;
                var normal = new Vector2(-dir.y, dir.x);
                float a = GroundAt(level, t.PlotA, mid + normal * 0.2f), b = GroundAt(level, t.PlotB, mid - normal * 0.2f);
                int high = !float.IsInfinity(a) && (float.IsInfinity(b) || a > b) ? t.PlotA : t.PlotB;
                var plot = level.Plots[high];
                var centroid = Polygon.Centroid(level.Polygons[plot.Polygon]);
                foreach (var p in level.Primitives)
                {
                    if (p.OwnerKind != OwnerKind.Transition || p.OwnerId != t.Id || p.Surfaces.Top == SurfaceTag.RampSide) continue;
                    foreach (var v in level.Polygons[p.Polygon])
                    {
                        if (Mathf.Abs(Vector2.Dot(v - t.From, normal)) > 1e-3f) continue;
                        float ground = plot.BaseHeight + Vector2.Dot(plot.Slope, v - centroid);
                        Assert.AreEqual(ground, p.PrismTop(v.x, v.y), 2e-3f, $"transition {t.Id}'s top at the face");
                        checkedCorners++;
                    }
                }
            }
            Assert.Greater(checkedCorners, 0);
        }

        [Test]
        public void WallBands_MitreAtCorners_WithoutOverlapping()
        {
            // A chamfered footprint (135° corners) and a square: the bands tile the ring between the outset and the
            // inset outline, so walls neither overlap nor leave gaps at corners.
            var square = new List<Vector2> { new(0f, 0f), new(6f, 0f), new(6f, 4f), new(0f, 4f) };
            var chamfered = new List<Vector2> { new(1f, 0f), new(5f, 0f), new(6f, 1f), new(6f, 3f), new(5f, 4f), new(1f, 4f), new(0f, 3f), new(0f, 1f) };
            foreach (var outline in new[] { square, chamfered })
            {
                var bands = BuildingStage.Bands(outline, true, 0.15f, 0.15f);
                float sum = 0f;
                for (int i = 0; i < bands.Count; i++)
                {
                    Assert.IsTrue(Polygon.IsConvex(bands[i]), $"band {i} is convex and counter-clockwise");
                    sum += Polygon.Area(bands[i]);
                    for (int j = i + 1; j < bands.Count; j++)
                        Assert.Less(Polygon.Area(Polygon.ClipConvex(bands[i], bands[j], true)), 1e-4f, $"bands {i} and {j}");
                }
                float ring = Polygon.Area(Polygon.Outset(outline, 0.15f)) - Polygon.Area(Polygon.Inset(outline, 0.15f));
                Assert.AreEqual(ring, sum, 1e-3f);
            }
        }

        private static IEnumerable<(LevelDescription level, BuildingDescription building)> Buildings(LevelGenSettings s, int seeds)
        {
            for (ulong seed = 0; seed < (ulong)seeds; seed++)
            {
                var level = Generate(s, seed);
                foreach (var b in level.Buildings) yield return (level, b);
            }
        }

        private static List<Overlap> OverlapsOf(LevelDescription level, int building) =>
            OverlapCheck.Find(level).FindAll(o =>
                level.Primitives[o.A].OwnerKind == OwnerKind.Building && level.Primitives[o.A].OwnerId == building
                || level.Primitives[o.B].OwnerKind == OwnerKind.Building && level.Primitives[o.B].OwnerId == building);

        [Test]
        public void Slabs_StopAtTheWallsInnerFaces()
        {
            int slabs = 0;
            foreach (var (level, b) in Buildings(LevelGenSettings.Default, 3))
            {
                Assert.AreEqual(0, OverlapsOf(level, b.Id).Count, $"building {b.Id}");
                var inside = Polygon.Inset(level.Polygons[b.Footprint], BuildingStage.WallThickness * 0.5f);
                foreach (var p in level.Primitives)
                {
                    if (p.OwnerKind != OwnerKind.Building || p.OwnerId != b.Id || p.Surfaces.Top != SurfaceTag.BuildingFloor
                        || Mathf.Abs(p.PrismTop(0f, 0f) - b.FloorY(0)) > 1e-3f) continue;
                    foreach (var v in level.Polygons[p.Polygon])
                        Assert.IsTrue(Polygon.Contains(Polygon.Outset(inside, 1e-3f), v), "the first floor's slab is inside the walls");
                    slabs++;
                }
            }
            Assert.Greater(slabs, 0);
        }

        [Test]
        public void Thresholds_FillTheDoorway_UpToTheFloor()
        {
            // A raised first floor's doors: the wall under each runs from the ground to the floor, between the wall's
            // faces, and nothing overlaps it.
            var s = LevelGenSettings.Default;
            // No wider than the widest doorway (a spine-wide entrance) through the wall's thickness.
            float doorway = s.Sanitized().Buildings.SpineWidth * BuildingStage.WallThickness + 1e-3f;
            int thresholds = 0;
            foreach (var (level, b) in Buildings(s, 4))
            {
                if (b.Placement != FirstFloor.Raised) continue;
                Assert.AreEqual(0, OverlapsOf(level, b.Id).Count, $"building {b.Id}");
                foreach (var p in level.Primitives)
                    if (p.OwnerKind == OwnerKind.Building && p.OwnerId == b.Id && p.Role == PrimitiveRole.Wall
                        && Mathf.Abs(p.PrismTop(0f, 0f) - b.FloorY(0)) < 1e-3f && Polygon.Area(level.Polygons[p.Polygon]) < doorway)
                        thresholds++;
            }
            Assert.Greater(thresholds, 0);
        }

        [Test]
        public void Parapets_StopAtTheUpperFloorsWalls()
        {
            // Tall buildings, many with a smaller floor on a terrace: the terrace's parapet stands on the walls below
            // and gives way to the upper floor's walls.
            var s = LevelGenSettings.Default;
            s.MaxHeight = 20f;
            s.Buildings.MinFloors = s.Buildings.MaxFloors = 3;
            int parapets = 0;
            foreach (var (level, b) in Buildings(s, 4))
            {
                Assert.AreEqual(0, OverlapsOf(level, b.Id).Count, $"building {b.Id}");
                foreach (var p in level.Primitives)
                    if (p.OwnerKind == OwnerKind.Building && p.OwnerId == b.Id && p.Role == PrimitiveRole.Railing
                        && p.Surfaces.Top == SurfaceTag.Railing && p.Position.y < b.RoofY - 1e-3f)
                        parapets++;
            }
            Assert.Greater(parapets, 0);
        }

        [Test]
        public void Props_StandOnLevelGround()
        {
            for (ulong seed = 0; seed < 3; seed++)
            {
                var level = Generate(LevelGenSettings.Default, seed);
                foreach (var o in OverlapCheck.Find(level))
                    Assert.IsFalse(level.Primitives[o.A].OwnerKind == OwnerKind.Prop || level.Primitives[o.B].OwnerKind == OwnerKind.Prop,
                        $"{OverlapCheck.Describe(level, o.A)} overlaps {OverlapCheck.Describe(level, o.B)}");
                Assert.Greater(level.Props.Count, 0);
            }
        }
    }
}
