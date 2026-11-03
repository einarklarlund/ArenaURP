using UnityEngine;

namespace Arena.LevelGen.Tests
{
    /// <summary>Hand-built levels for metric and validator tests: a 50 × 50 level with ground whose top is y = 0.</summary>
    internal sealed class Fixture
    {
        public const float Size = 50f;

        public readonly LevelDescription Level = new() { Size = new Vector3(Size, 12f, Size) };
        public LevelGenSettings Settings = LevelGenSettings.Default;

        public Fixture()
        {
            Settings.Width = Size;
            Settings.Depth = Size;
            Level.Primitives.Add(new Primitive
            {
                Role = PrimitiveRole.Floor,
                Shape = PrimitiveShape.Slab,
                Position = new Vector3(Size / 2f, -0.5f, Size / 2f),
                Size = new Vector3(Size, 0.5f, Size),
                OwnerKind = OwnerKind.Level,
            });
        }

        /// <summary>An axis-aligned plot outline, with no ground of its own (the level's ground or boxes supply it).</summary>
        public Fixture Plot(int id, float x0, float z0, float x1, float z1, float baseHeight = 0f)
        {
            return Plot(id, baseHeight, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
        }

        public Fixture Plot(int id, float baseHeight, params Vector2[] outline)
        {
            int polygon = Level.AddPolygon(outline);
            Level.Plots.Add(new PlotDescription { Id = id, Polygon = polygon, BaseHeight = baseHeight });
            return this;
        }

        /// <summary>A Prism over <paramref name="outline"/>, from <paramref name="bottom"/> to a top that is
        /// <paramref name="top"/> at the outline's centroid and tilts by <paramref name="slope"/>.</summary>
        public Fixture Prism(PrimitiveRole role, float bottom, float top, Vector2 slope, params Vector2[] outline)
        {
            Level.AddPrism(role, Level.AddPolygon(outline), bottom, top, slope, OwnerKind.Plot, 0,
                TransitionStage.GroundSurfaces);
            return this;
        }

        /// <summary>An axis-aligned box from (x0, y0, z0) to (x1, y1, z1).</summary>
        public Fixture Box(PrimitiveRole role, float x0, float z0, float x1, float z1, float y0, float y1, int plot = 0)
        {
            Level.Primitives.Add(new Primitive
            {
                Role = role,
                Shape = PrimitiveShape.Box,
                Position = new Vector3((x0 + x1) / 2f, y0, (z0 + z1) / 2f),
                Size = new Vector3(x1 - x0, y1 - y0, z1 - z0),
                OwnerKind = OwnerKind.Plot,
                OwnerId = plot,
            });
            return this;
        }

        public Fixture Transition(int id, TransitionType type, int plotA, int plotB)
        {
            Level.Transitions.Add(new TransitionDescription { Id = id, Type = type, PlotA = plotA, PlotB = plotB });
            return this;
        }

        public GenerationReport Measure()
        {
            Settings.PlotCount = Mathf.Max(LevelGenSettings.MinPlots, Level.Plots.Count);
            var options = MeasureOptions.Default;
            options.CollectDebug = true;
            return LevelEvaluator.Evaluate(Level, Settings, options);
        }

        public static float Plot0(GenerationReport report, PlotMetric metric) =>
            report.Plots[0].Metrics[(int)metric].Measured;

        public static ConstraintResult Constraint(GenerationReport report, Constraint constraint) =>
            report.Constraints.Find(c => c.Constraint == constraint);

        /// <summary>The surface at the top of column (x, z) of the level-space point.</summary>
        public static int TopSurfaceAt(GenerationReport report, float x, float z)
        {
            var surfaces = report.Debug.Surfaces;
            int cx = (int)(x / surfaces.Grid.CellSize), cz = (int)(z / surfaces.Grid.CellSize);
            surfaces.ColumnRange(cx, cz, out int start, out int end);
            return end > start ? end - 1 : -1;
        }
    }
}
