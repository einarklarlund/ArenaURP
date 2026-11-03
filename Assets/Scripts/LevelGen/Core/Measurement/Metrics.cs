using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>Knobs for measurement that are not part of the level's identity.</summary>
    public struct MeasureOptions
    {
        /// <summary>Path stretch samples every k-th cell in x and z. The improve stage uses a sparser lattice.</summary>
        public int PathStretchStride;
        /// <summary>Raw ledge length per walkable area (1/units) that counts as fully choppy (1.0). Calibrate
        /// with the batch runner's sweep.</summary>
        public float ElevationFrequencyScale;
        /// <summary>Keep per-sample and per-cell data for overlays.</summary>
        public bool CollectDebug;

        public static MeasureOptions Default => new()
        {
            PathStretchStride = 8,
            ElevationFrequencyScale = 1f,
            CollectDebug = false,
        };
    }

    /// <summary>
    /// The metric functions: pure functions of the span grid, the plot map and the movement graph. Every value is in
    /// 0..1. Plot metrics are per top-level plot, with its sub-plots folded in, and read walkable outdoor surfaces only.
    /// Path stretch is in <see cref="PathStretch"/>.
    /// </summary>
    public static class Metrics
    {
        /// <summary>(highest − lowest walkable surface on the top-level plot) ÷ maxHeight.</summary>
        public static float ElevationRange(int plot, SurfaceSet surfaces, PlotMap plots, bool[] outdoor, float maxHeight)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int s = 0; s < surfaces.Count; s++)
            {
                if (!OnPlotWalkable(s, plot, surfaces, plots, outdoor)) continue;
                float h = surfaces[s].Height;
                if (h < min) min = h;
                if (h > max) max = h;
            }
            return max >= min ? Mathf.Clamp01((max - min) / maxHeight) : 0f;
        }

        /// <summary>
        /// Ledge length per walkable area, normalized by <paramref name="scale"/> and clamped to 0..1. A ledge is
        /// an edge between 4-neighbouring walkable surfaces of the plot whose heights differ by more than a step.
        /// Each surface is paired with the closest-height surface in the next column.
        /// </summary>
        public static float ElevationFrequency(int plot, SurfaceSet surfaces, PlotMap plots, bool[] outdoor,
            in PlayerConstants player, float scale)
        {
            var grid = surfaces.Grid;
            int walkable = 0, ledges = 0;
            for (int s = 0; s < surfaces.Count; s++)
            {
                if (!OnPlotWalkable(s, plot, surfaces, plots, outdoor)) continue;
                walkable++;
                var a = surfaces[s];
                for (int d = 0; d < 2; d++)
                {
                    int nx = a.X + (d == 0 ? 1 : 0), nz = a.Z + (d == 1 ? 1 : 0);
                    if (!grid.InBounds(nx, nz)) continue;
                    surfaces.ColumnRange(nx, nz, out int start, out int end);
                    float closest = float.PositiveInfinity;
                    for (int t = start; t < end; t++)
                        if (OnPlotWalkable(t, plot, surfaces, plots, outdoor))
                            closest = Mathf.Min(closest, Mathf.Abs(surfaces[t].Height - a.Height));
                    if (!float.IsPositiveInfinity(closest) && closest > player.StepHeight) ledges++;
                }
            }
            if (walkable == 0 || scale <= 0f) return 0f;
            float raw = ledges * grid.CellSize / (walkable * grid.CellSize * grid.CellSize);
            return Mathf.Clamp01(raw / scale);
        }

        /// <summary>(max − min top-level plot base height) ÷ maxHeight. Sub-plots don't count.</summary>
        public static float LevelElevationRange(LevelDescription description, float maxHeight)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var plot in description.Plots)
            {
                if (plot.ParentId >= 0) continue;
                min = Mathf.Min(min, plot.BaseHeight);
                max = Mathf.Max(max, plot.BaseHeight);
            }
            return max >= min ? Mathf.Clamp01((max - min) / maxHeight) : 0f;
        }

        /// <summary>Walkable outdoor ground of the plot: building floors and props don't shape the terrain metrics.
        /// A null <paramref name="outdoor"/> counts every walkable surface.</summary>
        private static bool OnPlotWalkable(int surface, int plot, SurfaceSet surfaces, PlotMap plots, bool[] outdoor) =>
            plots.SurfaceTopPlot[surface] == plot && surfaces[surface].HasWalkableRole && (outdoor == null || outdoor[surface]);
    }
}
