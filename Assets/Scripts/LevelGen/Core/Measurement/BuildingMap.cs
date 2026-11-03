using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// What each building's walkable surfaces are: per floor, its largest walk region; per room, its largest region at
    /// the room's own level; per entrance, the surface just inside the door at spine height; whether those entrance
    /// surfaces are joined by two-way walking on the ground level under the footprint, over the ground floor's rooms at
    /// their own levels (the walk-through rule); per balcony, the surface just outside its door; and a reachable roof's
    /// largest region.
    /// </summary>
    public sealed class BuildingMap
    {
        /// <summary>Per building, per floor: the surfaces of the floor's largest walk region (empty if none).</summary>
        public readonly List<int>[][] FloorRegions;
        /// <summary>Per building, per room (as in <see cref="BuildingDescription.Rooms"/>): the surfaces of its largest
        /// walk region at its level (empty if none).</summary>
        public readonly List<int>[][] RoomRegions;
        /// <summary>Per building, per entrance: the surface just inside it, or -1.</summary>
        public readonly int[][] EntranceSurfaces;
        /// <summary>Per building: every entrance surface exists and they are joined on the ground level.</summary>
        public readonly bool[] WalkThrough;
        /// <summary>Per building, per balcony: the surface just outside its door, or -1.</summary>
        public readonly int[][] BalconySurfaces;
        /// <summary>Per building: the reachable roof's largest region (empty when the roof isn't meant to be reached).</summary>
        public readonly List<int>[] RoofRegions;

        public BuildingMap(LevelDescription description, SurfaceSet surfaces, MovementGraph graph, in PlayerConstants player)
        {
            int count = description.Buildings.Count;
            FloorRegions = new List<int>[count][];
            RoomRegions = new List<int>[count][];
            EntranceSurfaces = new int[count][];
            WalkThrough = new bool[count];
            BalconySurfaces = new int[count][];
            RoofRegions = new List<int>[count];
            var grid = surfaces.Grid;
            float step = player.StepHeight;

            for (int b = 0; b < count; b++)
            {
                var building = description.Buildings[b];
                var footprint = description.Polygons[building.Footprint];
                var under = new HashSet<int>();
                foreach (var c in CellsUnder(footprint, grid)) under.Add(c);

                List<Vector2> FloorFootprint(int k) =>
                    k < building.FloorFootprints.Count ? description.Polygons[building.FloorFootprints[k]] : footprint;
                List<int> RegionAt(List<Vector2> outline, float y)
                {
                    var members = new HashSet<int>();
                    foreach (int c in CellsUnder(outline, grid))
                    {
                        surfaces.ColumnRange(c % grid.CellsX, c / grid.CellsX, out int start, out int end);
                        for (int s = start; s < end; s++)
                            if (surfaces[s].HasWalkableRole && Mathf.Abs(surfaces[s].Height - y) <= 1e-3f) members.Add(s);
                    }
                    return Largest(members, graph, surfaces, step);
                }

                FloorRegions[b] = new List<int>[building.Floors];
                for (int k = 0; k < building.Floors; k++) FloorRegions[b][k] = RegionAt(FloorFootprint(k), building.FloorY(k));
                RoomRegions[b] = new List<int>[building.Rooms.Count];
                for (int r = 0; r < building.Rooms.Count; r++)
                    RoomRegions[b][r] = RegionAt(description.Polygons[building.Rooms[r].Polygon], building.Rooms[r].Height);
                RoofRegions[b] = building.RoofReachable && building.Floors > 0
                    ? RegionAt(FloorFootprint(building.Floors - 1), building.RoofY)
                    : new List<int>();

                BalconySurfaces[b] = new int[building.Balconies.Count];
                for (int i = 0; i < building.Balconies.Count; i++)
                {
                    var balcony = building.Balconies[i];
                    var outside = balcony.Door + balcony.Outward * (BuildingStage.WallThickness * 0.5f + 0.6f);
                    BalconySurfaces[b][i] = Nearest(surfaces, grid, outside, building.FloorY(balcony.Floor), player.StepHeight);
                }

                float plotHeight = description.Plots.Find(p => p.Id == building.Plot)?.BaseHeight ?? 0f;
                float spineY = building.SpineY(plotHeight);
                // The spine may climb between ground-floor rooms at their own levels.
                float above = 0f, below = 0f;
                if (building.Placement != FirstFloor.Stilts)
                    foreach (var room in building.Rooms)
                        if (room.Floor == 0)
                        {
                            above = Mathf.Max(above, room.Height - spineY);
                            below = Mathf.Max(below, spineY - room.Height);
                        }
                var entrances = EntranceSurfaces[b] = new int[building.Entrances.Count];
                var centre = Polygon.Centroid(footprint);
                for (int i = 0; i < entrances.Length; i++)
                {
                    var inside = Inside(building.Entrances[i], footprint, centre);
                    entrances[i] = Nearest(surfaces, grid, inside, spineY, player.StepHeight);
                }

                // Ground-level two-way walking within the footprint (and the door cells themselves).
                bool ok = entrances.Length > 0 && System.Array.TrueForAll(entrances, s => s >= 0);
                if (ok)
                {
                    var seen = new HashSet<int> { entrances[0] };
                    var queue = new Queue<int>(seen);
                    while (queue.Count > 0)
                    {
                        int s = queue.Dequeue();
                        foreach (int t in graph.Successors(s))
                        {
                            if (seen.Contains(t)) continue;
                            var surface = surfaces[t];
                            if (!under.Contains(surface.X + surface.Z * grid.CellsX)) continue;
                            if (surface.Height > spineY + above + player.StepHeight + 1e-3f
                                || surface.Height < spineY - below - player.StepHeight - 1e-3f) continue;
                            if (Mathf.Abs(surface.Height - surfaces[s].Height) > player.StepHeight) continue;
                            seen.Add(t);
                            queue.Enqueue(t);
                        }
                    }
                    foreach (int s in entrances) ok &= seen.Contains(s);
                }
                WalkThrough[b] = ok;
            }
        }

        /// <summary>Cells (x + z·CellsX) whose centre lies inside the polygon.</summary>
        public static IEnumerable<int> CellsUnder(List<Vector2> polygon, SpanGrid grid)
        {
            Polygon.Extent(polygon, Vector2.right, out float minX, out float maxX);
            Polygon.Extent(polygon, Vector2.up, out float minZ, out float maxZ);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX / grid.CellSize)), x1 = Mathf.Min(grid.CellsX - 1, Mathf.FloorToInt(maxX / grid.CellSize));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(minZ / grid.CellSize)), z1 = Mathf.Min(grid.CellsZ - 1, Mathf.FloorToInt(maxZ / grid.CellSize));
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                if (Polygon.Contains(polygon, grid.CellCenterX(x), grid.CellCenterZ(z))) yield return x + z * grid.CellsX;
        }

        /// <summary>A point a little inside the wall from an entrance on the footprint's edge.</summary>
        private static Vector2 Inside(Vector2 entrance, List<Vector2> footprint, Vector2 centre)
        {
            float best = float.PositiveInfinity;
            var inward = Vector2.zero;
            for (int k = 0; k < footprint.Count; k++)
            {
                var a = footprint[k];
                var dir = (footprint[(k + 1) % footprint.Count] - a).normalized;
                float off = Mathf.Abs(dir.x * (entrance - a).y - dir.y * (entrance - a).x);
                if (off >= best) continue;
                best = off;
                inward = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(inward, centre - entrance) < 0f) inward = -inward;
            }
            return entrance + inward * (BuildingStage.WallThickness * 0.5f + 0.6f);
        }

        private static int Nearest(SurfaceSet surfaces, SpanGrid grid, Vector2 p, float height, float tolerance)
        {
            int x = Mathf.FloorToInt(p.x / grid.CellSize), z = Mathf.FloorToInt(p.y / grid.CellSize);
            if (!grid.InBounds(x, z)) return -1;
            surfaces.ColumnRange(x, z, out int start, out int end);
            int best = -1;
            for (int s = start; s < end; s++)
            {
                float d = Mathf.Abs(surfaces[s].Height - height);
                if (d <= tolerance + 1e-3f && (best < 0 || d < Mathf.Abs(surfaces[best].Height - height))) best = s;
            }
            return best;
        }

        /// <summary>The largest group of <paramref name="members"/> joined by steps of at most a step height.</summary>
        private static List<int> Largest(HashSet<int> members, MovementGraph graph, SurfaceSet surfaces, float step)
        {
            var best = new List<int>();
            var seen = new HashSet<int>();
            var ordered = new List<int>(members);
            ordered.Sort();
            foreach (int seed in ordered)
            {
                if (!seen.Add(seed)) continue;
                var group = new List<int> { seed };
                var queue = new Queue<int>(group);
                while (queue.Count > 0)
                {
                    int s = queue.Dequeue();
                    foreach (int t in graph.Successors(s))
                    {
                        if (!members.Contains(t) || seen.Contains(t)) continue;
                        if (Mathf.Abs(surfaces[t].Height - surfaces[s].Height) > step) continue;
                        seen.Add(t);
                        group.Add(t);
                        queue.Enqueue(t);
                    }
                }
                if (group.Count > best.Count) best = group;
            }
            return best;
        }
    }
}
