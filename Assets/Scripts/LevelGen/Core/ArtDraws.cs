namespace Arena.LevelGen
{
    /// <summary>
    /// The art's only random choices, drawn from <see cref="GenStage.Art"/> streams that no generation stage reads, so a
    /// theme never moves geometry and every peer draws the same materials.
    /// </summary>
    public static class ArtDraws
    {
        /// <summary>Which of <paramref name="count"/> building material profiles building <paramref name="buildingId"/>
        /// gets, or -1 when there are none.</summary>
        public static int ProfileIndex(ulong seed, int buildingId, int count)
        {
            if (count <= 0) return -1;
            var rng = SeedTree.Stream(seed, GenStage.Art, buildingId);
            return rng.Range(0, count);
        }
    }
}
