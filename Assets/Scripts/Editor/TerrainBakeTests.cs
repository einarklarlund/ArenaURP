using System.IO;
using Arena.LevelGen;
using Arena.LevelGen.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="TerrainBake"/> on a throwaway theme with two layer materials (ground and cliff, the rest falling back), at
/// 4 px a layer, in a temporary folder.
/// </summary>
public class TerrainBakeTests
{
    private const string Folder = "Assets/TerrainBakeTests";

    private TerrainBakeSource source;
    private Material ground, cliff;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.CreateFolder("Assets", "TerrainBakeTests");
        var grass = Image("Grass.png", new Color32(40, 160, 40, 255));
        var stone = Image("Stone.png", new Color32(120, 120, 120, 255));
        ground = LayerMaterial("Ground.mat", grass, Color.white);
        cliff = LayerMaterial("Cliff.mat", stone, new Color(1f, 0.5f, 0.5f));

        var theme = ScriptableObject.CreateInstance<LevelTheme>();
        AssetDatabase.CreateAsset(theme, $"{Folder}/Theme.asset");
        source = ScriptableObject.CreateInstance<TerrainBakeSource>();
        source.theme = theme;
        source.template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Level Gen/Level Terrain.mat");
        source.layerSize = 4;
        source.ground = ground;
        source.cliff = cliff;
        AssetDatabase.CreateAsset(source, $"{Folder}/Terrain Bake Source.asset");
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Folder);

    [Test]
    public void Bake_WritesTheThemesTerrainMaterial()
    {
        Assert.IsTrue(TerrainBake.IsStale(source), "never baked");
        TerrainBake.Bake(source);
        var terrain = source.theme.terrain;
        Assert.IsNotNull(terrain);
        Assert.AreEqual($"{Folder}/Terrain/Theme Terrain.mat", AssetDatabase.GetAssetPath(terrain));
        Assert.IsTrue(terrain.IsKeywordEnabled("_NORMALMAP"), "the normal-map variant ships with the material");
        foreach (var name in new[] { "_AlbedoArray", "_NormalArray" })
        {
            var array = terrain.GetTexture(name) as Texture2DArray;
            Assert.IsNotNull(array, name);
            Assert.AreEqual(TerrainLayers.Count, array.depth, $"{name}: a slice per layer");
            Assert.AreEqual(4, array.width, name);
        }
        Assert.AreEqual(Color.white, terrain.GetColor("_Layer0Color"), "the ground's tint");
        Assert.AreEqual(new Color(1f, 0.5f, 0.5f), terrain.GetColor("_Layer8Color"), "the cliff's tint");
        Assert.AreEqual(new Color(1f, 0.5f, 0.5f), terrain.GetColor("_Layer7Color"), "ramp sides fall back to the cliff");
        Assert.IsFalse(TerrainBake.IsStale(source));
    }

    [Test]
    public void EditingALayerMaterial_MakesTheBakeStale()
    {
        TerrainBake.Bake(source);
        cliff.SetColor("_BaseColor", Color.blue);
        EditorUtility.SetDirty(cliff);
        AssetDatabase.SaveAssetIfDirty(cliff);
        Assert.IsTrue(TerrainBake.IsStale(source));
        TerrainBake.Bake(source);
        Assert.AreEqual(Color.blue, source.theme.terrain.GetColor("_Layer8Color"));
        Assert.IsFalse(TerrainBake.IsStale(source));
    }

    private static Texture2D Image(string name, Color32 color)
    {
        string path = $"{Folder}/{name}";
        var image = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var pixels = new Color32[64];
        System.Array.Fill(pixels, color);
        image.SetPixels32(pixels);
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(image);
        AssetDatabase.ImportAsset(path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material LayerMaterial(string name, Texture2D texture, Color tint)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", tint);
        AssetDatabase.CreateAsset(material, $"{Folder}/{name}");
        return material;
    }
}
