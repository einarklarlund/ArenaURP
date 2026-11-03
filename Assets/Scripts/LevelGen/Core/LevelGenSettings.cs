using System;
using System.IO;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A per-plot metric: a level-wide target, plus how the per-plot values spread around it.</summary>
    [Serializable]
    public struct MetricTarget
    {
        [Range(0f, 1f)] public float Target;
        [Range(0f, 1f)] public float Spread;
        [Tooltip("Positive gives a few high outliers, negative a few low ones.")]
        [Range(-1f, 1f)] public float Skew;
        [Tooltip("A measurement within this distance of the target counts as zero error.")]
        public float Tolerance;
        public float Weight;

        public static MetricTarget Of(float target) =>
            new() { Target = target, Spread = 0.3f, Skew = 0f, Tolerance = 0.05f, Weight = 1f };
    }

    /// <summary>A level-only metric. It has no per-plot spread.</summary>
    [Serializable]
    public struct LevelMetricTarget
    {
        [Range(0f, 1f)] public float Target;
        public float Tolerance;
        public float Weight;

        public static LevelMetricTarget Of(float target) =>
            new() { Target = target, Tolerance = 0.05f, Weight = 1f };
    }

    /// <summary>Path stretch: a level-only metric whose target is a ratio of at least 1.</summary>
    [Serializable]
    public struct StretchTarget
    {
        [Tooltip("Mean walking distance ÷ octile distance over sample pairs. 1 is open flat ground.")]
        [Min(1f)] public float Target;
        public float Tolerance;
        public float Weight;

        public static StretchTarget Of(float target) => new() { Target = target, Tolerance = 0.1f, Weight = 1f };
    }

    /// <summary>How the level is partitioned into plots.</summary>
    [Serializable]
    public struct PlotSettings
    {
        [Tooltip("How unequal plot areas are (power-diagram weights).")]
        [Range(0f, 1f)] public float SizeSpread;
        [Tooltip("Lloyd relaxation: 0..1 maps to 0..2 iterations.")]
        [Range(0f, 1f)] public float Regularity;
        [Tooltip("Smallest inscribed width of a plot, in units.")]
        public float MinPlotSide;
        [Tooltip("Shared edges shorter than this don't make plots neighbours.")]
        public float MinSharedEdge;
        [Tooltip("Plot base heights are multiples of this.")]
        public float HeightStep;
    }

    /// <summary>Relative odds of each transition type, among those that fit a segment.</summary>
    [Serializable]
    public struct TransitionWeights
    {
        public float Flat;
        public float Ledge;
        public float Cliff;
        public float Ramp;
        public float Bank;
    }

    [Serializable]
    public struct TransitionSizes
    {
        public float RampWidth;
        [Tooltip("Steepest ramp, bank or plot slope. Clamped so the rise per cell stays within StepHeight.")]
        public float RampMaxSlopeDegrees;
        [Tooltip("Clamped to at most PlayerConstants.StepHeight.")]
        public float StairRiserHeight;
        [Tooltip("Share of a ramp's or bank's run spent curving in at its foot and out at its top (half each). The " +
                 "steepest part keeps rampMaxSlope, so the run grows by 1 / (1 - this / 2).")]
        [Range(0f, 0.6f)] public float CurveFraction;
        [Tooltip("Straight pieces (bands) each curve is built from. More look smoother and cost a prism, plus up to two " +
                 "flare wedges, each.")]
        [Range(1, 8)] public int CurveSegments;
        [Tooltip("Grade of the flared sides a ramp (or a bank cut mid-edge) falls away to the ground at.")]
        public float FlareSlopeDegrees;
    }

    [Serializable]
    public struct BuildingSettings
    {
        [Tooltip("A big building's floors, at least... (cut to fit the max height; a small building has one)")]
        [Range(2, 3)] public int MinFloors;
        [Tooltip("...and at most.")]
        [Range(2, 3)] public int MaxFloors;
        [Tooltip("Slab plus clear height of a big building's floor. At least twice the player height, for stacked stair " +
                 "flights.")]
        public float BigFloorHeight;
        [Tooltip("Slab plus clear height of a small building's floor. At least twice the player height.")]
        public float SmallFloorHeight;
        [Range(0f, 1f)] public float RaisedGroundOdds;
        [Range(0f, 1f)] public float FullPlotOdds;
        [Range(0f, 1f)] public float RoofAccessOdds;
        public float DoorWidth;
        public float DoorHeight;
        public float WindowSill;
        public float WindowHeight;
        public float MinWindowWidth;
        public float MaxWindowWidth;
        public float BalconyDepth;
        public float RailingHeight;
        [Tooltip("Width of the walk-through route on the ground floor.")]
        public float SpineWidth;
        [Range(0, 3)] public int MaxInteriorWalls;
        [Tooltip("Each floor above the first differs in size from the one below by this much, at least...")]
        [Range(0f, 0.9f)] public float FloorResizeMin;
        [Tooltip("...and at most.")]
        [Range(0f, 0.9f)] public float FloorResizeMax;
        [Tooltip("Odds a floor is bigger than the one below; otherwise it is smaller.")]
        [Range(0f, 1f)] public float FloorGrowOdds;

        [Header("Rooms")]
        [Tooltip("Smallest floor area of a room (a piece of a floor between interior edges).")]
        public float MinRoomArea;
        [Tooltip("Smallest width of a room.")]
        public float MinRoomWidth;
        [Tooltip("Odds a room sits higher or lower than its floor.")]
        [Range(0f, 1f)] public float RoomOffsetOdds;
        [Tooltip("How far, in whole stair risers, at least...")]
        public float MinRoomOffset;
        [Tooltip("...and at most.")]
        public float MaxRoomOffset;
        [Tooltip("Relative odds an edge between rooms is a wall with a door...")]
        public float WallEdgeWeight;
        [Tooltip("...nothing at all...")]
        public float OpenEdgeWeight;
        [Tooltip("...or a railing with a gap in its middle.")]
        public float RailingEdgeWeight;
    }

    /// <summary>Stage 5: the uneven ground inside rooms without buildings.</summary>
    [Serializable]
    public struct TerrainSettings
    {
        [Tooltip("Floor area per sub-room: a room is split into about area ÷ this many Voronoi sub-rooms.")]
        public float SubRoomArea;
        [Tooltip("Operations per room, at least...")]
        public int MinOperations;
        [Tooltip("...and at most.")]
        public int MaxOperations;
        [Tooltip("How far a raise moves a sub-room up or down, at least...")]
        public float MinHeightChange;
        [Tooltip("...and at most.")]
        public float MaxHeightChange;
        [Tooltip("How much a tilt adds to a sub-room's slope, in degrees, at least...")]
        public float MinTiltDegrees;
        [Tooltip("...and at most.")]
        public float MaxTiltDegrees;
        [Tooltip("No terrain triangle gets steeper than this. Clamped to rampMaxSlope.")]
        public float MaxGradeDegrees;
        [Tooltip("How far a sinkhole sits below the ground round it. Deeper than the player's jump height, nothing " +
                 "climbs out: the validator allows that, and the settings check warns.")]
        public float SinkholeDepth;
        [Header("Operation odds")]
        public float RaiseWeight;
        public float TiltWeight;
        public float RaiseAndTiltWeight;
        public float SinkholeWeight;
        public float NothingWeight;
    }

    [Serializable]
    public struct PropSettings
    {
        [Tooltip("Cars and trees per 100 units of plot edge.")]
        public float Density;
        public float CarWeight;
        public float TreeWeight;

        [Header("Fences")]
        [Tooltip("Odds a room has any fences.")]
        [Range(0f, 1f)] public float FencedRoomOdds;
        [Tooltip("A fenced room lines this many of its sides, at least...")]
        public int MinFencedSides;
        [Tooltip("...and at most (capped at the room's side count).")]
        public int MaxFencedSides;
        [Tooltip("Distance from the room's edge to its fence.")]
        public float FenceMargin;
        [Tooltip("Odds a fenced room's fences are old: a few small gaps in each run.")]
        [Range(0f, 1f)] public float OldFenceOdds;
        public int MinOldGaps;
        public int MaxOldGaps;
        [Tooltip("Width of an old fence's gaps, at least... (keep it under doorWidth + 2, the narrowest gap to a neighbour)")]
        public float MinOldGapWidth;
        [Tooltip("...and at most.")]
        public float MaxOldGapWidth;
        [Tooltip("Odds a building on an inscribed footprint is ringed by a fence, with a gate out from each entrance.")]
        [Range(0f, 1f)] public float BuildingFenceOdds;
        [Tooltip("Distance from a building's porch out to its fence ring.")]
        public float BuildingFenceMargin;
        [Tooltip("Odds a room with no building fence is fenced on all its sides, open only where it leads to other rooms.")]
        [Range(0f, 1f)] public float EnclosedRoomOdds;
        [Tooltip("Odds a sinkhole is ringed by a fence.")]
        [Range(0f, 1f)] public float SinkholeFenceOdds;
        [Tooltip("Distance from a sinkhole's rim out to its fence ring.")]
        public float SinkholeFenceMargin;
        [Tooltip("Door-wide gaps in a sinkhole's ring, evenly spaced: at least...")]
        public int MinSinkholeGaps;
        [Tooltip("...and at most.")]
        public int MaxSinkholeGaps;

        [Header("Cars along the boundary walls")]
        [Tooltip("Odds a boundary wall's run gets a row of parked cars along it.")]
        [Range(0f, 1f)] public float BoundaryCarOdds;
        [Tooltip("Distance from the wall's inner face to the row.")]
        public float CarWallGap;
        [Tooltip("Space between neighbouring cars in a row, at least...")]
        public float MinCarSpacing;
        [Tooltip("...and at most.")]
        public float MaxCarSpacing;
        [Tooltip("Odds each slot in a row is left empty.")]
        [Range(0f, 1f)] public float CarSkipOdds;

        [Header("Inside")]
        [Tooltip("Tables, shelves and crates per 10 units of a room's floor area.")]
        public float InteriorPropDensity;
        public float TableWeight;
        public float ShelfWeight;
        public float CrateWeight;
        [Tooltip("Range of the wall lamps beside entrances and over porches. A room's ceiling light reaches its corners.")]
        public float LampRange;

        /// <summary>
        /// The prop catalogue's footprints, one per variant, in the config's order. The config fills these from its
        /// catalogue; sanitizing gives an empty list one default variant.
        /// </summary>
        [NonSerialized] public PropShape[] Fences;
        [NonSerialized] public PropShape[] Cars;
        [NonSerialized] public PropShape[] Trees;
        [NonSerialized] public PropShape[] Tables;
        [NonSerialized] public PropShape[] Shelves;
        [NonSerialized] public PropShape[] Crates;
        [NonSerialized] public PropShape[] CeilingLights;
        [NonSerialized] public PropShape[] WallLights;

        public PropShape[] Variants(PropKind kind) => kind switch
        {
            PropKind.Car => Cars,
            PropKind.Tree => Trees,
            PropKind.Table => Tables,
            PropKind.Shelf => Shelves,
            PropKind.Crate => Crates,
            PropKind.CeilingLight => CeilingLights,
            PropKind.WallLight => WallLights,
            _ => Fences,
        };
    }

    [Serializable]
    public struct BoundarySettings
    {
        public float WallHeight;
        public float Thickness;
    }

    [Serializable]
    public struct SearchBudget
    {
        public int ImproveIterations;
        public int RetriesPerPlot;
        public int RetriesPerLevel;
    }

    /// <summary>
    /// Movement facts that drive measurement and the no-stuck rule. Keep these in step with the pawn's
    /// CharacterController and MovementConfig.
    /// </summary>
    [Serializable]
    public struct PlayerConstants
    {
        [Tooltip("Height changes at or below this need no jump (CharacterController.stepOffset).")]
        public float StepHeight;
        [Tooltip("Height changes above StepHeight and up to this are two-way.")]
        public float JumpHeight;
        [Tooltip("The pawn camera's local y. Kept for later sightline work.")]
        public float EyeHeight;
        [Tooltip("Headroom a surface needs to count as walkable (CharacterController.height).")]
        public float PlayerHeight;
        [Tooltip("Drops higher than this are not allowed.")]
        public float MaxSafeDrop;
    }

    /// <summary>
    /// Everything the generator reads besides the seed. Plain data for the core; the
    /// <c>LevelGenConfig</c> ScriptableObject holds one of these.
    /// </summary>
    [Serializable]
    public struct LevelGenSettings
    {
        public const float MinSide = 50f;
        public const float MaxSide = 100f;
        public const int MinPlots = 4;
        public const int MaxPlots = 8;
        public const int MaxFloors = 3;

        [Header("Size")]
        [Tooltip("X extent in units.")]
        [Range(MinSide, MaxSide)] public float Width;
        [Tooltip("Z extent in units.")]
        [Range(MinSide, MaxSide)] public float Depth;
        [Tooltip("Top-level plots.")]
        [Range(MinPlots, MaxPlots)] public int PlotCount;
        [Tooltip("Vertical extent of the level. Boundary walls are exempt.")]
        public float MaxHeight;
        [Tooltip("Span grid resolution in units.")]
        public float CellSize;

        [Header("Plots")]
        public PlotSettings Plots;

        [Header("Plot metrics")]
        public MetricTarget ElevationRange;
        public MetricTarget ElevationFrequency;

        [Header("Level metrics")]
        public StretchTarget PathStretch;
        public LevelMetricTarget LevelElevationRange;

        [Header("Transitions")]
        public TransitionWeights TransitionWeights;
        public TransitionSizes TransitionSizes;

        [Header("Buildings")]
        public BuildingSettings Buildings;

        [Header("Terrain")]
        public TerrainSettings Terrain;

        [Header("Props")]
        public PropSettings Props;

        [Header("Boundary")]
        public BoundarySettings Boundary;

        [Header("Spawns")]
        [Tooltip("Spawn points to pick: twice the most players a match takes.")]
        public int SpawnPoints;

        [Header("Search")]
        public SearchBudget SearchBudget;

        [Header("Player")]
        public PlayerConstants Player;

        public static LevelGenSettings Default => new()
        {
            Width = 70f,
            Depth = 70f,
            PlotCount = 6,
            MaxHeight = 12f,
            CellSize = 0.5f,

            Plots = new PlotSettings
            {
                SizeSpread = 0.5f, Regularity = 0.5f, MinPlotSide = 8f, MinSharedEdge = 4f, HeightStep = 0.5f,
            },

            ElevationRange = MetricTarget.Of(0.2f),
            ElevationFrequency = MetricTarget.Of(0.3f),

            PathStretch = StretchTarget.Of(1.3f),
            LevelElevationRange = LevelMetricTarget.Of(0.3f),

            TransitionWeights = new TransitionWeights { Flat = 1f, Ledge = 1f, Cliff = 0.5f, Ramp = 1f, Bank = 1f },
            TransitionSizes = new TransitionSizes
            {
                RampWidth = 3f, RampMaxSlopeDegrees = 30f, StairRiserHeight = 0.25f, CurveFraction = 0.25f, CurveSegments = 4, FlareSlopeDegrees = 40f,
            },

            Buildings = new BuildingSettings
            {
                MinFloors = 2, MaxFloors = 3, BigFloorHeight = 5f, SmallFloorHeight = 4f,
                RaisedGroundOdds = 0.3f, FullPlotOdds = 0.3f, RoofAccessOdds = 0.5f,
                DoorWidth = 2f, DoorHeight = 2.5f,
                WindowSill = 0.9f, WindowHeight = 2.2f, MinWindowWidth = 1.5f, MaxWindowWidth = 2.5f,
                BalconyDepth = 2f, RailingHeight = 1.1f, SpineWidth = 3f, MaxInteriorWalls = 3,
                FloorResizeMin = 0.1f, FloorResizeMax = 0.5f, FloorGrowOdds = 0.25f,
                MinRoomArea = 12f, MinRoomWidth = 3f, RoomOffsetOdds = 0.5f, MinRoomOffset = 0.5f, MaxRoomOffset = 1.5f,
                WallEdgeWeight = 33f, OpenEdgeWeight = 33f, RailingEdgeWeight = 34f,
            },
            Terrain = new TerrainSettings
            {
                SubRoomArea = 35f, MinOperations = 2, MaxOperations = 5,
                MinHeightChange = 0.25f, MaxHeightChange = 1f, MinTiltDegrees = 3f, MaxTiltDegrees = 10f,
                MaxGradeDegrees = 20f, SinkholeDepth = 0.9f,
                RaiseWeight = 1f, TiltWeight = 1f, RaiseAndTiltWeight = 1f, SinkholeWeight = 0.5f, NothingWeight = 0.5f,
            },
            Props = new PropSettings
            {
                Density = 3f, CarWeight = 1f, TreeWeight = 1f,
                FencedRoomOdds = 0.5f, MinFencedSides = 1, MaxFencedSides = 3, FenceMargin = 1.5f,
                OldFenceOdds = 0.35f, MinOldGaps = 1, MaxOldGaps = 3, MinOldGapWidth = 0.6f, MaxOldGapWidth = 1.2f,
                BuildingFenceOdds = 0.75f, BuildingFenceMargin = 2f, EnclosedRoomOdds = 0.3f,
                SinkholeFenceOdds = 0.4f, SinkholeFenceMargin = 0.75f, MinSinkholeGaps = 2, MaxSinkholeGaps = 4,
                BoundaryCarOdds = 0.5f, CarWallGap = 0.5f, MinCarSpacing = 1f, MaxCarSpacing = 3f, CarSkipOdds = 0.25f,
                InteriorPropDensity = 1f, TableWeight = 1f, ShelfWeight = 1f, CrateWeight = 1f, LampRange = 6f,
            },
            Boundary = new BoundarySettings { WallHeight = 50f, Thickness = 1f },
            SpawnPoints = 16,
            SearchBudget = new SearchBudget { ImproveIterations = 200, RetriesPerPlot = 4, RetriesPerLevel = 4 },
            Player = new PlayerConstants
            {
                StepHeight = 0.3f, JumpHeight = 1f, EyeHeight = 1.877f, PlayerHeight = 2f, MaxSafeDrop = 4f,
            },
        };

        /// <summary>The capsule is 0.75 in radius: openings and walkways narrower than this can't be walked.</summary>
        public const float MinWalkWidth = 1.5f;

        /// <summary>Thickness of floor slabs; also what a stilted first floor needs above playerHeight.</summary>
        public const float SlabThickness = 0.5f;

        /// <summary>A shallower sinkhole is no hole at all.</summary>
        public const float MinSinkholeDepth = 0.1f;

        /// <summary>A copy with every value forced into its legal range. The generator only reads sanitized settings.</summary>
        public LevelGenSettings Sanitized()
        {
            var s = this;
            s.Width = Mathf.Clamp(s.Width, MinSide, MaxSide);
            s.Depth = Mathf.Clamp(s.Depth, MinSide, MaxSide);
            s.PlotCount = Mathf.Clamp(s.PlotCount, MinPlots, MaxPlots);
            s.CellSize = Mathf.Clamp(s.CellSize, 0.1f, 2f);
            // Whole cells, so the span grid covers the level exactly.
            s.Width = Mathf.Round(s.Width / s.CellSize) * s.CellSize;
            s.Depth = Mathf.Round(s.Depth / s.CellSize) * s.CellSize;

            s.Player.StepHeight = Mathf.Max(0.01f, s.Player.StepHeight);
            s.Player.JumpHeight = Mathf.Max(s.Player.StepHeight, s.Player.JumpHeight);
            s.Player.MaxSafeDrop = Mathf.Max(s.Player.JumpHeight, s.Player.MaxSafeDrop);
            s.MaxHeight = Mathf.Max(s.Player.PlayerHeight, s.MaxHeight);

            s.Plots.SizeSpread = Mathf.Clamp01(s.Plots.SizeSpread);
            s.Plots.Regularity = Mathf.Clamp01(s.Plots.Regularity);
            s.Plots.MinPlotSide = Mathf.Clamp(s.Plots.MinPlotSide, 4f, 20f);
            s.Plots.HeightStep = Mathf.Max(0.05f, s.Plots.HeightStep);

            s.PathStretch.Target = Mathf.Max(1f, s.PathStretch.Target);

            var b = s.Buildings;
            b.DoorWidth = Mathf.Max(MinWalkWidth, b.DoorWidth);
            b.DoorHeight = Mathf.Max(s.Player.PlayerHeight, b.DoorHeight);
            b.SpineWidth = Mathf.Max(b.DoorWidth, b.SpineWidth);
            // Stair cores stack floating flights a floor apart; each half-flight must leave a player's height under the
            // one above it.
            b.BigFloorHeight = Mathf.Max(2f * s.Player.PlayerHeight, b.BigFloorHeight);
            b.SmallFloorHeight = Mathf.Max(2f * s.Player.PlayerHeight, b.SmallFloorHeight);
            b.MinFloors = Mathf.Clamp(b.MinFloors, 2, MaxFloors);
            b.MaxFloors = Mathf.Clamp(b.MaxFloors, b.MinFloors, MaxFloors);
            b.RaisedGroundOdds = Mathf.Clamp01(b.RaisedGroundOdds);
            b.FullPlotOdds = Mathf.Clamp01(b.FullPlotOdds);
            b.RoofAccessOdds = Mathf.Clamp01(b.RoofAccessOdds);
            b.WindowHeight = Mathf.Max(s.Player.PlayerHeight, b.WindowHeight);
            b.MinWindowWidth = Mathf.Max(MinWalkWidth, b.MinWindowWidth);
            b.MaxWindowWidth = Mathf.Max(b.MinWindowWidth, b.MaxWindowWidth);
            b.BalconyDepth = Mathf.Max(MinWalkWidth, b.BalconyDepth);
            b.MaxInteriorWalls = Mathf.Clamp(b.MaxInteriorWalls, 0, 3);
            b.FloorResizeMin = Mathf.Clamp(b.FloorResizeMin, 0f, 0.9f);
            b.FloorResizeMax = Mathf.Clamp(b.FloorResizeMax, b.FloorResizeMin, 0.9f);
            b.FloorGrowOdds = Mathf.Clamp01(b.FloorGrowOdds);
            b.MinRoomWidth = Mathf.Max(MinWalkWidth, b.MinRoomWidth);
            b.MinRoomArea = Mathf.Max(b.MinRoomWidth * b.MinRoomWidth, b.MinRoomArea);
            b.RoomOffsetOdds = Mathf.Clamp01(b.RoomOffsetOdds);
            b.MinRoomOffset = Mathf.Max(0f, b.MinRoomOffset);
            b.MaxRoomOffset = Mathf.Max(b.MinRoomOffset, b.MaxRoomOffset);
            b.WallEdgeWeight = Mathf.Max(0f, b.WallEdgeWeight);
            b.OpenEdgeWeight = Mathf.Max(0f, b.OpenEdgeWeight);
            b.RailingEdgeWeight = Mathf.Max(0f, b.RailingEdgeWeight);
            if (b.WallEdgeWeight + b.OpenEdgeWeight + b.RailingEdgeWeight <= 0f) b.WallEdgeWeight = 1f;
            s.Buildings = b;

            s.Plots.MinSharedEdge = Mathf.Max(s.Buildings.DoorWidth, s.Plots.MinSharedEdge);
            s.TransitionSizes.RampWidth = Mathf.Max(MinWalkWidth, s.TransitionSizes.RampWidth);
            // A slope is walkable when its rise per cell is at most a step.
            float maxSlope = Mathf.Atan(s.Player.StepHeight / s.CellSize) * Mathf.Rad2Deg;
            s.TransitionSizes.RampMaxSlopeDegrees = Mathf.Clamp(s.TransitionSizes.RampMaxSlopeDegrees, 1f, maxSlope);
            s.TransitionSizes.StairRiserHeight = Mathf.Clamp(s.TransitionSizes.StairRiserHeight, 0.01f, s.Player.StepHeight);
            s.TransitionSizes.CurveFraction = Mathf.Clamp(s.TransitionSizes.CurveFraction, 0f, 0.6f);
            s.TransitionSizes.CurveSegments = Mathf.Clamp(s.TransitionSizes.CurveSegments, 1, 8);
            s.TransitionSizes.FlareSlopeDegrees = Mathf.Clamp(s.TransitionSizes.FlareSlopeDegrees, 15f, 75f);

            var t = s.Terrain;
            t.SubRoomArea = Mathf.Max(4f, t.SubRoomArea);
            t.MinOperations = Mathf.Max(0, t.MinOperations);
            t.MaxOperations = Mathf.Max(t.MinOperations, t.MaxOperations);
            t.MinHeightChange = Mathf.Max(0f, t.MinHeightChange);
            t.MaxHeightChange = Mathf.Max(t.MinHeightChange, t.MaxHeightChange);
            t.MinTiltDegrees = Mathf.Clamp(t.MinTiltDegrees, 0f, 45f);
            t.MaxTiltDegrees = Mathf.Clamp(t.MaxTiltDegrees, t.MinTiltDegrees, 45f);
            t.MaxGradeDegrees = Mathf.Clamp(t.MaxGradeDegrees, 0f, s.TransitionSizes.RampMaxSlopeDegrees);
            t.SinkholeDepth = Mathf.Clamp(t.SinkholeDepth, MinSinkholeDepth, s.MaxHeight);
            s.Terrain = t;
            var p = s.Props;
            p.Density = Mathf.Max(0f, p.Density);
            p.FencedRoomOdds = Mathf.Clamp01(p.FencedRoomOdds);
            p.MinFencedSides = Mathf.Max(1, p.MinFencedSides);
            p.MaxFencedSides = Mathf.Max(p.MinFencedSides, p.MaxFencedSides);
            p.FenceMargin = Mathf.Max(0.5f, p.FenceMargin);
            p.OldFenceOdds = Mathf.Clamp01(p.OldFenceOdds);
            p.MinOldGaps = Mathf.Max(0, p.MinOldGaps);
            p.MaxOldGaps = Mathf.Max(p.MinOldGaps, p.MaxOldGaps);
            p.MinOldGapWidth = Mathf.Max(0.2f, p.MinOldGapWidth);
            p.MaxOldGapWidth = Mathf.Max(p.MinOldGapWidth, p.MaxOldGapWidth);
            p.BuildingFenceOdds = Mathf.Clamp01(p.BuildingFenceOdds);
            p.BuildingFenceMargin = Mathf.Max(0.5f, p.BuildingFenceMargin);
            p.EnclosedRoomOdds = Mathf.Clamp01(p.EnclosedRoomOdds);
            p.SinkholeFenceOdds = Mathf.Clamp01(p.SinkholeFenceOdds);
            // Past half the thickest fence, so a ring stands wholly outside the rim.
            p.SinkholeFenceMargin = Mathf.Max(0.5f, p.SinkholeFenceMargin);
            p.MinSinkholeGaps = Mathf.Max(0, p.MinSinkholeGaps);
            p.MaxSinkholeGaps = Mathf.Max(p.MinSinkholeGaps, p.MaxSinkholeGaps);
            p.BoundaryCarOdds = Mathf.Clamp01(p.BoundaryCarOdds);
            p.CarWallGap = Mathf.Max(0.05f, p.CarWallGap);
            // Cars keep the prop clearance (0.5) from each other.
            p.MinCarSpacing = Mathf.Max(0.5f, p.MinCarSpacing);
            p.MaxCarSpacing = Mathf.Max(p.MinCarSpacing, p.MaxCarSpacing);
            p.CarSkipOdds = Mathf.Clamp01(p.CarSkipOdds);
            p.Fences = SanitizedVariants(p.Fences, PropKind.Fence);
            p.Cars = SanitizedVariants(p.Cars, PropKind.Car);
            p.Trees = SanitizedVariants(p.Trees, PropKind.Tree);
            p.InteriorPropDensity = Mathf.Max(0f, p.InteriorPropDensity);
            p.TableWeight = Mathf.Max(0f, p.TableWeight);
            p.ShelfWeight = Mathf.Max(0f, p.ShelfWeight);
            p.CrateWeight = Mathf.Max(0f, p.CrateWeight);
            p.LampRange = Mathf.Max(0f, p.LampRange);
            p.Tables = SanitizedVariants(p.Tables, PropKind.Table);
            p.Shelves = SanitizedVariants(p.Shelves, PropKind.Shelf);
            p.Crates = SanitizedVariants(p.Crates, PropKind.Crate);
            p.CeilingLights = SanitizedVariants(p.CeilingLights, PropKind.CeilingLight);
            p.WallLights = SanitizedVariants(p.WallLights, PropKind.WallLight);
            s.Props = p;
            s.Boundary.WallHeight = Mathf.Max(s.MaxHeight, s.Boundary.WallHeight);
            s.Boundary.Thickness = Mathf.Max(0.1f, s.Boundary.Thickness);
            s.SpawnPoints = Mathf.Clamp(s.SpawnPoints, 1, 64);
            return s;
        }

        private static PropShape[] SanitizedVariants(PropShape[] variants, PropKind kind)
        {
            if (variants == null || variants.Length == 0) return new[] { PropShape.Default(kind) };
            var result = new PropShape[variants.Length];
            for (int i = 0; i < variants.Length; i++) result[i] = variants[i].Sanitized(kind);
            return result;
        }

        /// <summary>Writes every field in a fixed order. This is the config's identity: hashes and share codes build on it.</summary>
        public void WriteTo(BinaryWriter w)
        {
            w.Write(Width); w.Write(Depth); w.Write(PlotCount); w.Write(MaxHeight); w.Write(CellSize);
            var p = Plots;
            w.Write(p.SizeSpread); w.Write(p.Regularity); w.Write(p.MinPlotSide); w.Write(p.MinSharedEdge); w.Write(p.HeightStep);
            Write(w, ElevationRange); Write(w, ElevationFrequency);
            w.Write(PathStretch.Target); w.Write(PathStretch.Tolerance); w.Write(PathStretch.Weight);
            Write(w, LevelElevationRange);
            var tw = TransitionWeights;
            w.Write(tw.Flat); w.Write(tw.Ledge); w.Write(tw.Cliff); w.Write(tw.Ramp); w.Write(tw.Bank);
            var ts = TransitionSizes;
            w.Write(ts.RampWidth); w.Write(ts.RampMaxSlopeDegrees); w.Write(ts.StairRiserHeight);
            w.Write(ts.CurveFraction); w.Write(ts.CurveSegments); w.Write(ts.FlareSlopeDegrees);
            var b = Buildings;
            w.Write(b.MinFloors); w.Write(b.MaxFloors); w.Write(b.BigFloorHeight); w.Write(b.SmallFloorHeight);
            w.Write(b.RaisedGroundOdds); w.Write(b.FullPlotOdds); w.Write(b.RoofAccessOdds);
            w.Write(b.DoorWidth); w.Write(b.DoorHeight);
            w.Write(b.WindowSill); w.Write(b.WindowHeight); w.Write(b.MinWindowWidth); w.Write(b.MaxWindowWidth);
            w.Write(b.BalconyDepth); w.Write(b.RailingHeight); w.Write(b.SpineWidth); w.Write(b.MaxInteriorWalls);
            w.Write(b.FloorResizeMin); w.Write(b.FloorResizeMax); w.Write(b.FloorGrowOdds);
            w.Write(b.MinRoomArea); w.Write(b.MinRoomWidth); w.Write(b.RoomOffsetOdds);
            w.Write(b.MinRoomOffset); w.Write(b.MaxRoomOffset);
            w.Write(b.WallEdgeWeight); w.Write(b.OpenEdgeWeight); w.Write(b.RailingEdgeWeight);
            var t = Terrain;
            w.Write(t.SubRoomArea); w.Write(t.MinOperations); w.Write(t.MaxOperations);
            w.Write(t.MinHeightChange); w.Write(t.MaxHeightChange); w.Write(t.MinTiltDegrees); w.Write(t.MaxTiltDegrees);
            w.Write(t.MaxGradeDegrees); w.Write(t.SinkholeDepth);
            w.Write(t.RaiseWeight); w.Write(t.TiltWeight); w.Write(t.RaiseAndTiltWeight); w.Write(t.SinkholeWeight); w.Write(t.NothingWeight);
            var pr = Props;
            w.Write(pr.Density); w.Write(pr.CarWeight); w.Write(pr.TreeWeight);
            w.Write(pr.FencedRoomOdds); w.Write(pr.MinFencedSides); w.Write(pr.MaxFencedSides); w.Write(pr.FenceMargin);
            w.Write(pr.OldFenceOdds); w.Write(pr.MinOldGaps); w.Write(pr.MaxOldGaps); w.Write(pr.MinOldGapWidth); w.Write(pr.MaxOldGapWidth);
            w.Write(pr.BuildingFenceOdds); w.Write(pr.BuildingFenceMargin); w.Write(pr.EnclosedRoomOdds);
            w.Write(pr.SinkholeFenceOdds); w.Write(pr.SinkholeFenceMargin); w.Write(pr.MinSinkholeGaps); w.Write(pr.MaxSinkholeGaps);
            w.Write(pr.BoundaryCarOdds); w.Write(pr.CarWallGap); w.Write(pr.MinCarSpacing); w.Write(pr.MaxCarSpacing); w.Write(pr.CarSkipOdds);
            w.Write(pr.InteriorPropDensity); w.Write(pr.TableWeight); w.Write(pr.ShelfWeight); w.Write(pr.CrateWeight);
            w.Write(pr.LampRange);
            Write(w, pr.Fences); Write(w, pr.Cars); Write(w, pr.Trees);
            Write(w, pr.Tables); Write(w, pr.Shelves); Write(w, pr.Crates); Write(w, pr.CeilingLights); Write(w, pr.WallLights);
            w.Write(Boundary.WallHeight); w.Write(Boundary.Thickness);
            w.Write(SpawnPoints);
            w.Write(SearchBudget.ImproveIterations); w.Write(SearchBudget.RetriesPerPlot); w.Write(SearchBudget.RetriesPerLevel);
            w.Write(Player.StepHeight); w.Write(Player.JumpHeight); w.Write(Player.EyeHeight);
            w.Write(Player.PlayerHeight); w.Write(Player.MaxSafeDrop);
        }

        /// <summary>Reads what <see cref="WriteTo"/> wrote, field for field: a config sent as bytes (a share code's or a
        /// level ticket's) comes back with the same hash.</summary>
        public static LevelGenSettings ReadFrom(BinaryReader r)
        {
            var s = new LevelGenSettings
            {
                Width = r.ReadSingle(), Depth = r.ReadSingle(), PlotCount = r.ReadInt32(), MaxHeight = r.ReadSingle(),
                CellSize = r.ReadSingle(),
            };
            s.Plots = new PlotSettings
            {
                SizeSpread = r.ReadSingle(), Regularity = r.ReadSingle(), MinPlotSide = r.ReadSingle(),
                MinSharedEdge = r.ReadSingle(), HeightStep = r.ReadSingle(),
            };
            s.ElevationRange = ReadMetric(r);
            s.ElevationFrequency = ReadMetric(r);
            s.PathStretch = new StretchTarget { Target = r.ReadSingle(), Tolerance = r.ReadSingle(), Weight = r.ReadSingle() };
            s.LevelElevationRange = new LevelMetricTarget { Target = r.ReadSingle(), Tolerance = r.ReadSingle(), Weight = r.ReadSingle() };
            s.TransitionWeights = new TransitionWeights
            {
                Flat = r.ReadSingle(), Ledge = r.ReadSingle(), Cliff = r.ReadSingle(), Ramp = r.ReadSingle(), Bank = r.ReadSingle(),
            };
            s.TransitionSizes = new TransitionSizes
            {
                RampWidth = r.ReadSingle(), RampMaxSlopeDegrees = r.ReadSingle(), StairRiserHeight = r.ReadSingle(),
                CurveFraction = r.ReadSingle(), CurveSegments = r.ReadInt32(), FlareSlopeDegrees = r.ReadSingle(),
            };
            s.Buildings = new BuildingSettings
            {
                MinFloors = r.ReadInt32(), MaxFloors = r.ReadInt32(), BigFloorHeight = r.ReadSingle(), SmallFloorHeight = r.ReadSingle(),
                RaisedGroundOdds = r.ReadSingle(), FullPlotOdds = r.ReadSingle(), RoofAccessOdds = r.ReadSingle(),
                DoorWidth = r.ReadSingle(), DoorHeight = r.ReadSingle(),
                WindowSill = r.ReadSingle(), WindowHeight = r.ReadSingle(), MinWindowWidth = r.ReadSingle(), MaxWindowWidth = r.ReadSingle(),
                BalconyDepth = r.ReadSingle(), RailingHeight = r.ReadSingle(), SpineWidth = r.ReadSingle(), MaxInteriorWalls = r.ReadInt32(),
                FloorResizeMin = r.ReadSingle(), FloorResizeMax = r.ReadSingle(), FloorGrowOdds = r.ReadSingle(),
                MinRoomArea = r.ReadSingle(), MinRoomWidth = r.ReadSingle(), RoomOffsetOdds = r.ReadSingle(),
                MinRoomOffset = r.ReadSingle(), MaxRoomOffset = r.ReadSingle(),
                WallEdgeWeight = r.ReadSingle(), OpenEdgeWeight = r.ReadSingle(), RailingEdgeWeight = r.ReadSingle(),
            };
            s.Terrain = new TerrainSettings
            {
                SubRoomArea = r.ReadSingle(), MinOperations = r.ReadInt32(), MaxOperations = r.ReadInt32(),
                MinHeightChange = r.ReadSingle(), MaxHeightChange = r.ReadSingle(), MinTiltDegrees = r.ReadSingle(),
                MaxTiltDegrees = r.ReadSingle(), MaxGradeDegrees = r.ReadSingle(), SinkholeDepth = r.ReadSingle(),
                RaiseWeight = r.ReadSingle(), TiltWeight = r.ReadSingle(), RaiseAndTiltWeight = r.ReadSingle(),
                SinkholeWeight = r.ReadSingle(), NothingWeight = r.ReadSingle(),
            };
            s.Props = new PropSettings
            {
                Density = r.ReadSingle(), CarWeight = r.ReadSingle(), TreeWeight = r.ReadSingle(),
                FencedRoomOdds = r.ReadSingle(), MinFencedSides = r.ReadInt32(), MaxFencedSides = r.ReadInt32(),
                FenceMargin = r.ReadSingle(), OldFenceOdds = r.ReadSingle(), MinOldGaps = r.ReadInt32(), MaxOldGaps = r.ReadInt32(),
                MinOldGapWidth = r.ReadSingle(), MaxOldGapWidth = r.ReadSingle(),
                BuildingFenceOdds = r.ReadSingle(), BuildingFenceMargin = r.ReadSingle(), EnclosedRoomOdds = r.ReadSingle(),
                SinkholeFenceOdds = r.ReadSingle(), SinkholeFenceMargin = r.ReadSingle(), MinSinkholeGaps = r.ReadInt32(),
                MaxSinkholeGaps = r.ReadInt32(),
                BoundaryCarOdds = r.ReadSingle(), CarWallGap = r.ReadSingle(), MinCarSpacing = r.ReadSingle(),
                MaxCarSpacing = r.ReadSingle(), CarSkipOdds = r.ReadSingle(),
                InteriorPropDensity = r.ReadSingle(), TableWeight = r.ReadSingle(), ShelfWeight = r.ReadSingle(),
                CrateWeight = r.ReadSingle(), LampRange = r.ReadSingle(),
                Fences = ReadVariants(r), Cars = ReadVariants(r), Trees = ReadVariants(r),
                Tables = ReadVariants(r), Shelves = ReadVariants(r), Crates = ReadVariants(r),
                CeilingLights = ReadVariants(r), WallLights = ReadVariants(r),
            };
            s.Boundary = new BoundarySettings { WallHeight = r.ReadSingle(), Thickness = r.ReadSingle() };
            s.SpawnPoints = r.ReadInt32();
            s.SearchBudget = new SearchBudget
            {
                ImproveIterations = r.ReadInt32(), RetriesPerPlot = r.ReadInt32(), RetriesPerLevel = r.ReadInt32(),
            };
            s.Player = new PlayerConstants
            {
                StepHeight = r.ReadSingle(), JumpHeight = r.ReadSingle(), EyeHeight = r.ReadSingle(),
                PlayerHeight = r.ReadSingle(), MaxSafeDrop = r.ReadSingle(),
            };
            return s;
        }

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) WriteTo(w);
            return stream.ToArray();
        }

        public static LevelGenSettings FromBytes(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            return ReadFrom(r);
        }

        private static MetricTarget ReadMetric(BinaryReader r) => new()
        {
            Target = r.ReadSingle(), Spread = r.ReadSingle(), Skew = r.ReadSingle(), Tolerance = r.ReadSingle(), Weight = r.ReadSingle(),
        };

        private static PropShape[] ReadVariants(BinaryReader r)
        {
            var variants = new PropShape[r.ReadInt32()];
            for (int i = 0; i < variants.Length; i++)
                variants[i] = new PropShape
                {
                    Weight = r.ReadSingle(), Length = r.ReadSingle(), Width = r.ReadSingle(), Height = r.ReadSingle(),
                    CanopyLength = r.ReadSingle(), CanopyWidth = r.ReadSingle(), CanopyBottom = r.ReadSingle(),
                    CanopyHeight = r.ReadSingle(),
                };
            return variants;
        }

        public ulong ComputeHash()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) WriteTo(w);
            return StableHash.Fnv1a64(stream.ToArray());
        }

        private static void Write(BinaryWriter w, in MetricTarget m)
        {
            w.Write(m.Target); w.Write(m.Spread); w.Write(m.Skew); w.Write(m.Tolerance); w.Write(m.Weight);
        }

        private static void Write(BinaryWriter w, PropShape[] variants)
        {
            w.Write(variants?.Length ?? 0);
            if (variants == null) return;
            foreach (var v in variants)
            {
                w.Write(v.Weight); w.Write(v.Length); w.Write(v.Width); w.Write(v.Height);
                w.Write(v.CanopyLength); w.Write(v.CanopyWidth); w.Write(v.CanopyBottom); w.Write(v.CanopyHeight);
            }
        }

        private static void Write(BinaryWriter w, in LevelMetricTarget m)
        {
            w.Write(m.Target); w.Write(m.Tolerance); w.Write(m.Weight);
        }
    }
}
