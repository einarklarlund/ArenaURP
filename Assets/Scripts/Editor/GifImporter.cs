using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Imports a .gif file directly as a <see cref="GifClip"/> asset: drop the gif into the Project window and
/// Unity decodes every frame into Sprites (stored as sub-assets of the gif). Then drag the resulting
/// GifClip onto a <see cref="BillboardGif"/>'s Clip field ??? one file, one drag.
///
/// The "gif" extension is passed as an OVERRIDE extension (not a normal one) because Unity's built-in
/// TextureImporter already claims .gif; overrideExts is the mechanism that lets a ScriptedImporter take it
/// over. Decoding is done by <see cref="GifDecoder"/>; no external packages are required. Tweak the import
/// settings (pixels-per-unit, filtering) in the Inspector of any selected gif and hit Apply.
/// </summary>
[ScriptedImporter(1, null, new[] { "gif" })]
public class GifImporter : ScriptedImporter
{
    [Tooltip("Sprite pixels-per-unit for every frame. Larger = smaller in world.")]
    public float pixelsPerUnit = 100f;

    [Tooltip("Point keeps pixel-art gifs crisp; Bilinear smooths photographic gifs.")]
    public FilterMode filterMode = FilterMode.Bilinear;

    [Tooltip("Frames whose delay is below this (e.g. 0) are clamped to this many seconds, matching browsers.")]
    public float minFrameDelay = 0.05f;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(ctx.assetPath); }
        catch (Exception e) { ctx.LogImportError($"Could not read gif: {e.Message}"); return; }

        GifDecoder.GifData gif;
        try { gif = GifDecoder.Decode(bytes); }
        catch (Exception e)
        {
            ctx.LogImportError($"Failed to decode gif '{Path.GetFileName(ctx.assetPath)}': {e.Message}");
            return;
        }

        var clip = ScriptableObject.CreateInstance<GifClip>();
        string baseName = Path.GetFileNameWithoutExtension(ctx.assetPath);
        clip.name = baseName;
        clip.loops = gif.Loops;

        int count = gif.Frames.Count;
        clip.frames = new Sprite[count];
        clip.frameDelays = new float[count];

        // The main object must be registered before its sub-assets.
        ctx.AddObjectToAsset("GifClip", clip);
        ctx.SetMainObject(clip);

        var rect = new Rect(0, 0, gif.Width, gif.Height);
        var pivot = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < count; i++)
        {
            GifDecoder.Frame frame = gif.Frames[i];

            var tex = new Texture2D(gif.Width, gif.Height, TextureFormat.RGBA32, false)
            {
                name = $"{baseName}_tex_{i}",
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp
            };

            // GIF rows run top-to-bottom; Unity textures run bottom-to-top. Flip vertically.
            var flipped = new Color32[frame.Pixels.Length];
            for (int y = 0; y < gif.Height; y++)
                Array.Copy(frame.Pixels, y * gif.Width, flipped, (gif.Height - 1 - y) * gif.Width, gif.Width);
            tex.SetPixels32(flipped);
            tex.Apply();

            var sprite = Sprite.Create(tex, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = $"{baseName}_{i}";

            clip.frames[i] = sprite;
            clip.frameDelays[i] = Mathf.Max(frame.Delay, minFrameDelay);

            ctx.AddObjectToAsset($"tex_{i}", tex);
            ctx.AddObjectToAsset($"sprite_{i}", sprite, tex);
        }
    }
}
