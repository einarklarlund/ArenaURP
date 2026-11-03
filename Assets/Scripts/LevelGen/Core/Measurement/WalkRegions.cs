using System.Collections.Generic;

namespace Arena.LevelGen
{
    public struct WalkRegion
    {
        public int Plot;
        /// <summary>Surface ids, in ascending order.</summary>
        public List<int> Surfaces;
        public int Size => Surfaces.Count;
    }

    /// <summary>
    /// Walk regions inside each plot: maximal sets of walkable-role surfaces of one plot joined by 4-neighbour
    /// steps of at most <c>stepHeight</c> with headroom. They stop at the plot's edge, so a flat transition doesn't
    /// merge two plots' ground. Regions are numbered in order of their lowest surface id.
    /// </summary>
    public sealed class WalkRegions
    {
        public readonly List<WalkRegion> Regions = new();
        /// <summary>Region of each surface, or -1 for surfaces outside plots or without a walkable role.</summary>
        public readonly int[] SurfaceRegion;

        /// <param name="outdoor">When given, only these surfaces form regions: a plot's regions are its ground, not
        /// the floors of buildings on it.</param>
        public WalkRegions(SurfaceSet surfaces, PlotMap plots, in PlayerConstants player, bool[] outdoor = null)
        {
            var grid = surfaces.Grid;
            SurfaceRegion = new int[surfaces.Count];
            for (int i = 0; i < SurfaceRegion.Length; i++) SurfaceRegion[i] = -1;

            var queue = new Queue<int>();
            for (int seed = 0; seed < surfaces.Count; seed++)
            {
                int plot = plots.SurfacePlot[seed];
                if (SurfaceRegion[seed] >= 0 || plot < 0 || !surfaces[seed].HasWalkableRole) continue;
                if (outdoor != null && !outdoor[seed]) continue;

                int region = Regions.Count;
                var members = new List<int>();
                SurfaceRegion[seed] = region;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int s = queue.Dequeue();
                    members.Add(s);
                    var a = surfaces[s];
                    foreach (var (dx, dz) in SurfaceSet.Neighbours4)
                    {
                        int nx = a.X + dx, nz = a.Z + dz;
                        if (!grid.InBounds(nx, nz)) continue;
                        surfaces.ColumnRange(nx, nz, out int start, out int end);
                        for (int t = start; t < end; t++)
                        {
                            if (SurfaceRegion[t] >= 0 || plots.SurfacePlot[t] != plot) continue;
                            var b = surfaces[t];
                            if (!b.HasWalkableRole || (outdoor != null && !outdoor[t])) continue;
                            if (System.Math.Abs(b.Height - a.Height) > player.StepHeight) continue;
                            if (!SurfaceSet.HasHeadroom(a, b, player.PlayerHeight)) continue;
                            SurfaceRegion[t] = region;
                            queue.Enqueue(t);
                        }
                    }
                }
                members.Sort();
                Regions.Add(new WalkRegion { Plot = plot, Surfaces = members });
            }
        }

        /// <summary>The plot's largest region (lowest index on ties), or -1 if it has none.</summary>
        public int LargestRegionOf(int plot)
        {
            int best = -1;
            for (int r = 0; r < Regions.Count; r++)
                if (Regions[r].Plot == plot && (best < 0 || Regions[r].Size > Regions[best].Size)) best = r;
            return best;
        }
    }
}
