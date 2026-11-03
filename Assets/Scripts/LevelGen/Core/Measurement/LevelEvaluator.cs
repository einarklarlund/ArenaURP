using System.Collections.Generic;
using System.Diagnostics;

namespace Arena.LevelGen
{
    public sealed class ValidationResult
    {
        public List<ConstraintResult> Constraints;
        public SurfaceSet Surfaces;
        public WalkRegions Regions;
        public Reachability Reachability;

        public int Failures => Constraints.FindAll(c => !c.Passed).Count;

        /// <summary>Whether the plot's largest walk region is in R and can get back to the reference.</summary>
        public bool PlotIsSound(int plot)
        {
            int region = Regions.LargestRegionOf(plot);
            if (region < 0) return false;
            int surface = Regions.Regions[region].Surfaces[0];
            return Reachability.InR[surface] && !Reachability.Sink[surface];
        }
    }

    /// <summary>Rasterizes a description, runs the validator, measures every metric and fills a report. Path stretch
    /// samples only surfaces in R, so validation comes first.</summary>
    public static class LevelEvaluator
    {
        public static GenerationReport Evaluate(LevelDescription description, LevelGenSettings settings,
            MeasureOptions options)
        {
            settings = settings.Sanitized();
            var player = settings.Player;
            var report = new GenerationReport();
            var timer = Stopwatch.StartNew();

            var grid = Rasterizer.Rasterize(description, settings.CellSize);
            Lap(report, timer, "Rasterize");

            var surfaces = new SurfaceSet(grid, player.PlayerHeight);
            var plots = new PlotMap(description, surfaces);
            var transitions = new TransitionMap(description, surfaces);
            var outdoor = PathStretch.OutdoorSurfaces(description, surfaces);
            var regions = new WalkRegions(surfaces, plots, player, outdoor);
            var graph = new MovementGraph(surfaces, player);
            var buildings = new BuildingMap(description, surfaces, graph, player);
            Lap(report, timer, "Surfaces & graph");

            if (options.CollectDebug)
            {
                report.Debug = new MeasurementDebug
                {
                    Grid = grid, Surfaces = surfaces, Plots = plots, Transitions = transitions, Buildings = buildings,
                    Regions = regions, Graph = graph, Outdoor = outdoor,
                };
            }

            report.Constraints = Validator.Validate(description, settings, surfaces, plots, transitions, buildings, regions,
                graph, out var reachability);
            if (report.Debug != null) report.Debug.Reachability = reachability;
            Lap(report, timer, "Validate");

            for (int r = 0; r < plots.PlotCount; r++)
            {
                if (!plots.IsTopLevel(r)) continue;
                var plot = description.Plots[r];
                var t = plot.Targets;
                var plotReport = new PlotReport { PlotId = plot.Id };
                for (int sub = 0; sub < plots.PlotCount; sub++)
                    if (sub != r && plots.TopLevel[sub] == r) plotReport.SubPlotIds.Add(description.Plots[sub].Id);
                var m = plotReport.Metrics;
                m[(int)PlotMetric.ElevationRange] = Of(
                    Metrics.ElevationRange(r, surfaces, plots, outdoor, settings.MaxHeight), t.ElevationRange, settings.ElevationRange);
                m[(int)PlotMetric.ElevationFrequency] = Of(
                    Metrics.ElevationFrequency(r, surfaces, plots, outdoor, player, options.ElevationFrequencyScale),
                    t.ElevationFrequency, settings.ElevationFrequency);
                report.Plots.Add(plotReport);
            }
            Lap(report, timer, "Plot metrics");

            report.Level[(int)LevelMetric.ElevationRange] = Of(
                Metrics.LevelElevationRange(description, settings.MaxHeight), settings.LevelElevationRange);
            var stretch = report.PathStretch = PathStretch.Measure(surfaces, plots, graph, outdoor, reachability.InR,
                options.PathStretchStride);
            var st = settings.PathStretch;
            report.Level[(int)LevelMetric.PathStretch] = MetricResult.Of(stretch.Mean, st.Target, st.Tolerance, st.Weight);
            Lap(report, timer, "Path stretch");

            return report;
        }

        /// <summary>The hard constraints alone, for the generator's repair loop: no metrics. Without
        /// <paramref name="overlaps"/>, the overlap check (most of the cost) is skipped and passes.</summary>
        public static ValidationResult Validate(LevelDescription description, LevelGenSettings settings, bool overlaps = true)
        {
            StageProfiler.Begin("Validate");
            settings = settings.Sanitized();
            var player = settings.Player;
            StageProfiler.Begin("Rasterize");
            var grid = Rasterizer.Rasterize(description, settings.CellSize);
            StageProfiler.End();
            StageProfiler.Begin("Surfaces & graph");
            var surfaces = new SurfaceSet(grid, player.PlayerHeight);
            var plots = new PlotMap(description, surfaces);
            var transitions = new TransitionMap(description, surfaces);
            var regions = new WalkRegions(surfaces, plots, player, PathStretch.OutdoorSurfaces(description, surfaces));
            var graph = new MovementGraph(surfaces, player);
            var buildings = new BuildingMap(description, surfaces, graph, player);
            StageProfiler.End();
            StageProfiler.Begin("Validator");
            var constraints = Validator.Validate(description, settings, surfaces, plots, transitions, buildings, regions,
                graph, out var reachability, overlaps);
            StageProfiler.End();
            StageProfiler.End();
            return new ValidationResult
            {
                Constraints = constraints, Surfaces = surfaces, Regions = regions, Reachability = reachability,
            };
        }

        private static MetricResult Of(float measured, float plotTarget, in MetricTarget config) =>
            MetricResult.Of(measured, plotTarget, config.Tolerance, config.Weight);

        private static MetricResult Of(float measured, in LevelMetricTarget config) =>
            MetricResult.Of(measured, config.Target, config.Tolerance, config.Weight);

        private static void Lap(GenerationReport report, Stopwatch timer, string stage)
        {
            report.Timings.Add(new StageTiming { Stage = stage, Milliseconds = timer.Elapsed.TotalMilliseconds });
            timer.Restart();
        }
    }
}
