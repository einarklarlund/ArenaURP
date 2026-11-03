using System;

namespace Arena.LevelGen
{
    /// <summary>
    /// What a <see cref="BuildPlan"/> reads besides the description: the art's choices that change which meshes and
    /// objects get built, taken from the theme and the prop catalogue. Materials are not here; the builder resolves
    /// them from the plan's <see cref="SurfaceKey"/>s.
    /// </summary>
    public sealed class BuildOptions
    {
        public const int DefaultMaxRealLights = 16;

        /// <summary>How the blended ground (ground tops, slopes and cliff faces, in one submesh) fades between layers.</summary>
        public TerrainBlendSettings Blend = TerrainBlendSettings.Default;
        public bool BuildDecals = true;
        public DecalSettings Decals = DecalSettings.Default;
        /// <summary>How many building material profiles there are to draw from (<see cref="ArtDraws.ProfileIndex"/>).</summary>
        public int ProfileCount;
        public int MaxRealLights = DefaultMaxRealLights;
        /// <summary>Prop variants' shapes and which have a prefab, or null for none.</summary>
        public PropArt Props;
    }

    /// <summary>The prop catalogue as a build plan sees it: each variant's shape, and whether a prefab draws it.</summary>
    public sealed class PropArt
    {
        private readonly PropShape[][] _shapes = new PropShape[PropKinds.All.Length][];
        private readonly bool[][] _prefabs = new bool[PropKinds.All.Length][];

        public void Set(PropKind kind, PropShape[] shapes, bool[] hasPrefab)
        {
            if (shapes != null && hasPrefab != null && shapes.Length != hasPrefab.Length)
                throw new ArgumentException("a shape and a prefab flag per variant");
            _shapes[(int)kind] = shapes;
            _prefabs[(int)kind] = hasPrefab;
        }

        /// <summary>The kind's variants' shapes, or null.</summary>
        public PropShape[] Shapes(PropKind kind) => _shapes[(int)kind];

        public bool HasPrefab(PropKind kind, int variant)
        {
            var prefabs = _prefabs[(int)kind];
            return prefabs != null && variant >= 0 && variant < prefabs.Length && prefabs[variant];
        }
    }
}
