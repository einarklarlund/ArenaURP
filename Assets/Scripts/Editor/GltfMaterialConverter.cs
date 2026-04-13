using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ArenaURP.Editor
{
    public static class GltfMaterialConverter
    {
        const string SimpleLitShaderName = "Universal Render Pipeline/Simple Lit";

        [MenuItem("Assets/Convert glTF Materials to Simple Lit")]
        static void ConvertSelectedToSimpleLit()
        {
            var selected = Selection.activeObject;
            if (selected == null)
            {
                Debug.LogWarning("No asset selected.");
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("Could not determine asset path.");
                return;
            }

            var simpleLit = Shader.Find(SimpleLitShaderName);
            if (simpleLit == null)
            {
                Debug.LogError($"Shader '{SimpleLitShaderName}' not found.");
                return;
            }

            var dir = Path.GetDirectoryName(path);
            var assetName = Path.GetFileNameWithoutExtension(path);
            var materialsDir = Path.Combine(dir, "Materials");
            if (!AssetDatabase.IsValidFolder(materialsDir))
                AssetDatabase.CreateFolder(dir, "Materials");

            // Extract materials from the glTF sub-assets
            var materialMap = new Dictionary<string, Material>();
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);

            foreach (var asset in allAssets)
            {
                if (asset is not Material mat)
                    continue;

                var matPath = Path.Combine(materialsDir, $"{mat.name}.mat");
                var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (existing != null)
                {
                    materialMap[mat.name] = existing;
                    continue;
                }

                var baseColorTex = mat.GetTexture("baseColorTexture");
                var baseColor = mat.HasColor("baseColorFactor")
                    ? mat.GetColor("baseColorFactor")
                    : Color.white;

                var extracted = new Material(simpleLit);
                extracted.name = mat.name;
                extracted.SetTexture("_BaseMap", baseColorTex);
                extracted.SetTexture("_MainTex", baseColorTex);
                extracted.SetColor("_BaseColor", baseColor);
                extracted.SetColor("_Color", baseColor);
                extracted.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

                AssetDatabase.CreateAsset(extracted, matPath);
                materialMap[mat.name] = extracted;
            }

            AssetDatabase.SaveAssets();

            // Instantiate the glTF, swap materials, save as prefab
            var sourceGO = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceGO);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && materialMap.TryGetValue(mats[i].name, out var replacement))
                        mats[i] = replacement;
                }
                renderer.sharedMaterials = mats;
            }

            var prefabPath = Path.Combine(dir, $"{assetName}.prefab");
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            Debug.Log($"Extracted {materialMap.Count} materials, created prefab at '{prefabPath}'.");
        }

        [MenuItem("Assets/Convert glTF Materials to Simple Lit", true)]
        static bool ValidateConvertSelected()
        {
            var selected = Selection.activeObject;
            if (selected == null) return false;
            var path = AssetDatabase.GetAssetPath(selected);
            return path.EndsWith(".gltf") || path.EndsWith(".glb");
        }
    }
}
