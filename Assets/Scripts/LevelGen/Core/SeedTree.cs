namespace Arena.LevelGen
{
    /// <summary>
    /// Stable ids for the pipeline stages that own a random stream. The values are part of the
    /// generator's output: never renumber one, and bump <see cref="LevelGenerator.GeneratorVersion"/>
    /// if you must.
    /// </summary>
    public enum GenStage
    {
        Allocation = 1,
        Repair = 7,
        LevelRetry = 8,
        Partition = 11,
        PlotWeights = 12,
        Heights = 13,
        Transitions = 14,
        Buildings = 15,
        Cut = 16,
        BuildingRetry = 17,
        Terrain = 18,
        Props = 19,
        /// <summary>Visual-only draws (building material profiles). No generation stream reads it.</summary>
        Art = 20,
    }

    /// <summary>
    /// Derives independent sub-seeds with SplitMix64: <c>subSeed = hash(seed, stage, index)</c>.
    /// Each stage, plot and building draws only from its own stream, so retrying plot 3 doesn't change plot 4.
    /// Nest calls for deeper trees, e.g. <c>Derive(Derive(seed, stage, plot), retryStage, attempt)</c>.
    /// </summary>
    public static class SeedTree
    {
        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        public static ulong Derive(ulong seed, GenStage stage, int index = 0)
        {
            ulong h = SplitMix64(seed);
            h = SplitMix64(h ^ (uint)stage);
            return SplitMix64(h ^ (uint)index);
        }

        public static Pcg32 Stream(ulong seed, GenStage stage, int index = 0) =>
            new(Derive(seed, stage, index), (uint)stage);
    }
}
