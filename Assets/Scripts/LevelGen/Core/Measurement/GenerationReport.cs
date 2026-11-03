using System.Collections.Generic;

namespace Arena.LevelGen
{
    public enum PlotMetric
    {
        ElevationRange,
        ElevationFrequency,
    }

    public enum LevelMetric
    {
        PathStretch,
        ElevationRange,
    }

    public struct MetricResult
    {
        public float Target;
        public float Measured;
        public float Tolerance;
        public float Weight;

        public bool WithinTolerance => System.Math.Abs(Measured - Target) <= Tolerance;

        /// <summary>
        /// <c>weight × max(0, |measured − target| − tolerance)</c>: zero inside the tolerance band and growing linearly
        /// from its edge, with no jump there, so a local-search move that just crosses into the band gains no more
        /// than any other move of the same size.
        /// </summary>
        public float Error => Weight * System.Math.Max(0f, System.Math.Abs(Measured - Target) - Tolerance);

        public static MetricResult Of(float measured, float target, float tolerance, float weight) =>
            new() { Measured = measured, Target = target, Tolerance = tolerance, Weight = weight };
    }

    public sealed class PlotReport
    {
        public int PlotId;
        /// <summary>Ids of the sub-plots folded under this plot, at any depth.</summary>
        public List<int> SubPlotIds = new();
        /// <summary>Indexed by <see cref="PlotMetric"/>.</summary>
        public MetricResult[] Metrics = new MetricResult[PlotMetricCount];

        public const int PlotMetricCount = 2;

        public float Error
        {
            get
            {
                float sum = 0f;
                foreach (var m in Metrics) sum += m.Error;
                return sum;
            }
        }
    }

    /// <summary>Debug layers for overlays. Only filled when <see cref="MeasureOptions.CollectDebug"/> is set.</summary>
    public sealed class MeasurementDebug
    {
        public SpanGrid Grid;
        public SurfaceSet Surfaces;
        public PlotMap Plots;
        public TransitionMap Transitions;
        public BuildingMap Buildings;
        /// <summary>Surfaces that are outdoor ground: plot, transition or level tops, not buildings or props.</summary>
        public bool[] Outdoor;
        public WalkRegions Regions;
        public MovementGraph Graph;
        public Reachability Reachability;
    }

    public struct StageTiming
    {
        public string Stage;
        public double Milliseconds;
    }

    /// <summary>
    /// What came out of a generation and how well it hit its targets. Built next to the description; not
    /// sent over the network.
    /// </summary>
    public sealed class GenerationReport
    {
        /// <summary>One row per top-level plot; sub-plots are measured as part of it.</summary>
        public List<PlotReport> Plots = new();
        public PathStretchResult PathStretch = new();
        /// <summary>Indexed by <see cref="LevelMetric"/>.</summary>
        public MetricResult[] Level = new MetricResult[LevelMetricCount];
        public List<ConstraintResult> Constraints = new();
        public int Retries;
        public int Repairs;
        public List<StageTiming> Timings = new();
        public MeasurementDebug Debug;

        public const int LevelMetricCount = 2;

        public float LevelError
        {
            get
            {
                float sum = 0f;
                foreach (var m in Level) sum += m.Error;
                return sum;
            }
        }

        /// <summary>The single number that improvement and candidate selection minimize.</summary>
        public float TotalError
        {
            get
            {
                float sum = LevelError;
                foreach (var plot in Plots) sum += plot.Error;
                return sum;
            }
        }

        public bool AllConstraintsPassed
        {
            get
            {
                foreach (var c in Constraints)
                    if (!c.Passed) return false;
                return true;
            }
        }
    }
}
