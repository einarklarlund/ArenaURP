namespace Arena.LevelGen
{
    /// <summary>FNV-1a 64 over bytes. Used for config and description hashes, which must not depend on the runtime.</summary>
    public static class StableHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Fnv1a64(byte[] bytes)
        {
            ulong h = Offset;
            unchecked
            {
                foreach (byte b in bytes)
                {
                    h ^= b;
                    h *= Prime;
                }
            }
            return h;
        }
    }
}
