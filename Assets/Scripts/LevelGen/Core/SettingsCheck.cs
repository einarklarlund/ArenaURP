using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    public enum IssueSeverity : byte
    {
        /// <summary>The generator changes the value, or a feature quietly stays off.</summary>
        Warning = 0,
        /// <summary>No level can be generated with these settings.</summary>
        Error = 1,
    }

    public struct SettingsIssue
    {
        public IssueSeverity Severity;
        public string Message;

        public override string ToString() => Message;
    }

    /// <summary>
    /// Settings problems found before generating: values sanitizing changes, and combinations no level can satisfy.
    /// The generator refuses settings with errors, and the config inspector shows every issue.
    /// </summary>
    public static class SettingsCheck
    {
        public static List<SettingsIssue> Check(in LevelGenSettings raw)
        {
            var issues = new List<SettingsIssue>();
            var s = raw.Sanitized();

            void Warn(string message) => issues.Add(new SettingsIssue { Severity = IssueSeverity.Warning, Message = message });
            void Fail(string message) => issues.Add(new SettingsIssue { Severity = IssueSeverity.Error, Message = message });
            void Changed(string name, float from, float to)
            {
                if (Mathf.Abs(from - to) > 1e-4f) Warn($"{name} {from:0.###} is out of range: using {to:0.###}.");
            }

            // Width and depth also snap to whole cells; only the clamp is worth a warning.
            Changed("Width", raw.Width, Mathf.Clamp(raw.Width, LevelGenSettings.MinSide, LevelGenSettings.MaxSide));
            Changed("Depth", raw.Depth, Mathf.Clamp(raw.Depth, LevelGenSettings.MinSide, LevelGenSettings.MaxSide));
            Changed("Plot count", raw.PlotCount, s.PlotCount);
            Changed("Cell size", raw.CellSize, s.CellSize);
            Changed("Max height", raw.MaxHeight, s.MaxHeight);
            Changed("Min plot side", raw.Plots.MinPlotSide, s.Plots.MinPlotSide);
            Changed("Min shared edge", raw.Plots.MinSharedEdge, s.Plots.MinSharedEdge);
            Changed("Height step", raw.Plots.HeightStep, s.Plots.HeightStep);
            Changed("Path stretch target", raw.PathStretch.Target, s.PathStretch.Target);
            Changed("Ramp width", raw.TransitionSizes.RampWidth, s.TransitionSizes.RampWidth);
            Changed("Ramp max slope", raw.TransitionSizes.RampMaxSlopeDegrees, s.TransitionSizes.RampMaxSlopeDegrees);
            Changed("Stair riser height", raw.TransitionSizes.StairRiserHeight, s.TransitionSizes.StairRiserHeight);
            Changed("Big floors (min)", raw.Buildings.MinFloors, s.Buildings.MinFloors);
            Changed("Big floors (max)", raw.Buildings.MaxFloors, s.Buildings.MaxFloors);
            Changed("Big floor height", raw.Buildings.BigFloorHeight, s.Buildings.BigFloorHeight);
            Changed("Small floor height", raw.Buildings.SmallFloorHeight, s.Buildings.SmallFloorHeight);
            Changed("Min room width", raw.Buildings.MinRoomWidth, s.Buildings.MinRoomWidth);
            Changed("Min room area", raw.Buildings.MinRoomArea, s.Buildings.MinRoomArea);
            Changed("Door width", raw.Buildings.DoorWidth, s.Buildings.DoorWidth);
            Changed("Door height", raw.Buildings.DoorHeight, s.Buildings.DoorHeight);
            Changed("Spine width", raw.Buildings.SpineWidth, s.Buildings.SpineWidth);
            Changed("Window height", raw.Buildings.WindowHeight, s.Buildings.WindowHeight);
            Changed("Balcony depth", raw.Buildings.BalconyDepth, s.Buildings.BalconyDepth);
            Changed("Boundary wall height", raw.Boundary.WallHeight, s.Boundary.WallHeight);
            Changed("Jump height", raw.Player.JumpHeight, s.Player.JumpHeight);
            Changed("Max safe drop", raw.Player.MaxSafeDrop, s.Player.MaxSafeDrop);
            Changed("Sinkhole depth", raw.Terrain.SinkholeDepth, s.Terrain.SinkholeDepth);

            // Plots are cells of a diagram drawn past the level's sides, and only cells wholly inside it are kept, so
            // about half the rectangle is playable. A plot at least minPlotSide wide covers at least minPlotSide².
            float side = s.Plots.MinPlotSide;
            if (2f * s.PlotCount * side * side > s.Width * s.Depth)
                Fail($"{s.PlotCount} plots at least {side:0.##} units wide don't fit in {s.Width:0.##} × {s.Depth:0.##} " +
                     "(about half of it is playable once the outline is cut): lower the plot count or min plot side, or " +
                     "make the level bigger.");

            var w = s.TransitionWeights;
            if (w.Flat + w.Ledge + w.Ramp + w.Bank <= 0f)
                Fail("Flat, ledge, ramp and bank weights are all 0: no plot can have the two-way transition every plot needs.");
            if (s.LevelElevationRange.Target > 0f && w.Ramp + w.Bank <= 0f)
                Warn("Ramp and bank weights are both 0: plots more than a jump apart in height can only be joined by repairs.");

            var t = s.Terrain;
            if (t.SinkholeWeight > 0f && t.SinkholeDepth > s.Player.JumpHeight)
                Warn($"Sinkholes {t.SinkholeDepth:0.##} deep are deeper than a jump ({s.Player.JumpHeight:0.##}): a player " +
                     "who falls in can't climb out.");

            // Every level draws a building mix: four small buildings need one small floor, the others two big floors.
            var b = s.Buildings;
            float rise = BuildingStage.GroundFloorRise;
            int bigFloorsThatFit = Mathf.FloorToInt((s.MaxHeight - rise) / b.BigFloorHeight);
            if (rise + b.SmallFloorHeight > s.MaxHeight)
                Fail($"A small building's floor height {b.SmallFloorHeight:0.##} doesn't fit under the max height " +
                     $"{s.MaxHeight:0.##}, so no building mix can be built.");
            else if (bigFloorsThatFit < BuildingStage.MinBigFloors)
                Warn($"Two big floors of {b.BigFloorHeight:0.##} don't fit under the max height {s.MaxHeight:0.##}: every level " +
                     "draws the four-small mix.");
            else if (bigFloorsThatFit < b.MaxFloors)
                Warn($"Only {bigFloorsThatFit} big floors of {b.BigFloorHeight:0.##} fit under the max height {s.MaxHeight:0.##}: " +
                     "taller buildings are cut down.");
            return issues;
        }

        public static bool HasErrors(List<SettingsIssue> issues) => issues.Exists(i => i.Severity == IssueSeverity.Error);
    }
}
