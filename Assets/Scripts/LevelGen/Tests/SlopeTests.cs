using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class SlopeTests
    {
        [Test]
        public void Profile_LeavesAndMeetsLevelGround_AtTheSteepestGradeAllowed([Values(0f, 0.25f, 0.5f)] float f)
        {
            Assert.AreEqual(0f, TransitionStage.Profile(0f, f), 1e-5f);
            Assert.AreEqual(1f, TransitionStage.Profile(1f, f), 1e-5f);
            const float dv = 1e-3f;
            float steepest = 0f, previous = 0f;
            for (float v = dv; v <= 1f + 1e-6f; v += dv)
            {
                float p = TransitionStage.Profile(Mathf.Min(v, 1f), f);
                Assert.GreaterOrEqual(p, previous - 1e-6f, "never falls");
                steepest = Mathf.Max(steepest, (p - previous) / dv);
                previous = p;
            }
            Assert.AreEqual(1f / (1f - f * 0.5f), steepest, 0.01f, "the steepest grade is the average over (1 - f / 2)");
            if (f > 0f)
            {
                Assert.Less(TransitionStage.Profile(dv, f) / dv, 0.01f, "level at the foot (sag)");
                Assert.Less((1f - TransitionStage.Profile(1f - dv, f)) / dv, 0.01f, "level at the top (crest)");
            }
        }

        [Test]
        public void GeneratedSlopes_AreBandedFlaredAndWalkable([Values(0, 1, 2, 3)] int seed)
        {
            var s = LevelGenSettings.Default;
            var result = LevelGenerator.Generate(s, (ulong)seed);
            Assert.IsTrue(result.Success, result.Reason);
            s = s.Sanitized();
            float maxGrade = Mathf.Tan(s.TransitionSizes.RampMaxSlopeDegrees * Mathf.Deg2Rad) * 1.02f + 1e-3f;
            // A flare falls sideways at the flare grade while following the band's grade along the ramp.
            float flare = Mathf.Tan(s.TransitionSizes.FlareSlopeDegrees * Mathf.Deg2Rad);
            float flareGrade = Mathf.Sqrt(flare * flare + maxGrade * maxGrade) * 1.05f;
            foreach (var t in result.Level.Transitions)
            {
                if (t.Type != TransitionType.Ramp) continue;
                var prisms = result.Level.Primitives.FindAll(p => p.OwnerKind == OwnerKind.Transition && p.OwnerId == t.Id);
                // A ramp gives way to the slopes made before it, so one that crosses an earlier one has fewer pieces;
                // what holds is that it has a top.
                Assert.GreaterOrEqual(prisms.FindAll(p => p.Surfaces.Top == SurfaceTag.Ramp).Count, 1, $"ramp {t.Id} has a top");
                // Each band and flare is cut into triangles at the foot's breaks (and round earlier slopes), so the count
                // varies; what holds is that the pieces tile the slope, never lying over one another.
                for (int i = 0; i < prisms.Count; i++)
                    for (int j = i + 1; j < prisms.Count; j++)
                        Assert.Less(Polygon.Area(Polygon.ClipConvex(result.Level.Polygons[prisms[i].Polygon],
                            result.Level.Polygons[prisms[j].Polygon], true)), 1e-3f, $"ramp {t.Id}: pieces {i} and {j} share no area");
                foreach (var p in prisms) Assert.LessOrEqual(p.Slope.magnitude, flareGrade);
                // The bands (the walkable part) keep the ramp's grade: the steepest prisms beyond it are flares.
                int walkable = prisms.FindAll(p => p.Slope.magnitude <= maxGrade).Count;
                Assert.Greater(walkable, 0);
            }
            Assert.AreEqual(0, LevelEvaluator.Validate(result.Level, s).Failures, "every slope is reachable both ways");
        }
    }
}
