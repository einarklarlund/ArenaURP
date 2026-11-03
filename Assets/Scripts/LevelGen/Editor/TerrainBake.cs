using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Bakes a <see cref="TerrainBakeSource"/> into its theme's terrain material: every layer's albedo and normal map
    /// side by side in two 3×3 flipbook PNGs, which import as <see cref="Texture2DArray"/>s (slice = layer) compressed
    /// for the build target, and each layer's tiling, colour and smoothness copied onto the material. Textures are read
    /// from their source files, so their import settings don't affect the bake. Builds read only the baked material,
    /// so WebGL (which can't copy textures on the GPU) gets the same arrays as the Editor.
    /// </summary>
    public static class TerrainBake
    {
        private const int Columns = 3;
        // Bump to rebake everything when the bake's output changes.
        private const int Version = 1;

        private static readonly int s_albedo = Shader.PropertyToID("_AlbedoArray");
        private static readonly int s_normals = Shader.PropertyToID("_NormalArray");
        private static readonly int s_baseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        private static readonly int s_bumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int s_baseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int s_smoothness = Shader.PropertyToID("_Smoothness");

        [MenuItem("Arena/Level Gen/Bake Terrain")]
        public static void BakeAll()
        {
            foreach (var source in Sources()) Bake(source);
        }

        public static TerrainBakeSource[] Sources()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(TerrainBakeSource)}");
            var sources = new TerrainBakeSource[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                sources[i] = AssetDatabase.LoadAssetAtPath<TerrainBakeSource>(AssetDatabase.GUIDToAssetPath(guids[i]));
            return sources;
        }

        /// <summary>Whether the theme's terrain material is missing or was baked from different inputs.</summary>
        public static bool IsStale(TerrainBakeSource source) =>
            source.theme != null && (source.theme.terrain == null || source.bakedHash != Hash(source));

        public static void Bake(TerrainBakeSource source)
        {
            if (source.theme == null || source.template == null)
            {
                Debug.LogError($"{source.name}: a terrain bake needs a theme and a template material.", source);
                return;
            }

            string folder = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)), "Terrain");
            Directory.CreateDirectory(folder);
            string name = source.theme.name;
            int size = source.layerSize;

            var albedo = WriteArray(source, Path.Combine(folder, $"{name} Albedo.png"), size, false);
            var normals = WriteArray(source, Path.Combine(folder, $"{name} Normals.png"), size, true);

            string materialPath = Path.Combine(folder, $"{name} Terrain.mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(source.template);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                // Not CopyPropertiesFromMaterial: properties the template never saved would then be lost on save.
                EditorUtility.CopySerialized(source.template, material);
                material.name = Path.GetFileNameWithoutExtension(materialPath);
            }
            material.SetTexture(s_albedo, albedo);
            material.SetTexture(s_normals, normals);
            material.EnableKeyword("_NORMALMAP");
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                var layer = source.LayerMaterial((TerrainLayer)i);
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
                    if (layer.HasProperty(s_smoothness)) smoothness = layer.GetFloat(s_smoothness);
                }
                material.SetVector($"_Layer{i}_ST", st);
                material.SetColor($"_Layer{i}Color", color);
                material.SetFloat($"_Layer{i}Smoothness", smoothness);
            }
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            source.theme.terrain = material;
            EditorUtility.SetDirty(source.theme);
            AssetDatabase.SaveAssetIfDirty(source.theme);
            source.bakedHash = Hash(source);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssetIfDirty(source);
            Debug.Log($"Baked {name}'s terrain ({size}² layers).", material);
        }

        /// <summary>Each layer's texture resized into one cell of a 3×3 sheet, slice 0 top left, imported as an array.</summary>
        private static Texture2DArray WriteArray(TerrainBakeSource source, string path, int size, bool normal)
        {
            var sheet = new Texture2D(Columns * size, Columns * size, TextureFormat.RGB24, false, true);
            var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                var layer = source.LayerMaterial((TerrainLayer)i);
                var texture = normal ? Texture(layer, s_bumpMap, s_bumpMap) : Texture(layer, s_baseMap, s_mainTex);
                int column = i % Columns, row = i / Columns;
                // Texture rows count up from the bottom; the importer reads cells from the top.
                int x = column * size, y = (Columns - 1 - row) * size;
                var image = Load(texture);
                if (image == null)
                {
                    var fill = normal ? new Color32(128, 128, 255, 255) : new Color32(255, 255, 255, 255);
                    var pixels = new Color32[size * size];
                    System.Array.Fill(pixels, fill);
                    sheet.SetPixels32(x, y, size, size, pixels);
                    continue;
                }
                Graphics.Blit(image, target);
                Object.DestroyImmediate(image);
                RenderTexture.active = target;
                sheet.ReadPixels(new Rect(0, 0, size, size), x, y);
            }
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            settings.textureShape = TextureImporterShape.Texture2DArray;
            settings.flipbookRows = Columns;
            settings.flipbookColumns = Columns;
            settings.sRGBTexture = !normal;
            settings.mipmapEnabled = true;
            settings.npotScale = TextureImporterNPOTScale.None;
            settings.wrapMode = TextureWrapMode.Repeat;
            settings.filterMode = FilterMode.Trilinear;
            settings.aniso = 4;
            importer.SetTextureSettings(settings);
            importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Columns * size));
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        }

        private static Texture Texture(Material material, int id, int fallback)
        {
            if (material == null) return null;
            if (material.HasProperty(id) && material.GetTexture(id) != null) return material.GetTexture(id);
            return material.HasProperty(fallback) ? material.GetTexture(fallback) : null;
        }

        /// <summary>The texture's source file as it is on disk (with mips, so resizing filters it), or null.</summary>
        private static Texture2D Load(Texture texture)
        {
            if (texture == null) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, true, true) { filterMode = FilterMode.Trilinear };
            if (File.Exists(path) && image.LoadImage(File.ReadAllBytes(path))) return image;
            Object.DestroyImmediate(image);
            Debug.LogError($"Terrain bake can't read {path}: only PNG and JPG sources are supported.", texture);
            return null;
        }

        private static string Hash(TerrainBakeSource source)
        {
            var hash = new Hash128();
            hash.Append(Version);
            hash.Append(source.layerSize);
            hash.Append(source.theme != null ? source.theme.name : "");
            Append(ref hash, source.template);
            for (int i = 0; i < TerrainLayers.Count; i++)
            {
                var layer = source.LayerMaterial((TerrainLayer)i);
                Append(ref hash, layer);
                Append(ref hash, Texture(layer, s_baseMap, s_mainTex));
                Append(ref hash, Texture(layer, s_bumpMap, s_bumpMap));
            }
            return hash.ToString();
        }

        private static void Append(ref Hash128 hash, Object asset)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : "";
            hash.Append(path);
            if (path.Length > 0) hash.Append(AssetDatabase.GetAssetDependencyHash(path).ToString());
        }

        /// <summary>Rebakes a source once anything it bakes from has changed, so a layer edit shows without a Bake.</summary>
        private class Watcher : AssetPostprocessor
        {
            private static bool s_queued;

            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (s_queued || imported.Length == 0 || Application.isBatchMode) return;
                s_queued = true;
                // Not from inside the import: baking imports assets of its own.
                EditorApplication.delayCall += () =>
                {
                    s_queued = false;
                    foreach (var source in Sources())
                        if (source != null && source.theme != null && source.template != null && source.bakedHash != Hash(source))
                            Bake(source);
                };
            }
        }

        /// <summary>A build refuses stale bakes and themes without terrain: their ground would be drawn wrong.</summary>
        private class BuildCheck : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                foreach (var source in Sources())
                    if (IsStale(source))
                        throw new BuildFailedException($"{AssetDatabase.GetAssetPath(source)} is out of date: run " +
                                                       "Arena/Level Gen/Bake Terrain in the Editor and commit the result.");
                foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(LevelArtRegistry)}"))
                {
                    var registry = AssetDatabase.LoadAssetAtPath<LevelArtRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                    foreach (var theme in registry.themes)
                        if (theme != null && theme.terrain == null)
                            throw new BuildFailedException($"Theme {theme.name} has no terrain material: give it a " +
                                                           "Terrain Bake Source and bake it.");
                }
            }
        }
    }

    [CustomEditor(typeof(TerrainBakeSource))]
    public class TerrainBakeSourceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var source = (TerrainBakeSource)target;
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (source.theme != null && TerrainBake.IsStale(source))
                EditorGUILayout.HelpBox("The theme's terrain is out of date.", MessageType.Warning);
            if (GUILayout.Button("Bake")) TerrainBake.Bake(source);
        }
    }
}
