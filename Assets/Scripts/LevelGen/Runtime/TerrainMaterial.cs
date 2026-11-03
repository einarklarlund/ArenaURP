using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Arena.LevelGen
{
    /// <summary>
    /// The blended ground's material: a copy of the theme's <see cref="LevelTheme.terrain"/> with every layer's albedo
    /// and normal map in two <see cref="Texture2DArray"/>s (slice = <see cref="TerrainLayer"/>), and each layer's tiling,
    /// colour and smoothness, all read from the layer's own material. The arrays are assembled on the GPU (each texture
    /// is drawn into a slice at the largest layer's size), so the layer textures needn't share a size or format, and are
    /// kept until a layer's textures change.
    /// </summary>
    public static class TerrainMaterial
    {
        public const int MaxSize = 2048;

        private static readonly int s_albedo = Shader.PropertyToID("_AlbedoArray");
        private static readonly int s_normals = Shader.PropertyToID("_NormalArray");
        private static readonly int[] s_st = Ids("_Layer{0}_ST");
        private static readonly int[] s_color = Ids("_Layer{0}Color");
        private static readonly int[] s_smoothness = Ids("_Layer{0}Smoothness");
        private static readonly int s_baseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_bumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int s_baseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_smoothnessId = Shader.PropertyToID("_Smoothness");

        private static readonly Dictionary<LevelTheme, (int key, Material material)> s_cache = new();
        private static Texture2D s_flatNormal;

        private static int[] Ids(string format)
        {
            var ids = new int[TerrainLayers.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID(string.Format(format, i));
            return ids;
        }

        /// <summary>The theme's blended-ground material, or null when it has no <see cref="LevelTheme.terrain"/>.</summary>
        public static Material For(LevelTheme theme)
        {
            if (theme == null || theme.terrain == null) return null;
            var albedos = new Texture[TerrainLayers.Count];
            var normals = new Texture[TerrainLayers.Count];
            int key = theme.terrain.GetInstanceID();
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                var layer = theme.LayerMaterial((TerrainLayer)i);
                albedos[i] = Texture(layer, s_baseMap, s_mainTex);
                normals[i] = Texture(layer, s_bumpMap, s_bumpMap);
                key = key * 31 + (albedos[i] != null ? albedos[i].GetInstanceID() : 0);
                key = key * 31 + (normals[i] != null ? normals[i].GetInstanceID() : 0);
            }

            if (!s_cache.TryGetValue(theme, out var cached) || cached.material == null || cached.key != key)
            {
                if (cached.material != null) Release(cached.material);
                var built = new Material(theme.terrain) { name = $"{theme.terrain.name} (blended)", hideFlags = HideFlags.DontSave };
                // A dedicated server has no GPU to draw the arrays with; it never renders the material anyway.
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                {
                    built.SetTexture(s_albedo, BuildArray(albedos, Texture2D.whiteTexture, true, $"{theme.name} albedo"));
                    bool anyNormal = System.Array.Exists(normals, t => t != null);
                    if (anyNormal)
                    {
                        built.SetTexture(s_normals, BuildArray(normals, FlatNormal(), false, $"{theme.name} normals"));
                        built.EnableKeyword("_NORMALMAP");
                    }
                    else built.DisableKeyword("_NORMALMAP");
                }
                cached = (key, built);
                s_cache[theme] = cached;
            }

            // Tiling and colours are cheap: refresh them every build, so tweaking a layer's material shows at once.
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                var layer = theme.LayerMaterial((TerrainLayer)i);
                var st = new Vector4(1f, 1f, 0f, 0f);
                var color = Color.white;
                float smoothness = 0.5f;
                if (layer != null)
                {
                    int map = layer.HasProperty(s_baseMap) ? s_baseMap : s_mainTex;
                    if (layer.HasProperty(map))
                    {
                        var scale = layer.GetTextureScale(map);
                        var offset = layer.GetTextureOffset(map);
                        st = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    }
                    color = layer.HasProperty(s_baseColor) ? layer.GetColor(s_baseColor) : layer.color;
                    if (layer.HasProperty(s_smoothnessId)) smoothness = layer.GetFloat(s_smoothnessId);
                }
                cached.material.SetVector(s_st[i], st);
                cached.material.SetColor(s_color[i], color);
                cached.material.SetFloat(s_smoothness[i], smoothness);
            }
            return cached.material;
        }

        private static Texture Texture(Material material, int id, int fallback)
        {
            if (material == null) return null;
            if (material.HasProperty(id) && material.GetTexture(id) != null) return material.GetTexture(id);
            return material.HasProperty(fallback) ? material.GetTexture(fallback) : null;
        }

        /// <summary>Each texture (or <paramref name="missing"/>) drawn into one slice, with mipmaps, at the largest's size.</summary>
        private static Texture2DArray BuildArray(Texture[] textures, Texture missing, bool srgb, string name)
        {
            int size = 4;
            foreach (var t in textures)
                if (t != null) size = Mathf.Max(size, t.width, t.height);
            size = Mathf.Min(size, MaxSize);

            var format = srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            var array = new Texture2DArray(size, size, textures.Length, format, TextureCreationFlags.MipChain)
            {
                name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            var descriptor = new RenderTextureDescriptor(size, size, format, 0)
            {
                useMipMap = true, autoGenerateMips = true, mipCount = array.mipmapCount,
            };
            var target = RenderTexture.GetTemporary(descriptor);
            for (int i = 0; i < textures.Length; i++)
            {
                Graphics.Blit(textures[i] != null ? textures[i] : missing, target);
                // A GPU copy: the array's CPU side stays empty, so it must never be Apply()d.
                Graphics.CopyTexture(target, 0, array, i);
            }
            RenderTexture.ReleaseTemporary(target);
            return array;
        }

        /// <summary>Flat in both packings UnpackNormal reads (xy in RG, or in AG).</summary>
        private static Texture2D FlatNormal()
        {
            if (s_flatNormal != null) return s_flatNormal;
            s_flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "Flat normal", hideFlags = HideFlags.DontSave };
            // UnpackNormalmapRGorAG reads x from a × r and y from g: (1, ½, 1, ½) is flat either way.
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 128, 255, 128);
            s_flatNormal.SetPixels32(pixels);
            s_flatNormal.Apply();
            return s_flatNormal;
        }

        private static void Release(Material material)
        {
            foreach (var id in new[] { s_albedo, s_normals })
                if (material.HasTexture(id) && material.GetTexture(id) != null) Destroy(material.GetTexture(id));
            Destroy(material);
        }

        private static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
