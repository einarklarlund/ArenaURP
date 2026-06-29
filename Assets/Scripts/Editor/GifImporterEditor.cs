using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

/// <summary>
/// Inspector for an imported .gif: keeps the normal import settings (pixels-per-unit, filtering, ???) and the
/// Apply/Revert bar from the base <see cref="ScriptedImporterEditor"/>, and adds an animated preview in the
/// bottom preview pane so you can watch the gif play without entering play mode. Also renders the first
/// frame as the asset's Project-window thumbnail.
///
/// The preview pulls frames from the imported <see cref="GifClip"/> and advances them on editor time,
/// honoring each frame's delay. Play/pause and a speed multiplier live in the preview header.
/// </summary>
[CustomEditor(typeof(GifImporter))]
public class GifImporterEditor : ScriptedImporterEditor
{
    bool _play = true;
    float _speed = 1f;
    int _frame;
    float _timer;
    double _lastTime;
    GifClip _cached;

    // Resolve the imported clip. assetTarget is the normal path, but fall back to loading it from the
    // importer's asset path so the preview never depends on assetTarget being populated yet.
    GifClip Clip
    {
        get
        {
            if (_cached != null) return _cached;
            if (assetTarget is GifClip c) { _cached = c; return _cached; }
            if (target is GifImporter imp) _cached = AssetDatabase.LoadAssetAtPath<GifClip>(imp.assetPath);
            return _cached;
        }
    }

    public override bool HasPreviewGUI() => Clip != null && Clip.FrameCount > 0;

    public override GUIContent GetPreviewTitle() => new GUIContent("GIF Preview");

    // Only keep repainting (and burning frames) while actually animating.
    public override bool RequiresConstantRepaint() => _play && HasPreviewGUI();

    public override void OnPreviewSettings()
    {
        GifClip clip = Clip;
        if (clip == null) return;

        GUILayout.Label($"{clip.FrameCount} fr", EditorStyles.miniLabel);

        if (GUILayout.Button($"{_speed:0.##}x", EditorStyles.miniButton, GUILayout.Width(40)))
            _speed = _speed >= 4f ? 0.25f : _speed * 2f;

        GUIContent icon = EditorGUIUtility.IconContent(_play ? "PauseButton" : "PlayButton");
        if (GUILayout.Button(icon, EditorStyles.miniButton, GUILayout.Width(30)))
            _play = !_play;
    }

    public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
    {
        GifClip clip = Clip;
        if (clip == null || clip.FrameCount == 0) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;

        if (_play)
        {
            if (dt < 0f || dt > 0.5f) dt = 0f; // ignore editor stalls / first frame
            _timer += dt * Mathf.Max(0f, _speed);
            float delay = Mathf.Max(0.0001f, clip.GetDelay(_frame));
            while (_timer >= delay)
            {
                _timer -= delay;
                _frame = (_frame + 1) % clip.FrameCount;
                delay = Mathf.Max(0.0001f, clip.GetDelay(_frame));
            }
        }

        if (_frame >= clip.FrameCount) _frame = 0;
        Sprite sprite = clip.GetFrame(_frame);
        if (sprite == null || sprite.texture == null) return;

        // ScaleToFit preserves aspect; the checkerboard makes gif transparency visible.
        EditorGUI.DrawTextureTransparent(r, sprite.texture, ScaleMode.ScaleToFit);
    }

    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
    {
        GifClip clip = null;
        foreach (Object o in subAssets)
            if (o is GifClip c) { clip = c; break; }
        if (clip == null) clip = AssetDatabase.LoadAssetAtPath<GifClip>(assetPath);
        if (clip == null || clip.FrameCount == 0 || clip.GetFrame(0) == null)
            return base.RenderStaticPreview(assetPath, subAssets, width, height);

        try
        {
            Texture2D src = clip.GetFrame(0).texture;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.SetPixels32(src.GetPixels32());
            copy.Apply();
            return copy;
        }
        catch
        {
            return base.RenderStaticPreview(assetPath, subAssets, width, height);
        }
    }
}
