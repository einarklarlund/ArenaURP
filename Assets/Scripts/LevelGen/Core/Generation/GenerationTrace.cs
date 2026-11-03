using System.Collections.Generic;

namespace Arena.LevelGen
{
    /// <summary>
    /// What each generator stage produced, for the editor's step-through view, plus retry counts and timings.
    /// Built next to the description; never sent over the network. On failure it holds the closest attempt.
    /// </summary>
    public sealed class GenerationTrace
    {
        /// <summary>Stage 1: per-plot targets.</summary>
        public PlotTargets[] Targets;
        /// <summary>Stage 2: sites, plot polygons and neighbours.</summary>
        public PlotPartition Partition;
        /// <summary>Stage 3: heights, slopes, segments and the plot-graph repairs.</summary>
        public PlotPlan Plots;
        /// <summary>Stage 5: each terrained room's sub-rooms, operations and triangles.</summary>
        public List<TerrainPlan> Terrain;
        /// <summary>Stage 7: the props kept (removed ones are gone from the list).</summary>
        public List<PropPlan> Props;
        /// <summary>Stage 4: one plan per building plot, including dropped ones and why.</summary>
        public List<BuildingPlan> Buildings;
        /// <summary>One line per repair, across stages.</summary>
        public List<string> Repairs = new();
        public List<StageTiming> Timings = new();
        /// <summary>Whole-level attempts; above 1 when an attempt stopped early or broke a hard constraint.</summary>
        public int LevelAttempts = 1;
        /// <summary>Why each earlier attempt was dropped.</summary>
        public List<string> DroppedAttempts = new();

        /// <summary>Attempts beyond the first, across every stage that retries.</summary>
        public int Retries => System.Math.Max(0, LevelAttempts - 1) + System.Math.Max(0, (Partition?.Attempts ?? 1) - 1);

        public void CopyTo(GenerationTrace to)
        {
            to.Targets = Targets;
            to.Partition = Partition;
            to.Plots = Plots;
            to.Buildings = Buildings;
            to.Terrain = Terrain;
            to.Props = Props;
            to.Repairs = Repairs;
            to.Timings = Timings;
            to.LevelAttempts = LevelAttempts;
            to.DroppedAttempts = DroppedAttempts;
        }
    }
}
