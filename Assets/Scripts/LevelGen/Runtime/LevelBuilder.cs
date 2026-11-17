using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arena.LevelGen
{
    /// <summary>
    /// Turns a <see cref="LevelDescription"/> into GameObjects: one combined mesh and mesh collider per owner
    /// (each plot, transition, building, prop, the boundary and the level itself), with a submesh per material. A
    /// building keeps one collider but is drawn by a child renderer per room and one for its shell. Each face's <see cref="SurfaceTag"/> picks its material from the theme (a building's from the profile drawn for it);
    /// an empty slot, or no theme, gets a placeholder colour per tag. Server and clients build identical colliders from
    /// identical descriptions, whatever their themes.
    /// <para>
    /// Every decision but the materials, prefabs and lights' looks is made in plain C# by <see cref="BuildPlan"/>; this
    /// class only turns the plan into GameObjects.
    /// </para>
    /// <para>
    /// A prop whose variant has a prefab in <c>props</c> is that prefab, placed at its pivot, yaw and stretch, with the
    /// prefab's own colliders; its placeholder primitives (which the description keeps for the span grid and the
    /// validator) are not built. Any other prop is built as its placeholder.
    /// </para>
    /// <para>
    /// Light fixtures have no collider: a fixture's prefab is built with its colliders taken off, and a fixture without
    /// one is a glowing placeholder box (the theme's fixture material, or an emissive colour per light slot). The
    /// <c>maxRealLights</c> highest-priority lights get a real point light with no shadows; the rest only glow.
    /// </para>
    /// </summary>
    public static class LevelBuilder
    {
        private static readonly Dictionary<PrimitiveRole, Material> s_roleMaterials = new();
        private static readonly Dictionary<SurfaceTag, Material> s_tagMaterials = new();
        private static readonly Dictionary<DecalType, Material> s_decalMaterials = new();
        private static readonly Dictionary<LightSlot, Material> s_fixtureMaterials = new();

        /// <summary>Real lights without a theme, and a new theme's default.</summary>
        public const int DefaultMaxRealLights = BuildOptions.DefaultMaxRealLights;
        public const float DefaultLightIntensity = 1.5f;

        /// <param name="decals">Also run the decal pass and build its strips.</param>
        public static GameObject Build(LevelDescription description, Transform parent, string name = "Level",
            LevelTheme theme = null, PropCatalogue props = null, bool decals = true)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var terrain = TerrainMaterial.For(theme);
            var plan = Plan(description, theme, props, decals);

            foreach (var group in plan.Groups)
            {
                if (group.Prefab != null)
                {
                    Instance(group.Prefab, props.PrefabFor(group.Prefab.Kind, group.Prefab.Variant), root.transform);
                    continue;
                }
                var profile = group.Profile >= 0 ? theme.buildingProfiles[group.Profile] : null;
                BuildGroup(group, root.transform, theme, profile, terrain);
            }

            BuildFixtures(plan, description, root.transform, theme, props);
            if (plan.Decals != null) BuildDecals(plan, root.transform, theme);
            return root;
        }

        /// <summary>What <see cref="Build"/> builds, as plain data: everything but the materials, prefabs and lights' looks.</summary>
        public static BuildPlan Plan(LevelDescription description, LevelTheme theme = null, PropCatalogue props = null,
            bool decals = true) =>
            BuildPlan.Create(description, Options(theme, props, decals));

        /// <summary>The theme's and catalogue's choices that change what gets built.</summary>
        public static BuildOptions Options(LevelTheme theme, PropCatalogue props, bool decals) => new()
        {
            Blend = theme != null ? theme.TerrainBlend() : TerrainBlendSettings.Default,
            BuildDecals = decals,
            Decals = theme != null ? theme.Decals() : DecalSettings.Default,
            ProfileCount = theme != null ? theme.buildingProfiles.Count : 0,
            MaxRealLights = theme != null ? theme.maxRealLights : DefaultMaxRealLights,
            Props = props?.Art(),
        };

        /// <summary>
        /// Each light fixture (see the class summary), and a point light for each of the plan's real lights.
        /// </summary>
        private static void BuildFixtures(BuildPlan plan, LevelDescription description, Transform parent,
            LevelTheme theme, PropCatalogue catalogue)
        {
            foreach (var fixture in plan.Fixtures)
            {
                var prop = fixture.Prop;
                if (fixture.Prefab)
                {
                    var instance = Instance(prop, catalogue.PrefabFor(prop.Kind, prop.Variant), parent);
                    foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) DestroyObject(collider);
                    continue;
                }
                var material = theme != null && theme.lightFixture != null ? theme.lightFixture : FixturePlaceholder(fixture.Slot, theme);
                PlaceholderFixture(prop, fixture.Placeholder, material, parent);
            }

            float intensity = theme != null ? theme.lightIntensity : DefaultLightIntensity;
            foreach (int index in plan.RealLights)
            {
                var light = description.Lights[index];
                var go = new GameObject($"Light {light.Prop}");
                go.transform.SetParent(parent, false);
                go.transform.localPosition = light.Position;
                var point = go.AddComponent<Light>();
                point.type = LightType.Point;
                point.range = light.Range;
                point.color = theme != null ? theme.LightColour(light.Slot) : LevelTheme.DefaultLightColour(light.Slot);
                point.intensity = intensity;
                point.shadows = LightShadows.None;
            }
        }

        /// <summary>A fixture's placeholder: its box, with no collider and no shadow.</summary>
        private static void PlaceholderFixture(PropDescription prop, PlanMesh box, Material material, Transform parent)
        {
            var mesh = new Mesh { name = $"Prop {prop.Id}", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(box.Vertices);
            mesh.SetNormals(box.Normals);
            mesh.SetUVs(0, box.Uvs);
            mesh.SetTriangles(box.Triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(BuildPlan.GroupName(OwnerKind.Prop, prop.Id));
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>An emissive placeholder in a light slot's colour (the theme's, or the default).</summary>
        private static Material FixturePlaceholder(LightSlot slot, LevelTheme theme)
        {
            var colour = theme != null ? theme.LightColour(slot) : LevelTheme.DefaultLightColour(slot);
            var material = Placeholder(s_fixtureMaterials, slot, $"LevelGen light {slot}", colour);
            material.color = colour;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", colour * 2f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return material;
        }

        /// <summary>
        /// The plan's decal strips, as one "Decals" mesh with a submesh per material and no collider. Level decals take
        /// the theme's materials and building decals their profile's; a slot left empty leaves that decal out. Without
        /// a theme every decal gets a placeholder colour.
        /// </summary>
        private static GameObject BuildDecals(BuildPlan plan, Transform parent, LevelTheme theme)
        {
            var materials = new List<Material>();
            var submeshes = new List<List<int>>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int s = 0; s < plan.Decals.Count; s++)
            {
                var strip = plan.Decals[s];
                Material material;
                if (theme == null) material = DecalPlaceholder(strip.Type);
                else if (DecalTypes.IsBuilding(strip.Type))
                    material = plan.DecalProfiles[s] >= 0 ? theme.buildingProfiles[plan.DecalProfiles[s]].DecalMaterial(strip.Type) : null;
                else material = theme.DecalMaterial(strip.Type);
                if (material == null) continue;

                int index = materials.IndexOf(material);
                if (index < 0)
                {
                    index = materials.Count;
                    materials.Add(material);
                    submeshes.Add(new List<int>());
                }
                DecalMesher.Append(strip, vertices, normals, uvs, submeshes[index]);
            }
            if (materials.Count == 0) return null;

            var mesh = new Mesh { name = "Decals", hideFlags = HideFlags.DontSave };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            var go = new GameObject("Decals");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private static Material DecalPlaceholder(DecalType type) =>
            Placeholder(s_decalMaterials, type, $"LevelGen decal {type}", DecalColor(type));

        public static Color DecalColor(DecalType type) => type switch
        {
            DecalType.DoorFrame => new Color(0.95f, 0.95f, 0.95f),
            DecalType.WindowFrame => new Color(0.2f, 0.3f, 0.9f),
            DecalType.WallToCeiling => new Color(0.9f, 0.4f, 0.1f),
            DecalType.Roofline => new Color(0.9f, 0.1f, 0.1f),
            DecalType.WallCorner => new Color(0.1f, 0.8f, 0.8f),
            DecalType.FloorSkirting => new Color(0.5f, 0.2f, 0.7f),
            DecalType.GroundSkirting => new Color(0.25f, 0.2f, 0.15f),
            DecalType.TreeBase => new Color(0.35f, 0.22f, 0.1f),
            DecalType.CarSurround => new Color(0.95f, 0.95f, 0.3f),
            _ => new Color(0.8f, 0.7f, 0.3f),
        };

        private static GameObject Instance(PropDescription prop, GameObject prefab, Transform parent)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = $"Prop {prop.Id} ({prefab.name})";
            go.transform.SetLocalPositionAndRotation(prop.Position, Quaternion.Euler(0f, prop.YawDegrees, 0f));
            var scale = go.transform.localScale;
            scale.z *= prop.Stretch;
            go.transform.localScale = scale;
            return go;
        }

        /// <summary>Destroys a built level and the meshes it created (not the prefab instances' shared meshes).</summary>
        public static void Teardown(GameObject root)
        {
            if (root == null) return;
            var meshes = new HashSet<Mesh>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) meshes.Add(filter.sharedMesh);
            foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true)) meshes.Add(collider.sharedMesh);
            foreach (var mesh in meshes)
                if (mesh != null && mesh.hideFlags == HideFlags.DontSave) DestroyObject(mesh);
            DestroyObject(root);
        }

        /// <summary>
        /// A group's mesh and collider: each triangle goes to the submesh of its key's material, in order of the
        /// materials' first keys, so keys that share a material share a submesh. A building split by room
        /// (<see cref="PlanGroup.TrianglePieces"/>) keeps the collider and draws each piece with a child renderer.
        /// </summary>
        private static void BuildGroup(PlanGroup group, Transform parent, LevelTheme theme,
            BuildingMaterialProfile profile, Material terrain)
        {
            var plan = group.Mesh;
            var keyMaterials = new Material[plan.Keys.Count];
            for (int k = 0; k < plan.Keys.Count; k++)
            {
                var key = plan.Keys[k];
                keyMaterials[k] = key.Ground ? terrain : Resolve(key.Tag, key.Role, theme, profile);
            }

            var go = new GameObject(group.Name);
            go.transform.SetParent(parent, false);
            Mesh collision;
            if (group.TrianglePieces == null)
            {
                collision = Render(go, group, plan, null, -1, keyMaterials, group.Name);
            }
            else
            {
                for (int piece = 0; piece < group.PieceNames.Count; piece++)
                {
                    var child = new GameObject(group.PieceNames[piece]);
                    child.transform.SetParent(go.transform, false);
                    Render(child, group, plan, group.TrianglePieces, piece, keyMaterials, $"{group.Name} {group.PieceNames[piece]}");
                }
                collision = null;
            }

            if (group.CollisionVertices != null || collision == null)
            {
                collision = new Mesh { name = group.Name + " collision", hideFlags = HideFlags.DontSave };
                var vertices = group.CollisionVertices ?? plan.Vertices;
                if (vertices.Count > ushort.MaxValue) collision.indexFormat = IndexFormat.UInt32;
                collision.SetVertices(vertices);
                collision.SetTriangles(group.CollisionTriangles ?? plan.Triangles, 0);
                collision.RecalculateBounds();
            }
            go.AddComponent<MeshCollider>().sharedMesh = collision;
        }

        /// <summary>
        /// Gives <paramref name="go"/> a renderer for the plan's triangles in <paramref name="piece"/> (all of them when
        /// <paramref name="pieces"/> is null), with a submesh per material, and returns its mesh.
        /// </summary>
        private static Mesh Render(GameObject go, PlanGroup group, PlanMesh plan, List<int> pieces, int piece,
            Material[] keyMaterials, string name)
        {
            var materials = new List<Material>();
            var submeshes = new List<List<int>>();
            var keySubmesh = new int[plan.Keys.Count];
            for (int k = 0; k < plan.Keys.Count; k++) keySubmesh[k] = -1;
            // A piece keeps only its own triangles' vertices, in order; the whole mesh keeps every vertex.
            int[] remap = null;
            var vertices = plan.Vertices;
            var normals = plan.Normals;
            var uvs = plan.Uvs;
            List<Vector3> layerIds = plan.LayerIds;
            List<Vector4> layerWeights = plan.LayerWeights;
            if (pieces != null)
            {
                remap = new int[plan.Vertices.Count];
                for (int v = 0; v < remap.Length; v++) remap[v] = -1;
                vertices = new List<Vector3>();
                normals = new List<Vector3>();
                uvs = new List<Vector2>();
                if (plan.LayerIds != null)
                {
                    layerIds = new List<Vector3>();
                    layerWeights = new List<Vector4>();
                }
            }

            for (int t = 0; t < plan.TriangleCount; t++)
            {
                if (pieces != null && pieces[t] != piece) continue;
                int key = plan.TriangleKeys[t];
                if (keySubmesh[key] < 0)
                {
                    int index = materials.IndexOf(keyMaterials[key]);
                    if (index < 0)
                    {
                        index = materials.Count;
                        materials.Add(keyMaterials[key]);
                        submeshes.Add(new List<int>());
                    }
                    keySubmesh[key] = index;
                }
                var submesh = submeshes[keySubmesh[key]];
                for (int c = 0; c < 3; c++)
                {
                    int v = plan.Triangles[3 * t + c];
                    if (remap != null)
                    {
                        if (remap[v] < 0)
                        {
                            remap[v] = vertices.Count;
                            vertices.Add(plan.Vertices[v]);
                            normals.Add(plan.Normals[v]);
                            uvs.Add(plan.Uvs[v]);
                            if (layerIds != null)
                            {
                                layerIds.Add(plan.LayerIds[v]);
                                layerWeights.Add(plan.LayerWeights[v]);
                            }
                        }
                        v = remap[v];
                    }
                    submesh.Add(v);
                }
            }

            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (layerIds != null) SetLayers(mesh, layerIds, layerWeights);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials.ToArray();
            if (!group.CastShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return mesh;
        }

        /// <summary>
        /// The blended ground's vertex data (see the Arena/Level Terrain shader): each triangle's three layers on all
        /// three of its vertices (UV3), and each vertex's weights over them as its colour.
        /// </summary>
        private static void SetLayers(Mesh mesh, List<Vector3> layerIds, List<Vector4> layerWeights)
        {
            var weights = new List<Color>(layerWeights.Count);
            foreach (var w in layerWeights) weights.Add(w);
            mesh.SetUVs(3, layerIds);
            mesh.SetColors(weights);
        }

        /// <summary>The theme's material for a face (the building profile's for building tags), else a placeholder:
        /// a colour per tag, or per role for an untagged face.</summary>
        public static Material Resolve(SurfaceTag tag, PrimitiveRole role, LevelTheme theme, BuildingMaterialProfile profile)
        {
            if (tag == SurfaceTag.None) return Placeholder(s_roleMaterials, role, $"LevelGen {role}", RoleColor(role));
            var themed = SurfaceTags.IsBuilding(tag) ? profile?.Resolve(tag) : theme != null ? theme.Resolve(tag) : null;
            return themed != null ? themed : Placeholder(s_tagMaterials, tag, $"LevelGen {tag}", TagColor(tag));
        }

        private static Material Placeholder<T>(Dictionary<T, Material> cache, T key, string name, Color color)
        {
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            cache[key] = material;
            return material;
        }

        public static Color TagColor(SurfaceTag tag) => tag switch
        {
            SurfaceTag.Ground => new Color(0.5f, 0.6f, 0.4f),
            SurfaceTag.GroundMound => new Color(0.6f, 0.68f, 0.38f),
            SurfaceTag.GroundDip => new Color(0.42f, 0.52f, 0.42f),
            SurfaceTag.GroundSinkhole => new Color(0.45f, 0.38f, 0.3f),
            SurfaceTag.CliffFace => new Color(0.5f, 0.47f, 0.43f),
            SurfaceTag.Ramp => new Color(0.6f, 0.6f, 0.55f),
            SurfaceTag.RampSide => new Color(0.52f, 0.5f, 0.45f),
            SurfaceTag.Bank => new Color(0.55f, 0.62f, 0.42f),
            SurfaceTag.BoundaryWall => new Color(0.35f, 0.37f, 0.42f),
            SurfaceTag.PropPlaceholder => new Color(0.9f, 0.55f, 0.2f),
            SurfaceTag.BuildingFloor => new Color(0.7f, 0.62f, 0.5f),
            SurfaceTag.BuildingCeiling => new Color(0.85f, 0.85f, 0.82f),
            SurfaceTag.OuterWallOutside => new Color(0.75f, 0.72f, 0.66f),
            SurfaceTag.OuterWallInside => new Color(0.88f, 0.86f, 0.8f),
            SurfaceTag.InteriorWall => new Color(0.82f, 0.8f, 0.74f),
            SurfaceTag.SlabEdge => new Color(0.6f, 0.58f, 0.55f),
            SurfaceTag.Balcony => new Color(0.62f, 0.6f, 0.56f),
            SurfaceTag.Railing => new Color(0.3f, 0.3f, 0.32f),
            SurfaceTag.Roof => new Color(0.5f, 0.4f, 0.36f),
            SurfaceTag.Stairs => new Color(0.66f, 0.64f, 0.6f),
            SurfaceTag.Plinth => new Color(0.55f, 0.53f, 0.5f),
            SurfaceTag.Stilts => new Color(0.45f, 0.43f, 0.42f),
            _ => Color.magenta,
        };

        public static Color RoleColor(PrimitiveRole role) => role switch
        {
            PrimitiveRole.Floor => new Color(0.55f, 0.55f, 0.55f),
            PrimitiveRole.Platform => new Color(0.75f, 0.65f, 0.45f),
            PrimitiveRole.Ramp => new Color(0.45f, 0.7f, 0.45f),
            PrimitiveRole.Stairs => new Color(0.4f, 0.65f, 0.7f),
            PrimitiveRole.Wall => new Color(0.4f, 0.45f, 0.6f),
            PrimitiveRole.Cover => new Color(0.9f, 0.55f, 0.2f),
            PrimitiveRole.Pillar => new Color(0.7f, 0.3f, 0.3f),
            PrimitiveRole.Railing => new Color(0.85f, 0.85f, 0.3f),
            PrimitiveRole.Ceiling => new Color(0.7f, 0.8f, 0.9f),
            _ => Color.magenta,
        };

        private static void DestroyObject(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
