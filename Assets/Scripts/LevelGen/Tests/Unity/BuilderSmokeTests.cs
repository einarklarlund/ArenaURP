using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    /// <summary>
    /// That <see cref="LevelBuilder"/> turns its <see cref="BuildPlan"/> into the right GameObjects, materials, shader data
    /// and colliders. Everything the plan decides is tested on the plan itself, outside Unity; these need the engine.
    /// </summary>
    public class BuilderSmokeTests
    {
        private static LevelDescription Generate(LevelGenSettings settings, ulong seed)
        {
            var result = LevelGenerator.Generate(settings, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        private static LevelTheme MakeTheme(Color color, int profiles)
        {
            var theme = ScriptableObject.CreateInstance<LevelTheme>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = color };
            for (int i = 0; i < profiles; i++)
                theme.buildingProfiles.Add(new BuildingMaterialProfile { name = $"P{i}", floor = material, outerWallOutside = material });
            return theme;
        }

        [Test]
        public void Build_RealisesItsPlan([Values(false, true)] bool themed)
        {
            var level = Generate(LevelGenSettings.Default, 3);
            var theme = themed ? MakeTheme(Color.red, 2) : null;
            var plan = LevelBuilder.Plan(level, theme);
            var root = LevelBuilder.Build(level, null, "BuilderSmokeTests", theme);
            try
            {
                var names = new List<string>();
                foreach (var g in plan.Groups) names.Add(g.Name);
                foreach (var f in plan.Fixtures) names.Add(BuildPlan.GroupName(OwnerKind.Prop, f.Prop.Id));
                foreach (int light in plan.RealLights) names.Add($"Light {level.Lights[light].Prop}");
                // Placeholder decals without a theme; this theme has no decal materials, so none.
                if (!themed && plan.Decals.Count > 0) names.Add("Decals");
                var children = new List<string>();
                foreach (Transform child in root.transform) children.Add(child.name);
                CollectionAssert.AreEqual(names, children);

                for (int i = 0; i < plan.Groups.Count; i++)
                {
                    var group = plan.Groups[i];
                    var go = root.transform.GetChild(i);
                    var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                    CollectionAssert.AreEqual(group.Mesh.Vertices, mesh.vertices, group.Name);
                    int indices = 0;
                    for (int s = 0; s < mesh.subMeshCount; s++) indices += mesh.GetTriangles(s).Length;
                    Assert.AreEqual(group.Mesh.Triangles.Count, indices, $"{group.Name}: every triangle in a submesh");
                    Assert.AreEqual(mesh.subMeshCount, go.GetComponent<MeshRenderer>().sharedMaterials.Length);
                    var collider = go.GetComponent<MeshCollider>();
                    CollectionAssert.AreEqual(group.CollisionVertices ?? group.Mesh.Vertices, collider.sharedMesh.vertices, group.Name);
                }
                Assert.AreEqual(plan.RealLights.Count, root.GetComponentsInChildren<Light>().Length);
            }
            finally
            {
                LevelBuilder.Teardown(root);
                if (theme != null) Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void Theme_ProfileFor_IsTheDrawnProfile()
        {
            var theme = MakeTheme(Color.green, 3);
            try
            {
                for (int building = 0; building < 10; building++)
                    Assert.AreSame(theme.buildingProfiles[ArtDraws.ProfileIndex(7, building, 3)], theme.ProfileFor(7, building));
            }
            finally
            {
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void Builder_LeavesOutDecalsWhoseSlotIsEmpty()
        {
            var level = Generate(LevelGenSettings.Default, 1);
            var theme = ScriptableObject.CreateInstance<LevelTheme>();
            var root = LevelBuilder.Build(level, null, "DecalTests", theme);
            var placeholders = LevelBuilder.Build(level, null, "DecalTests placeholders");
            try
            {
                Assert.IsNull(root.transform.Find("Decals"), "a theme with no decal materials builds no decals");
                var decals = placeholders.transform.Find("Decals");
                Assert.IsNotNull(decals, "without a theme, decals get placeholder colours");
                Assert.IsNull(decals.GetComponent<Collider>(), "decals have no collider");
            }
            finally
            {
                LevelBuilder.Teardown(root);
                LevelBuilder.Teardown(placeholders);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void Catalogue_IsInTheConfigHash_PrefabsAreNot()
        {
            var config = ScriptableObject.CreateInstance<LevelGenConfig>();
            var prefab = new GameObject("Prop prefab");
            var van = new PropShape { Weight = 1f, Length = 5f, Width = 2f, Height = 2.3f };
            try
            {
                ulong empty = config.ToSettings().ComputeHash();
                Assert.AreEqual(LevelGenSettings.Default.Sanitized().ComputeHash(), empty, "no catalogue is the default variants");

                config.props.cars.Add(new PropVariant { shape = van });
                ulong withVan = config.ToSettings().ComputeHash();
                Assert.AreNotEqual(empty, withVan, "a footprint is geometry");

                config.props.cars[0].prefab = prefab;
                Assert.AreEqual(withVan, config.ToSettings().ComputeHash(), "a prefab is not");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Builder_InstancesPrefabs_InPlaceOfPlaceholders()
        {
            var s = LevelGenSettings.Default;
            s.Props.Cars = new[] { PropShape.DefaultCar, new PropShape { Weight = 1f, Length = 5f, Width = 2f, Height = 2.3f } };
            var level = Generate(s, 2);
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.name = "Test car";
            var sharedMesh = car.GetComponent<MeshFilter>().sharedMesh;
            var catalogue = new PropCatalogue();
            // Only the first car variant has a prefab; everything else stays a placeholder.
            catalogue.cars.Add(new PropVariant { prefab = car, shape = PropShape.DefaultCar });
            var root = LevelBuilder.Build(level, null, "BuilderSmokeTests", props: catalogue);
            try
            {
                int instanced = 0;
                foreach (var prop in level.Props)
                {
                    if (prop.Kind != PropKind.Car || prop.Variant != 0) continue;
                    var instance = root.transform.Find($"Prop {prop.Id} (Test car)");
                    Assert.IsNotNull(instance, $"prop {prop.Id} is its prefab");
                    Assert.IsNull(root.transform.Find($"Prop {prop.Id}"), "and no placeholder");
                    Assert.AreEqual(prop.Position, instance.localPosition);
                    Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, prop.YawDegrees, 0f), instance.localRotation), 1e-2f);
                    instanced++;
                }
                Assert.Greater(instanced, 0, "seed 2 has a first-variant car");
            }
            finally
            {
                LevelBuilder.Teardown(root);
                Assert.IsTrue(sharedMesh != null, "teardown leaves the prefab's own mesh alone");
                Object.DestroyImmediate(car);
            }
        }

        [Test]
        public void Builder_DrawsGroundWithLayers_AndReleasesCollisionMeshes()
        {
            var level = Generate(LevelGenSettings.Default, 1);
            var theme = ScriptableObject.CreateInstance<LevelTheme>();
            theme.terrain = new Material(Shader.Find("Arena/Level Terrain"));
            var root = LevelBuilder.Build(level, null, "BuilderSmokeTests", theme, decals: false);
            var collisionMeshes = new List<Mesh>();
            try
            {
                int ground = 0;
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    var collider = renderer.GetComponent<MeshCollider>();
                    if (collider != null && collider.sharedMesh != mesh) collisionMeshes.Add(collider.sharedMesh);
                    int submesh = System.Array.FindIndex(renderer.sharedMaterials, m => m != null && m.shader == theme.terrain.shader);
                    if (submesh < 0) continue;
                    var ids = new List<Vector3>();
                    mesh.GetUVs(3, ids);
                    Assert.AreEqual(mesh.vertexCount, ids.Count, "layer ids on UV3");
                    Assert.AreEqual(mesh.vertexCount, mesh.colors.Length, "weights as colours");
                    var triangles = mesh.GetTriangles(submesh);
                    for (int t = 0; t < triangles.Length; t++) Assert.AreEqual(1f, mesh.colors[triangles[t]].a, "ground vertices are marked");
                    ground += triangles.Length / 3;
                }
                Assert.Greater(ground, 0);
                Assert.Greater(collisionMeshes.Count, 0, "split tops get collision meshes of their own");
            }
            finally
            {
                LevelBuilder.Teardown(root);
                Object.DestroyImmediate(theme.terrain);
                Object.DestroyImmediate(theme);
            }
            foreach (var mesh in collisionMeshes) Assert.IsTrue(mesh == null, "separate collision meshes are released during teardown");
        }

        [Test]
        public void Builder_DrawsGroundBlended_WithoutABakedTerrain([Values(false, true)] bool themed)
        {
            var level = new LevelDescription();
            int polygon = level.AddPolygon(new[] { new Vector2(10, 20), new Vector2(14, 20), new Vector2(14, 24), new Vector2(10, 24) });
            level.Plots.Add(new PlotDescription { Id = 0, Polygon = polygon, BaseHeight = 3f });
            level.AddPrism(PrimitiveRole.Floor, polygon, -10f, 3f, Vector2.right, OwnerKind.Plot, 0,
                Surfaces.Of(SurfaceTag.Ground, SurfaceTag.CliffFace, SurfaceTag.CliffFace));
            var theme = themed ? ScriptableObject.CreateInstance<LevelTheme>() : null;
            var root = LevelBuilder.Build(level, null, "Placeholder terrain", theme, decals: false);
            try
            {
                var materials = root.GetComponentInChildren<MeshRenderer>().sharedMaterials;
                var terrain = System.Array.Find(materials, m => m != null && m.shader.name == "Arena/Level Terrain");
                Assert.IsNotNull(terrain, "ground and cliffs are always blended");
                Assert.AreEqual(1, materials.Length, "nothing else on a plain plot");
                Assert.AreEqual(TerrainLayers.Count, ((Texture2DArray)terrain.GetTexture("_AlbedoArray")).depth,
                    "a placeholder colour per layer");
            }
            finally
            {
                LevelBuilder.Teardown(root);
                if (theme != null) Object.DestroyImmediate(theme);
            }
        }
    }
}
