namespace Arena.LevelGen
{
    /// <summary>
    /// PCG32 (XSH-RR) random stream. The generator draws from these only, never from
    /// <c>UnityEngine.Random</c> or <c>System.Random</c>, so a seed reproduces on any machine.
    /// Get one from <see cref="SeedTree.Stream"/> rather than seeding it by hand.
    /// </summary>
    public struct Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private readonly ulong _increment;

        public Pcg32(ulong seed, ulong streamId)
        {
            _state = 0;
            _increment = (streamId << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Uniform in [0, 1), with 24 bits of precision so every value is an exact float.</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform in [min, maxExclusive), without modulo bias.</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            uint span = (uint)(maxExclusive - min);
            uint threshold = unchecked(0u - span) % span;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return min + (int)(r % span);
            }
        }

        public bool Chance(float probability) => NextFloat() < probability;
    }
}
