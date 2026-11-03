using NUnit.Framework;

namespace Arena.LevelGen.Tests
{
    public class GuaranteeTests
    {
        internal static LevelGenSettings Preset(int preset)
        {
            var s = LevelGenSettings.Default;
            switch (preset)
            {
                case 1: // small
                    s.Width = s.Depth = 50f;
                    s.PlotCount = 4;
                    break;
                case 2: // dense
                    s.Width = s.Depth = 100f;
                    s.PlotCount = 8;
                    break;
                case 3: // tall: steep terrain (a 7-unit level range, about 5 within plots) and 3-floor buildings
                    s.MaxHeight = 20f;
                    s.LevelElevationRange.Target = 0.36f;
                    s.ElevationRange.Target = 0.24f;
                    s.Buildings.MinFloors = 3;
                    s.Buildings.MaxFloors = 3;
                    break;
                case 4: // small with full-plot buildings
                    s.Width = s.Depth = 50f;
                    s.PlotCount = 4;
                    s.Buildings.FullPlotOdds = 1f;
                    break;
            }
            return s;
        }

        [Test]
        public void GeneratedLevels_PassEveryHardConstraint([Values(0, 1, 2, 3, 4)] int preset)
        {
            var s = Preset(preset);
            for (ulong seed = 0; seed < 12; seed++)
            {
                var result = LevelGenerator.Generate(s, seed);
                Assert.IsTrue(result.Success, $"preset {preset} seed {seed}: {result.Reason}");
                var check = LevelEvaluator.Validate(result.Level, s);
                Assert.AreEqual(0, check.Failures, $"preset {preset} seed {seed}");
                Assert.AreEqual(s.PlotCount, result.Level.Plots.FindAll(p => p.ParentId < 0).Count);
                Assert.AreNotEqual(BuildingMix.None, result.Level.Mix, "every level draws a building mix");
                Assert.GreaterOrEqual(result.Level.Primitives.FindAll(p => p.OwnerKind == OwnerKind.Boundary).Count, 4);
            }
        }

        [Test]
        public void SettingsThatCantFit_AreRefusedWithAReason()
        {
            var s = LevelGenSettings.Default;
            s.Width = s.Depth = 50f;
            s.PlotCount = 6;
            s.Plots.MinPlotSide = 20f;
            var result = LevelGenerator.Generate(s, 0UL);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.Attempts);
            StringAssert.Contains("don't fit", result.Reason);
        }

        [Test]
        public void LevelElevationRange_IsSpreadOverTheTarget()
        {
            var s = LevelGenSettings.Default;
            var level = LevelGenerator.Generate(s, 3UL).Level;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var plot in level.Plots)
            {
                var polygon = level.PolygonOf(plot);
                foreach (var v in polygon)
                {
                    float h = plot.BaseHeight + UnityEngine.Vector2.Dot(plot.Slope, v - Polygon.Centroid(polygon));
                    min = System.Math.Min(min, h);
                    max = System.Math.Max(max, h);
                }
            }
            Assert.AreEqual(0f, min, 1e-3f, "the lowest plot sits at 0");
            Assert.Greater(level.Primitives.FindAll(p => p.OwnerKind == OwnerKind.Boundary).Count, 4, "cut cells bend the outline");
            Assert.GreaterOrEqual(max, s.LevelElevationRange.Target * s.MaxHeight - s.Plots.HeightStep);
        }
    }
}
