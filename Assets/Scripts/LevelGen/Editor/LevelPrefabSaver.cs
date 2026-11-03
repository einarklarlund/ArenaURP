using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Saves a built level as a self-contained prefab. The builder makes its meshes, materials and terrain arrays in
    /// memory as DontSave, so each one is stored as a sub-asset of the prefab: the level's own meshes are moved in,
    /// and the builder's cached materials and textures (shared with later builds) are copied in.
    /// </summary>
    public static class LevelPrefabSaver
    {
        /// <summary>Saves <paramref name="root"/>, a fresh build that nothing else uses, to <paramref name="path"/>.
        /// Its meshes become the prefab's, so tear it down afterwards as usual.</summary>
        public static GameObject Save(GameObject root, string path)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;

            var subAssets = new List<Object>();
            var copies = new Dictionary<Object, Object>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                filter.sharedMesh = Persist(filter.sharedMesh, subAssets);
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
                collider.sharedMesh = Persist(collider.sharedMesh, subAssets);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = CopyMaterial(materials[i], copies, subAssets);
                renderer.sharedMaterials = materials;
            }

            // A re-save would otherwise keep the last save's sub-assets alongside the new ones.
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                foreach (var old in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    if (old is Mesh || old is Material || old is Texture) Object.DestroyImmediate(old, true);

            // The prefab file must exist before sub-assets can join it; the second save writes the references to them.
            PrefabUtility.SaveAsPrefabAsset(root, path);
            foreach (var asset in subAssets) AssetDatabase.AddObjectToAsset(asset, path);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) return null;
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static bool InMemory(Object o) => o != null && !EditorUtility.IsPersistent(o);

        /// <summary>The level's own mesh, made saveable; a prefab prop's mesh or a project asset is left alone.</summary>
        private static Mesh Persist(Mesh mesh, List<Object> subAssets)
        {
            if (!InMemory(mesh)) return mesh;
            if (mesh.hideFlags != HideFlags.None)
            {
                mesh.hideFlags = HideFlags.None;
                subAssets.Add(mesh);
            }
            return mesh;
        }

        private static Material CopyMaterial(Material material, Dictionary<Object, Object> copies, List<Object> subAssets)
        {
            if (!InMemory(material)) return material;
            if (copies.TryGetValue(material, out var done)) return (Material)done;
            var copy = new Material(material) { name = material.name, hideFlags = HideFlags.None };
            foreach (int id in copy.GetTexturePropertyNameIDs())
            {
                var texture = copy.GetTexture(id);
                if (InMemory(texture)) copy.SetTexture(id, CopyTexture(texture, copies, subAssets));
            }
            copies[material] = copy;
            subAssets.Add(copy);
            return copy;
        }

        private static Texture CopyTexture(Texture texture, Dictionary<Object, Object> copies, List<Object> subAssets)
        {
            if (copies.TryGetValue(texture, out var done)) return (Texture)done;
            Texture copy = texture switch
            {
                Texture2DArray array => ReadBack(array),
                Texture2D t when t.isReadable => Object.Instantiate(t),
                _ => null,
            };
            if (copy == null)
            {
                Debug.LogWarning($"Level prefab: can't save texture '{texture.name}' ({texture.GetType().Name}); left empty.");
                return null;
            }
            copy.name = texture.name;
            copy.hideFlags = HideFlags.None;
            copies[texture] = copy;
            subAssets.Add(copy);
            return copy;
        }

        /// <summary>A CPU copy of a GPU-only array (the terrain arrays are filled with CopyTexture and never Apply()d),
        /// read back slice by slice and mip by mip.</summary>
        private static Texture2DArray ReadBack(Texture2DArray source)
        {
            var copy = new Texture2DArray(source.width, source.height, source.depth, source.graphicsFormat,
                UnityEngine.Experimental.Rendering.TextureCreationFlags.MipChain, source.mipmapCount)
            {
                wrapMode = source.wrapMode, filterMode = source.filterMode, anisoLevel = source.anisoLevel,
            };
            for (int slice = 0; slice < source.depth; slice++)
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    var request = AsyncGPUReadback.Request(source, mip, 0, Mathf.Max(1, source.width >> mip), 0,
                        Mathf.Max(1, source.height >> mip), slice, 1);
                    request.WaitForCompletion();
                    if (request.hasError)
                    {
                        Object.DestroyImmediate(copy);
                        return null;
                    }
                    copy.SetPixelData(request.GetData<byte>(), mip, slice);
                }
            copy.Apply(false, false);
            return copy;
        }
    }
}
