using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>The design's hard constraints, numbered as in the doc.</summary>
    public enum Constraint
    {
        NoOneGetsStuck = 1,
        EveryPlotHasTwoWayTransition = 2,
        EveryBuildingIsWalkThrough = 3,
        InsideBounds = 4,
        HeadroomAndSizes = 5,
        PlotCount = 6,
        NoOverlap = 7,
        BuildingMix = 8,
    }

    public sealed class ConstraintResult
    {
        public Constraint Constraint;
        public bool Passed;
        public string Detail;
        /// <summary>Offending surfaces, for overlays.</summary>
        public List<int> Surfaces = new();
        /// <summary>Offending plots or primitives (by list position), depending on the constraint.</summary>
        public List<int> Items = new();
        /// <summary>Buildings (by list position) that cause the failure, so the generator can drop them.</summary>
        public List<int> Buildings = new();
        /// <summary>Where on the XZ plane it fails, for a constraint that names no surface (the cells short of
        /// headroom), so the generator can find the props there.</summary>
        public List<Vector2> Where = new();
    }

    /// <summary>
    /// The no-stuck reachability result, kept for overlays. R is everything reachable from the reference
    /// surface; a sink is a surface in R from which the reference can't be reached again.
    /// </summary>
    public sealed class Reachability
    {
        public int Reference = -1;
        public bool[] InR;
        public bool[] Sink;
    }

    /// <summary>Checks the hard constraints on a measured level. Balconies and reachable roofs (part of 1) come with
    /// milestone 5; repair comes with the improve stage.</summary>
    public static class Validator
    {
        public static List<ConstraintResult> Validate(LevelDescription description, in LevelGenSettings settings,
            SurfaceSet surfaces, PlotMap plots, TransitionMap transitions, BuildingMap buildings, WalkRegions regions,
            MovementGraph graph, out Reachability reachability, bool overlaps = true)
        {
            var noOneGetsStuck = NoOneGetsStuck(description, surfaces, plots, transitions, buildings, regions, graph,
                out reachability);
            var twoWay = EveryPlotHasTwoWayTransition(description);
            var walkThrough = EveryBuildingIsWalkThrough(description, buildings, reachability);
            var insideBounds = InsideBounds(description, settings);
            var headroom = Headroom(surfaces, settings.Player);
            var plotCount = PlotCount(description, settings);
            StageProfiler.Begin("Overlap check");
            var noOverlap = overlaps
                ? OverlapCheck.Check(description)
                : new ConstraintResult { Constraint = Constraint.NoOverlap, Passed = true, Detail = "Not checked." };
            StageProfiler.End();
            return new List<ConstraintResult>
            {
                noOneGetsStuck, twoWay, walkThrough, insideBounds, headroom, plotCount, noOverlap,
                BuildingMixMatches(description),
            };
        }

        /// <summary>
        /// R, the set reachable from the largest walk region of the first plot, must be strongly connected and
        /// must contain every plot's (and sub-plot's) largest walk region, every transition's surfaces, and every
        /// building floor's largest walk region. A sinkhole's floor may be a sink: sinkholes are allowed to be deeper
        /// than a jump (the settings check warns).
        /// </summary>
        public static ConstraintResult NoOneGetsStuck(LevelDescription description, SurfaceSet surfaces, PlotMap plots,
            TransitionMap transitions, BuildingMap buildings, WalkRegions regions, MovementGraph graph,
            out Reachability reachability)
        {
            var result = new ConstraintResult { Constraint = Constraint.NoOneGetsStuck };
            reachability = new Reachability { InR = new bool[surfaces.Count], Sink = new bool[surfaces.Count] };

            // The reference is the largest region of the first plot with ground of its own.
            int referenceRegion = -1;
            for (int r = 0; r < plots.PlotCount && referenceRegion < 0; r++)
                if (plots.IsLeaf[r]) referenceRegion = regions.LargestRegionOf(r);
            if (referenceRegion < 0)
            {
                result.Detail = "The first plot has no walkable surface to start from.";
                return result;
            }

            int reference = regions.Regions[referenceRegion].Surfaces[0];
            reachability.Reference = reference;
            var inR = reachability.InR = graph.Reach(reference);
            var canReturn = graph.Reach(reference, reverse: true, within: inR);

            var sinkholes = new List<List<Vector2>>();
            foreach (var subRoom in description.SubRooms)
                if (subRoom.Surface == SurfaceTag.GroundSinkhole) sinkholes.Add(description.Polygons[subRoom.Polygon]);

            int sinks = 0;
            for (int s = 0; s < surfaces.Count; s++)
            {
                if (!inR[s] || canReturn[s] || InAny(sinkholes, surfaces, s)) continue;
                reachability.Sink[s] = true;
                result.Surfaces.Add(s);
                sinks++;
            }

            var problems = new List<string>();
            if (sinks > 0) problems.Add($"{sinks} reachable surfaces have no way back");

            var unreached = new List<int>();
            for (int r = 0; r < plots.PlotCount; r++)
            {
                if (!plots.IsLeaf[r]) continue;
                int region = regions.LargestRegionOf(r);
                if (region >= 0 && inR[regions.Regions[region].Surfaces[0]]) continue;
                unreached.Add(description.Plots[r].Id);
                result.Items.Add(r);
                if (region >= 0) result.Surfaces.AddRange(regions.Regions[region].Surfaces);
            }
            if (unreached.Count > 0) problems.Add($"plots not reachable: {string.Join(", ", unreached)}");

            var unreachedTransitions = new List<int>();
            for (int t = 0; t < transitions.FloorSurfaces.Length; t++)
            {
                bool ok = true;
                foreach (int s in transitions.FloorSurfaces[t])
                {
                    if (inR[s]) continue;
                    ok = false;
                    result.Surfaces.Add(s);
                }
                if (!ok) unreachedTransitions.Add(description.Transitions[t].Id);
            }
            if (unreachedTransitions.Count > 0)
                problems.Add($"transition surfaces not reachable: {string.Join(", ", unreachedTransitions)}");

            var unreachedFloors = new List<string>();
            for (int b = 0; b < buildings.FloorRegions.Length; b++)
                for (int k = 0; k < buildings.FloorRegions[b].Length; k++)
                {
                    var region = buildings.FloorRegions[b][k];
                    if (region.Count > 0 && inR[region[0]]) continue;
                    unreachedFloors.Add($"building {description.Buildings[b].Id} floor {k}");
                    if (!result.Buildings.Contains(b)) result.Buildings.Add(b);
                    result.Surfaces.AddRange(region);
                }
            for (int b = 0; b < buildings.RoomRegions.Length; b++)
                for (int r = 0; r < buildings.RoomRegions[b].Length; r++)
                {
                    var region = buildings.RoomRegions[b][r];
                    if (region.Count > 0 && inR[region[0]]) continue;
                    unreachedFloors.Add($"building {description.Buildings[b].Id} room {r} (floor {description.Buildings[b].Rooms[r].Floor})");
                    if (!result.Buildings.Contains(b)) result.Buildings.Add(b);
                    result.Surfaces.AddRange(region);
                }
            for (int b = 0; b < buildings.BalconySurfaces.Length; b++)
            {
                for (int i = 0; i < buildings.BalconySurfaces[b].Length; i++)
                {
                    int surface = buildings.BalconySurfaces[b][i];
                    if (surface >= 0 && inR[surface]) continue;
                    unreachedFloors.Add($"building {description.Buildings[b].Id} balcony {i}");
                    if (!result.Buildings.Contains(b)) result.Buildings.Add(b);
                }
                var roof = buildings.RoofRegions[b];
                if (description.Buildings[b].RoofReachable && (roof.Count == 0 || !inR[roof[0]]))
                {
                    unreachedFloors.Add($"building {description.Buildings[b].Id} roof");
                    if (!result.Buildings.Contains(b)) result.Buildings.Add(b);
                }
            }
            if (unreachedFloors.Count > 0) problems.Add($"floors not reachable: {string.Join(", ", unreachedFloors)}");

            // A sink inside a building's floors or balconies is the building's doing.
            var grid = surfaces.Grid;
            for (int b = 0; b < description.Buildings.Count; b++)
            {
                if (result.Buildings.Contains(b)) continue;
                var building = description.Buildings[b];
                var outlines = new List<List<Vector2>> { description.Polygons[building.Footprint] };
                foreach (int f in building.FloorFootprints) outlines.Add(description.Polygons[f]);
                foreach (var balcony in building.Balconies) outlines.Add(description.Polygons[balcony.Polygon]);
                for (int s = 0; s < surfaces.Count && !result.Buildings.Contains(b); s++)
                {
                    if (!reachability.Sink[s]) continue;
                    float x = grid.CellCenterX(surfaces[s].X), z = grid.CellCenterZ(surfaces[s].Z);
                    foreach (var outline in outlines)
                        if (Polygon.Contains(outline, x, z))
                        {
                            result.Buildings.Add(b);
                            break;
                        }
                }
            }

            result.Passed = problems.Count == 0;
            result.Detail = result.Passed ? "R is strongly connected and reaches every plot, transition, building floor, room, balcony and reachable roof."
                : string.Join("; ", problems);
            return result;
        }

        private static bool InAny(List<List<Vector2>> outlines, SurfaceSet surfaces, int s)
        {
            if (outlines.Count == 0) return false;
            var grid = surfaces.Grid;
            float x = grid.CellCenterX(surfaces[s].X), z = grid.CellCenterZ(surfaces[s].Z);
            foreach (var outline in outlines)
                if (Polygon.Contains(outline, x, z)) return true;
            return false;
        }

        /// <summary>Every building's entrances are joined by two-way walking at spine height and lie in R.</summary>
        public static ConstraintResult EveryBuildingIsWalkThrough(LevelDescription description, BuildingMap buildings,
            Reachability reachability)
        {
            var result = new ConstraintResult { Constraint = Constraint.EveryBuildingIsWalkThrough };
            var failing = new List<int>();
            for (int b = 0; b < description.Buildings.Count; b++)
            {
                bool ok = buildings.WalkThrough[b];
                foreach (int s in buildings.EntranceSurfaces[b]) ok &= s >= 0 && reachability.InR[s];
                if (ok) continue;
                failing.Add(description.Buildings[b].Id);
                result.Buildings.Add(b);
                foreach (int s in buildings.EntranceSurfaces[b])
                    if (s >= 0) result.Surfaces.Add(s);
            }
            result.Passed = failing.Count == 0;
            result.Detail = result.Passed ? "Every building's entrances are joined through it on the ground."
                : $"buildings not walk-through: {string.Join(", ", failing)}";
            return result;
        }

        /// <summary>Every plot with ground of its own (a sub-plot, or a plot that wasn't carved) has a two-way
        /// transition.</summary>
        public static ConstraintResult EveryPlotHasTwoWayTransition(LevelDescription description)
        {
            var result = new ConstraintResult { Constraint = Constraint.EveryPlotHasTwoWayTransition };
            var failing = new List<int>();
            var parents = new HashSet<int>();
            foreach (var plot in description.Plots)
                if (plot.ParentId >= 0) parents.Add(plot.ParentId);
            for (int r = 0; r < description.Plots.Count; r++)
            {
                int id = description.Plots[r].Id;
                if (parents.Contains(id)) continue;
                bool ok = false;
                foreach (var t in description.Transitions)
                    if (TransitionTypes.IsTwoWay(t.Type) && (t.PlotA == id || t.PlotB == id)) ok = true;
                if (ok) continue;
                failing.Add(id);
                result.Items.Add(r);
            }
            result.Passed = failing.Count == 0;
            result.Detail = result.Passed ? "Every plot has a two-way transition."
                : $"plots with no two-way transition: {string.Join(", ", failing)}";
            return result;
        }

        /// <summary>Every primitive's footprint lies in width × depth and its top at or below maxHeight. The
        /// ground may extend below y = 0, and boundary walls are exempt.</summary>
        public static ConstraintResult InsideBounds(LevelDescription description, in LevelGenSettings settings)
        {
            const float epsilon = 1e-3f;
            var result = new ConstraintResult { Constraint = Constraint.InsideBounds };
            for (int i = 0; i < description.Primitives.Count; i++)
            {
                var p = description.Primitives[i];
                if (p.OwnerKind == OwnerKind.Boundary) continue;
                if (p.Shape == PrimitiveShape.Prism)
                {
                    if (!PrismInside(p, description.Polygons[p.Polygon], settings, epsilon)) result.Items.Add(i);
                    continue;
                }
                float yaw = p.YawDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Abs(Mathf.Cos(yaw)), sin = Mathf.Abs(Mathf.Sin(yaw));
                float extentX = p.Size.x * 0.5f * cos + p.Size.z * 0.5f * sin;
                float extentZ = p.Size.x * 0.5f * sin + p.Size.z * 0.5f * cos;
                bool inside = p.Position.x - extentX >= -epsilon && p.Position.x + extentX <= settings.Width + epsilon
                    && p.Position.z - extentZ >= -epsilon && p.Position.z + extentZ <= settings.Depth + epsilon
                    && p.Position.y + p.Size.y <= settings.MaxHeight + epsilon;
                if (!inside) result.Items.Add(i);
            }
            result.Passed = result.Items.Count == 0;
            result.Detail = result.Passed ? "Everything is inside the level volume."
                : $"{result.Items.Count} primitives stick out of the level volume";
            return result;
        }

        private static bool PrismInside(in Primitive p, List<Vector2> polygon, in LevelGenSettings settings, float epsilon)
        {
            foreach (var v in polygon)
            {
                bool inside = v.x >= -epsilon && v.x <= settings.Width + epsilon && v.y >= -epsilon
                    && v.y <= settings.Depth + epsilon && p.PrismTop(v.x, v.y) <= settings.MaxHeight + epsilon;
                if (!inside) return false;
            }
            return true;
        }

        /// <summary>
        /// Walkable-role tops need playerHeight of headroom. A floor or platform covered too low isn't a surface,
        /// so it's found from the spans. Door, window, stair and walkway sizes are checked once buildings exist.
        /// </summary>
        public static ConstraintResult Headroom(SurfaceSet surfaces, in PlayerConstants player)
        {
            var result = new ConstraintResult { Constraint = Constraint.HeadroomAndSizes };
            var grid = surfaces.Grid;
            int low = 0;
            for (int z = 0; z < grid.CellsZ; z++)
            for (int x = 0; x < grid.CellsX; x++)
            {
                var column = grid.GetColumn(x, z);
                for (int i = 0; i + 1 < column.Count; i++)
                    if (PrimitiveRoles.IsWalkable(column[i].Role) && column[i + 1].Bottom - column[i].Top < player.PlayerHeight)
                    {
                        low++;
                        result.Where.Add(new Vector2(grid.CellCenterX(x), grid.CellCenterZ(z)));
                    }
            }
            result.Passed = low == 0;
            result.Detail = result.Passed ? "Every floor has headroom." : $"{low} floor cells have less than {player.PlayerHeight} headroom";
            return result;
        }

        /// <summary>Hard constraint 8: the built buildings match the drawn mix, big and small. A level with no mix drawn
        /// (a hand-built one) passes.</summary>
        public static ConstraintResult BuildingMixMatches(LevelDescription description)
        {
            var result = new ConstraintResult { Constraint = Constraint.BuildingMix };
            if (description.Mix == BuildingMix.None)
            {
                result.Passed = true;
                result.Detail = "No building mix was drawn.";
                return result;
            }
            BuildingStage.MixCounts(description.Mix, out int big, out int small);
            int builtBig = description.Buildings.FindAll(b => b.Class == BuildingClass.Big).Count;
            int builtSmall = description.Buildings.Count - builtBig;
            result.Passed = builtBig == big && builtSmall == small;
            result.Detail = result.Passed ? $"The buildings match the {description.Mix} mix."
                : $"{builtBig} big and {builtSmall} small buildings, the {description.Mix} mix wants {big} and {small}";
            return result;
        }

        /// <summary>The top-level plot count matches the settings. Sub-plots don't count.</summary>
        public static ConstraintResult PlotCount(LevelDescription description, in LevelGenSettings settings)
        {
            int count = description.Plots.FindAll(p => p.ParentId < 0).Count;
            bool ok = count == settings.PlotCount;
            return new ConstraintResult
            {
                Constraint = Constraint.PlotCount,
                Passed = ok,
                Detail = ok ? $"{settings.PlotCount} plots." : $"{count} plots, expected {settings.PlotCount}",
            };
        }
    }
}
