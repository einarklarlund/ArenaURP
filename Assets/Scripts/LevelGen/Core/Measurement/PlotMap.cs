using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Which grid cells lie under each plot's polygon, and which plot each surface belongs to. A cell is under a
    /// polygon when its centre is inside it (<see cref="Polygon.Contains(IReadOnlyList{Vector2}, float, float)"/>).
    /// Top-level plots tile the level and sub-plots tile their parent, so a surface belongs to the deepest plot over
    /// its cell, whatever its height: a building's upper floor belongs to the plot it stands on. Sub-plots fold under
    /// their top-level plot through <see cref="SurfaceTopPlot"/>.
    /// </summary>
    public sealed class PlotMap
    {
        public readonly int PlotCount;
        /// <summary>Plot index (position in <see cref="LevelDescription.Plots"/>) of each surface, or -1.</summary>
        public readonly int[] SurfacePlot;
        /// <summary>Index of the top-level plot each surface's plot folds under, or -1.</summary>
        public readonly int[] SurfaceTopPlot;
        /// <summary>Per plot index, the index of its top-level ancestor (itself for a top-level plot).</summary>
        public readonly int[] TopLevel;
        /// <summary>Per plot index, how many parents it has above it (0 for a top-level plot).</summary>
        public readonly int[] Depth;
        /// <summary>Per plot index: no sub-plot names it as parent, so it has ground of its own.</summary>
        public readonly bool[] IsLeaf;
        /// <summary>Cell bounds of each polygon: min inclusive, max exclusive. Empty for a plot with no cells.</summary>
        public readonly RectInt[] Bounds;

        private readonly bool[][] _footprints;
        private readonly int[] _footprintCellCount;
        private readonly int _cellsX;

        public PlotMap(LevelDescription description, SurfaceSet surfaces)
        {
            var grid = surfaces.Grid;
            _cellsX = grid.CellsX;
            PlotCount = description.Plots.Count;
            _footprints = new bool[PlotCount][];
            _footprintCellCount = new int[PlotCount];
            Bounds = new RectInt[PlotCount];

            for (int r = 0; r < PlotCount; r++)
            {
                var polygon = description.PolygonOf(description.Plots[r]);
                var mask = _footprints[r] = new bool[grid.CellCount];
                int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
                for (int z = 0; z < grid.CellsZ; z++)
                for (int x = 0; x < grid.CellsX; x++)
                {
                    if (!Polygon.Contains(polygon, grid.CellCenterX(x), grid.CellCenterZ(z))) continue;
                    mask[x + z * grid.CellsX] = true;
                    _footprintCellCount[r]++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
                Bounds[r] = _footprintCellCount[r] > 0
                    ? new RectInt(minX, minZ, maxX - minX + 1, maxZ - minZ + 1)
                    : new RectInt(0, 0, 0, 0);
            }

            var indexById = new Dictionary<int, int>();
            for (int r = 0; r < PlotCount; r++) indexById[description.Plots[r].Id] = r;
            TopLevel = new int[PlotCount];
            Depth = new int[PlotCount];
            for (int r = 0; r < PlotCount; r++)
            {
                int top = r;
                // Bounded by the plot count, so a parent cycle in a bad description can't hang.
                for (int guard = 0; guard < PlotCount; guard++)
                {
                    int parent = description.Plots[top].ParentId;
                    if (parent < 0 || !indexById.TryGetValue(parent, out int next)) break;
                    top = next;
                    Depth[r]++;
                }
                TopLevel[r] = top;
            }
            IsLeaf = new bool[PlotCount];
            for (int r = 0; r < PlotCount; r++) IsLeaf[r] = true;
            for (int r = 0; r < PlotCount; r++)
            {
                int parent = description.Plots[r].ParentId;
                if (parent >= 0 && indexById.TryGetValue(parent, out int p)) IsLeaf[p] = false;
            }

            SurfacePlot = new int[surfaces.Count];
            SurfaceTopPlot = new int[surfaces.Count];
            for (int s = 0; s < surfaces.Count; s++)
            {
                var surface = surfaces[s];
                int best = -1;
                for (int r = 0; r < PlotCount; r++)
                    if (InPolygon(r, surface.X, surface.Z) && (best < 0 || Depth[r] > Depth[best])) best = r;
                SurfacePlot[s] = best;
                SurfaceTopPlot[s] = best < 0 ? -1 : TopLevel[best];
            }
        }

        public bool IsTopLevel(int plot) => TopLevel[plot] == plot;

        public bool InPolygon(int plot, int x, int z) => _footprints[plot][x + z * _cellsX];

        public int CellCount(int plot) => _footprintCellCount[plot];
    }
}
