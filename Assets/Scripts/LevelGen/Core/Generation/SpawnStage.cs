using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Spawn points, picked from a validated level's outdoor ground. A candidate is ground (a Floor surface) in a plot's
    /// outdoor walk region, in R and not a sink, under open sky, and with ground of the same region within a step
    /// all round it for <see cref="ClearCells"/> cells, so nothing (a fence, a car, a cliff edge) is at a player's
    /// elbow. They are spread by farthest-point sampling from the candidate nearest the level's middle, and face it.
    /// </summary>
    public static class SpawnStage
    {
        /// <summary>Cells of clear ground needed on every side: a capsule's radius and a little more.</summary>
        public const int ClearCells = 2;

        public static List<SpawnPoint> Pick(LevelDescription level, ValidationResult check, in LevelGenSettings s)
        {
            var surfaces = check.Surfaces;
            var grid = surfaces.Grid;
            var regions = check.Regions;
            var reach = check.Reachability;
            float step = s.Player.StepHeight;
            var candidates = new List<int>();
            for (int i = 0; i < surfaces.Count; i++)
            {
                var a = surfaces[i];
                int region = regions.SurfaceRegion[i];
                if (region < 0 || a.Role != PrimitiveRole.Floor || !float.IsPositiveInfinity(a.Ceiling)) continue;
                if (!reach.InR[i] || reach.Sink[i]) continue;
                bool clear = true;
                for (int dz = -ClearCells; dz <= ClearCells && clear; dz++)
                for (int dx = -ClearCells; dx <= ClearCells && clear; dx++)
                {
                    int x = a.X + dx, z = a.Z + dz;
                    if (!grid.InBounds(x, z)) { clear = false; break; }
                    surfaces.ColumnRange(x, z, out int start, out int end);
                    bool found = false;
                    for (int t = start; t < end && !found; t++)
                        found = regions.SurfaceRegion[t] == region && float.IsPositiveInfinity(surfaces[t].Ceiling)
                            && Mathf.Abs(surfaces[t].Height - a.Height) <= step;
                    clear = found;
                }
                if (clear) candidates.Add(i);
            }

            var points = new List<SpawnPoint>();
            int count = Mathf.Min(s.SpawnPoints, candidates.Count);
            if (count == 0) return points;

            Vector2 Plan(int i) => new(grid.CellCenterX(surfaces[i].X), grid.CellCenterZ(surfaces[i].Z));
            var middle = new Vector2(s.Width, s.Depth) * 0.5f;
            int first = candidates[0];
            foreach (int c in candidates)
                if ((Plan(c) - middle).sqrMagnitude < (Plan(first) - middle).sqrMagnitude) first = c;

            // Farthest-point sampling: each next point is the candidate farthest from those already picked.
            var nearest = new float[candidates.Count];
            for (int k = 0; k < nearest.Length; k++) nearest[k] = float.PositiveInfinity;
            int pick = first;
            for (int n = 0; n < count; n++)
            {
                var p = Plan(pick);
                var toMiddle = middle - p;
                float yaw = toMiddle.sqrMagnitude > 1e-6f ? Mathf.Atan2(toMiddle.x, toMiddle.y) * Mathf.Rad2Deg : 0f;
                points.Add(new SpawnPoint { Position = new Vector3(p.x, surfaces[pick].Height, p.y), YawDegrees = yaw });
                int next = -1;
                float farthest = -1f;
                for (int k = 0; k < candidates.Count; k++)
                {
                    nearest[k] = Mathf.Min(nearest[k], (Plan(candidates[k]) - p).sqrMagnitude);
                    if (nearest[k] > farthest)
                    {
                        farthest = nearest[k];
                        next = candidates[k];
                    }
                }
                if (next < 0 || farthest <= 0f) break;
                pick = next;
            }
            return points;
        }
    }
}
