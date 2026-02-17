using UnityEngine;
using UnityEditor;
using System.IO;

public class PaletteLUTGenerator : EditorWindow
{
    public enum ComparisonMode { CIELAB, sRGB, Length, Luminance, HSV }

    Texture2D paletteTexture;
    int lutSize = 16;
    ComparisonMode selectedMode = ComparisonMode.CIELAB;
    string folderPath = "Assets/LUTs";
    string fileName = "PaletteLUT";

    [MenuItem("Tools/Create Palette LUT")]
    public static void ShowWindow() => GetWindow<PaletteLUTGenerator>("Palette LUT Creator");

    void OnGUI()
    {
        GUILayout.Label("Source Settings", EditorStyles.boldLabel);
        paletteTexture = (Texture2D)EditorGUILayout.ObjectField("Source Palette", paletteTexture, typeof(Texture2D), false);
        lutSize = EditorGUILayout.IntSlider("LUT Resolution", lutSize, 8, 256);
        selectedMode = (ComparisonMode)EditorGUILayout.EnumPopup("Comparison Function", selectedMode);

        EditorGUILayout.Space();
        GUILayout.Label("Output Settings", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.TextField("Save Path", folderPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60)))
        {
            string absolutePath = EditorUtility.OpenFolderPanel("Select Save Location", folderPath, "");
            if (!string.IsNullOrEmpty(absolutePath) && absolutePath.StartsWith(Application.dataPath))
                folderPath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
        }
        EditorGUILayout.EndHorizontal();

        fileName = EditorGUILayout.TextField("File Name", fileName);

        if (GUILayout.Button("Generate 3D LUT") && paletteTexture != null)
            GenerateLUT();
    }

    void GenerateLUT()
    {
        Color[] palette = paletteTexture.GetPixels();
        Texture3D lut = new Texture3D(lutSize, lutSize, lutSize, TextureFormat.RGBA32, false);
        lut.filterMode = FilterMode.Point;
        lut.wrapMode = TextureWrapMode.Clamp;

        Color[] lutColors = new Color[lutSize * lutSize * lutSize];

        for (int z = 0; z < lutSize; z++)
        {
            float b = (float)z / (lutSize - 1);
            for (int y = 0; y < lutSize; y++)
            {
                float g = (float)y / (lutSize - 1);
                for (int x = 0; x < lutSize; x++)
                {
                    float r = (float)x / (lutSize - 1);
                    Color baseColor = new Color(r, g, b);
                    lutColors[x + (y * lutSize) + (z * lutSize * lutSize)] = GetClosestColor(baseColor, palette);
                }
            }
            EditorUtility.DisplayProgressBar("Generating LUT", $"Mode: {selectedMode}", (float)z / lutSize);
        }

        lut.SetPixels(lutColors);
        lut.Apply();

        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        AssetDatabase.CreateAsset(lut, Path.Combine(folderPath, fileName + ".asset"));
        AssetDatabase.SaveAssets();
        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
    }

    Color GetClosestColor(Color target, Color[] palette)
    {
        Color bestColor = palette[0];
        float minDiff = float.MaxValue;

        foreach (Color p in palette)
        {
            float diff = 0;
            switch (selectedMode)
            {
                case ComparisonMode.CIELAB:
                    diff = Vector3.Distance(RGBToLAB(target), RGBToLAB(p));
                    break;
                case ComparisonMode.sRGB:
                    diff = Vector3.Distance(LinearToSRGB(target), LinearToSRGB(p));
                    break;
                case ComparisonMode.Length:
                    diff = Vector3.Distance(new Vector3(target.r, target.g, target.b), new Vector3(p.r, p.g, p.b));
                    break;
                case ComparisonMode.Luminance:
                    float targetLum = target.r * 0.2126f + target.g * 0.7152f + target.b * 0.0722f;
                    float pLum = p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f;
                    diff = Mathf.Abs(targetLum - pLum);
                    break;
                case ComparisonMode.HSV:
                    Color.RGBToHSV(target, out float th, out float ts, out float tv);
                    Color.RGBToHSV(p, out float ph, out float ps, out float pv);
                    diff = Vector3.Distance(new Vector3(th, ts, tv), new Vector3(ph, ps, pv));
                    break;
            }

            if (diff < minDiff) { minDiff = diff; bestColor = p; }
        }
        return bestColor;
    }

    #region Color Math
    Vector3 RGBToLAB(Color c)
    {
        // Convert to Linear XYZ
        float r = c.r, g = c.g, b = c.b;
        float x = r * 0.4124f + g * 0.3576f + b * 0.1805f;
        float y = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        float z = r * 0.0193f + g * 0.1192f + b * 0.9505f;

        float _f(float t) => (t > 0.008856f) ? Mathf.Pow(t, 1f/3f) : (7.787f * t) + (16f/116f);
        
        float fx = _f(x / 0.95047f);
        float fy = _f(y / 1.00000f);
        float fz = _f(z / 1.08883f);

        return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }

    Vector3 LinearToSRGB(Color c)
    {
        float _s(float v) => (v <= 0.0031308f) ? v * 12.92f : 1.055f * Mathf.Pow(v, 1f/2.4f) - 0.055f;
        return new Vector3(_s(c.r), _s(c.g), _s(c.b));
    }
    #endregion
}