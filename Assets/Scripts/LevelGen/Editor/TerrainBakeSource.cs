using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Where a theme's blended ground comes from: a material per <see cref="TerrainLayer"/>, whose textures, tiling,
    /// colour and smoothness <see cref="TerrainBake"/> bakes into the theme's <see cref="LevelTheme.terrain"/>. Editor
    /// only: nothing in a build references these materials, so their textures stay out of it unless something else
    /// uses them.
    /// </summary>
    [CreateAssetMenu(menuName = "Arena/Terrain Bake Source", fileName = "Terrain Bake Source")]
    public class TerrainBakeSource : ScriptableObject
    {
        [Tooltip("The theme whose terrain material the bake writes.")] public LevelTheme theme;
        [Tooltip("A material with the Arena/Level Terrain shader. The baked material starts as a copy of it.")]
        public Material template;
        [Min(4), Tooltip("Each layer's size in the baked arrays, in pixels. Layers are resized to it.")]
        public int layerSize = 512;

        [Header("Layers")]
        [Tooltip("Ground near its room's original plane.")] public Material ground;
        [Tooltip("Ground raised above its room's original plane. Falls back to the ground.")] public Material mound;
        [Tooltip("Ground lowered below its room's original plane. Falls back to the ground.")] public Material dip;
        [Tooltip("Falls back to the ground.")] public Material sinkhole;
        [Tooltip("The higher ground round a sinkhole. Falls back to the ground.")] public Material rim;
        [Tooltip("Ramp tops. Falls back to the ground.")] public Material ramp;
        [Tooltip("Bank tops. Falls back to the ground.")] public Material bank;
        [Tooltip("Ramp and bank sides and flares. Falls back to the cliff.")] public Material rampSide;
        [Tooltip("Cliff and ledge faces, and the lip of ground along their tops. Falls back to the ground.")]
        public Material cliff;

        [HideInInspector] public string bakedHash;

        /// <summary>The material a layer takes its textures and settings from, or null.</summary>
        public Material LayerMaterial(TerrainLayer layer) => layer switch
        {
            TerrainLayer.Ground => ground,
            TerrainLayer.Mound => mound != null ? mound : ground,
            TerrainLayer.Dip => dip != null ? dip : ground,
            TerrainLayer.Sinkhole => sinkhole != null ? sinkhole : ground,
            TerrainLayer.Rim => rim != null ? rim : ground,
            TerrainLayer.Ramp => ramp != null ? ramp : ground,
            TerrainLayer.Bank => bank != null ? bank : ground,
            TerrainLayer.RampSide => rampSide != null ? rampSide : cliff != null ? cliff : ground,
            TerrainLayer.Cliff => cliff != null ? cliff : ground,
            _ => null,
        };
    }
}
