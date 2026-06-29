using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Inspector for <see cref="BillboardGif"/> that adds a "Preview In Scene View" toggle. While on, the gif
/// animates in the Scene view without entering play mode, billboarding toward the Scene view camera.
///
/// The actual animation is driven by <see cref="BillboardGifScenePreviewDriver"/>. The preview is in-memory
/// only ??? toggling it never dirties the scene (the flag is non-serialized and the frame/rotation changes are
/// procedural, which Unity does not auto-dirty), and resting state is restored before any save.
/// </summary>
[CustomEditor(typeof(BillboardGif))]
[CanEditMultipleObjects]
public class BillboardGifEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        var gif = (BillboardGif)target;
        bool on = gif.previewInEditor;

        using (new EditorGUI.DisabledScope(gif.clip == null))
        {
            bool now = GUILayout.Toggle(on, on ? "Stop Scene Preview" : "Preview In Scene View", "Button");
            if (now != on)
            {
                foreach (Object t in targets)
                {
                    var bg = (BillboardGif)t;
                    bg.previewInEditor = now;       // non-serialized: no Undo/SetDirty, no scene dirtying
                    if (!now) bg.EditorPreviewReset();
                }
                if (now) BillboardGifScenePreviewDriver.Kick();
            }
        }

        if (gif.clip == null)
            EditorGUILayout.HelpBox("Assign a Clip to enable Scene-view preview.", MessageType.Info);
    }
}

/// <summary>
/// Editor-only driver that animates every <see cref="BillboardGif"/> whose previewInEditor flag is on. Runs
/// off EditorApplication.update (only in edit mode), faces each preview toward the active Scene view camera,
/// and repaints the Scene view so the animation is smooth. Resting state (first frame + the rotation the
/// object had when preview started) is restored on stop, before a scene save, and before assembly reload, so
/// the preview never leaves a trace in the saved scene.
/// </summary>
[InitializeOnLoad]
public static class BillboardGifScenePreviewDriver
{
    static double _lastTime;
    static double _lastScan;
    static BillboardGif[] _cache = new BillboardGif[0];
    static readonly Dictionary<BillboardGif, Quaternion> _restRotation = new Dictionary<BillboardGif, Quaternion>();

    static BillboardGifScenePreviewDriver()
    {
        EditorApplication.update += OnUpdate;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.ExitingEditMode) RestoreAll();
        };
    }

    /// <summary>Force an immediate rescan so a just-enabled preview starts without waiting for the next scan.</summary>
    public static void Kick() => _lastScan = 0;

    static void OnUpdate()
    {
        if (Application.isPlaying) return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;
        if (dt < 0f || dt > 0.5f) dt = 0f; // ignore editor stalls / first tick

        if (now - _lastScan > 0.5 || _lastScan == 0)
        {
            _cache = Object.FindObjectsByType<BillboardGif>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _lastScan = now;
        }

        // Stop any that turned preview off or were destroyed.
        if (_restRotation.Count > 0)
        {
            List<BillboardGif> stop = null;
            foreach (var kv in _restRotation)
                if (kv.Key == null || !kv.Key.previewInEditor)
                    (stop ??= new List<BillboardGif>()).Add(kv.Key);
            if (stop != null)
                foreach (var c in stop) { if (c != null) Restore(c); _restRotation.Remove(c); }
        }

        Camera cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
        bool any = false;
        foreach (var c in _cache)
        {
            if (c == null || !c.previewInEditor) continue;
            if (!_restRotation.ContainsKey(c)) _restRotation[c] = c.transform.rotation; // snapshot resting pose
            c.EditorPreviewTick(dt, cam);
            any = true;
        }

        if (any && SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.Repaint();
    }

    static void Restore(BillboardGif c)
    {
        if (_restRotation.TryGetValue(c, out Quaternion rot)) c.transform.rotation = rot;
        c.EditorPreviewReset();
    }

    static void RestoreAll()
    {
        foreach (var kv in _restRotation) if (kv.Key != null) Restore(kv.Key);
        _restRotation.Clear();
    }

    // Put previewing objects back to resting state so the saved scene is clean; they resume on the next tick.
    static void OnSceneSaving(Scene scene, string path)
    {
        foreach (var kv in _restRotation)
        {
            BillboardGif c = kv.Key;
            if (c != null && c.gameObject.scene == scene)
            {
                c.transform.rotation = kv.Value;
                c.EditorPreviewReset();
            }
        }
    }
}
