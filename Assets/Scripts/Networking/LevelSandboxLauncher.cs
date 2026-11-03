#if UNITY_EDITOR
using FishNet.Component.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor only: <b>Arena ▸ Level Gen ▸ Play Sandbox</b> plays the dev flow from Boot alone, with the host's online scene
/// switched from Debug to LevelGenSandbox (no hand-built arena, level generation on, the match starts at once), to walk
/// generated levels solo. The open scenes, Boot's settings and the next ordinary Play are left as they were.
/// </summary>
[InitializeOnLoad]
public static class LevelSandboxLauncher
{
    private const string FlagKey = "Arena.LevelGen.PlaySandbox";
    private const string BootScene = "Assets/Scenes/Boot.unity";
    private const string DebugScene = "Assets/Scenes/Debug.unity";
    private const string SandboxScene = "Assets/Scenes/LevelGenSandbox.unity";

    // Subscribed on every domain load: entering play mode reloads the domain, which drops a subscription made only in
    // Play(). The flag lives in SessionState, which survives the reload.
    static LevelSandboxLauncher() => EditorApplication.playModeStateChanged += Restore;

    [MenuItem("Arena/Level Gen/Play Sandbox")]
    private static void Play()
    {
        if (EditorApplication.isPlaying) return;
        SessionState.SetBool(FlagKey, true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScene);
        EditorApplication.EnterPlaymode();
    }

    private static void Restore(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(FlagKey, false)) return;
        EditorSceneManager.playModeStartScene = null;
        SessionState.EraseBool(FlagKey);
    }

    /// <summary>Before Boot starts the host: point the dev network manager's online scene at the sandbox.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SwapOnlineScene()
    {
        if (!SessionState.GetBool(FlagKey, false)) return;
        foreach (var scene in Object.FindObjectsByType<DefaultScene>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (scene.GetOnlineScene() == DebugScene)
                scene.SetOnlineScene(SandboxScene);
    }
}
#endif
