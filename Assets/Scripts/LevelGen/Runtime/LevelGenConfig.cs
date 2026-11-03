using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A level generator preset. The core never sees this asset, only the settings it converts to.</summary>
    [CreateAssetMenu(menuName = "Arena/Level Gen Config", fileName = "LevelGenConfig")]
    public class LevelGenConfig : ScriptableObject
    {
        [Tooltip("The seed this preset generates with when no other seed is given.")]
        public ulong seed;

        public LevelGenSettings settings = LevelGenSettings.Default;

        [Tooltip("Prefab variants per prop kind. A kind with none uses one placeholder of the default size.")]
        public PropCatalogue props = new();

        /// <summary>The settings with the catalogue's footprints, sanitized: what the generator reads.</summary>
        public LevelGenSettings ToSettings()
        {
            var s = settings;
            s.Props.Fences = props?.Shapes(PropKind.Fence);
            s.Props.Cars = props?.Shapes(PropKind.Car);
            s.Props.Trees = props?.Shapes(PropKind.Tree);
            s.Props.Tables = props?.Shapes(PropKind.Table);
            s.Props.Shelves = props?.Shapes(PropKind.Shelf);
            s.Props.Crates = props?.Shapes(PropKind.Crate);
            s.Props.CeilingLights = props?.Shapes(PropKind.CeilingLight);
            s.Props.WallLights = props?.Shapes(PropKind.WallLight);
            return s.Sanitized();
        }

        private void Reset() => settings = LevelGenSettings.Default;
    }
}
