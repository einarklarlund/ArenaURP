using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.LevelGen
{
    /// <summary>
    /// The blended ground's material: the theme's <see cref="LevelTheme.terrain"/>, baked in the Editor (each layer's
    /// albedo and normal map in two <see cref="Texture2DArray"/>s, slice = <see cref="TerrainLayer"/>, with its tiling,
    /// colour and smoothness). Without a theme or a bake, a placeholder draws each layer in its surface's colour.
    /// </summary>
    public static class TerrainMaterial
    {
        private static readonly int s_albedo = Shader.PropertyToID("_AlbedoArray");
        private static readonly int s_normals = Shader.PropertyToID("_NormalArray");

        private static readonly HashSet<LevelTheme> s_warned = new();
        private static Material s_placeholder;

        public static Material For(LevelTheme theme)
        {
            if (theme != null && theme.terrain != null) return theme.terrain;
            if (theme != null && s_warned.Add(theme))
                Debug.LogWarning($"Theme {theme.name} has no baked terrain material: the ground is drawn in placeholder " +
                                 "colours. Bake its Terrain Bake Source.", theme);
            return Placeholder();
        }

        /// <summary>Flat colours from <see cref="LevelBuilder.TagColor"/>, one per layer. Filled on the CPU, so it works
        /// on every platform.</summary>
        private static Material Placeholder()
        {
            if (s_placeholder != null) return s_placeholder;
            s_placeholder = new Material(Shader.Find("Arena/Level Terrain")) { name = "LevelGen Terrain", hideFlags = HideFlags.DontSave };
            // A dedicated server has no GPU to upload the arrays to; it never renders the material anyway.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return s_placeholder;

            const int size = 4;
            var albedo = new Texture2DArray(size, size, TerrainLayers.Count, TextureFormat.RGBA32, false, false)
                { name = "LevelGen Terrain albedo", hideFlags = HideFlags.DontSave };
            var normals = new Texture2DArray(size, size, TerrainLayers.Count, TextureFormat.RGBA32, false, true)
                { name = "LevelGen Terrain normals", hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            // UnpackNormalmapRGorAG reads x from a × r and y from g: (1, ½, 1, ½) is flat either way.
            var flat = new Color32[size * size];
            System.Array.Fill(flat, new Color32(255, 128, 255, 128));
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                System.Array.Fill(pixels, (Color32)LevelBuilder.TagColor(TerrainLayers.TagOf((TerrainLayer)i)));
                albedo.SetPixels32(pixels, i);
                normals.SetPixels32(flat, i);
            }
            albedo.Apply(false, true);
            normals.Apply(false, true);
            s_placeholder.SetTexture(s_albedo, albedo);
            s_placeholder.SetTexture(s_normals, normals);
            s_placeholder.EnableKeyword("_NORMALMAP");
            return s_placeholder;
        }
    }
}
