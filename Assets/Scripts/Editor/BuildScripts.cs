using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.Linq;

public class BuildScripts
{
    [MenuItem("Build/Build WebGL Development")]
    public static void BuildWebGLDevelopment()
    {
        string buildPath = "builds/webgl";
        
        // Get all enabled scenes in Build Settings
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        // Configure build options for a Development build
        BuildPlayerOptions buildPlayerOptions = new()
        {
            scenes = scenes,
            locationPathName = buildPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.Development | BuildOptions.ConnectWithProfiler
        };

        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    /// <summary>
    /// The release web build CI deploys (game-ci's <c>buildMethod</c>). Code optimization is an Editor user setting,
    /// kept in Library rather than ProjectSettings, so a CI Library would otherwise build with whatever it last had
    /// (this machine had "Shorter Build Time", the least optimized): set it here. game-ci passes the output folder as
    /// <c>-customBuildPath</c>.
    /// </summary>
    // Measured 2026-11-16 in Chrome, generating and planning 5 levels with WebAssembly 2023 on: Runtime Speed with LTO
    // took ~405 ms and Disk Size with LTO ~465 ms (Shorter Build Time without Wasm 2023: ~650 ms). Speed costs ~2.7 MB
    // more gzipped wasm (11.9 vs 9.2 MB), downloaded once and cached; a shooter's frame time matters more.
    private const UnityEditor.WebGL.WasmCodeOptimization ReleaseCodeOptimization = UnityEditor.WebGL.WasmCodeOptimization.RuntimeSpeedLTO;

    public static void BuildWebGLRelease()
    {
        string path = GetArgValue(Environment.GetCommandLineArgs(), "-customBuildPath") ?? "builds/webgl_release";
        var previous = UnityEditor.WebGL.UserBuildSettings.codeOptimization;
        UnityEditor.WebGL.UserBuildSettings.codeOptimization = ReleaseCodeOptimization;
        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"Build Failed: {report.summary.result}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
        finally
        {
            UnityEditor.WebGL.UserBuildSettings.codeOptimization = previous;
        }
    }

    public static void BuildProject()
    {
        string[] args = Environment.GetCommandLineArgs();
        
        // Extract our custom arguments
        string platform = GetArgValue(args, "-buildPlatform");
        bool isDevelopment = args.Contains("-development");
        bool isProfiler = args.Contains("-profiler");

        BuildPlayerOptions buildPlayerOptions = new()
        {
            scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray()
        };

        // Configure Platform Specifics
        switch (platform)
        {
            case "linux":
                buildPlayerOptions.target = BuildTarget.StandaloneLinux64;
                buildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Server;
                buildPlayerOptions.locationPathName = "builds/linux_server/arena-server.x86_64";
                break;

            case "mac":
                buildPlayerOptions.target = BuildTarget.StandaloneOSX;
                buildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Player;
                buildPlayerOptions.locationPathName = "builds/mac_client/Arena.app";
                break;

            case "windows":
                buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
                buildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Player;
                buildPlayerOptions.locationPathName = "builds/windows_client/Arena.exe";
                break;

            default:
                Debug.LogError($"Invalid platform: {platform}. Use linux, mac, or windows.");
                EditorApplication.Exit(1);
                return;
        }

        // Set Flags
        buildPlayerOptions.options = BuildOptions.None;
        if (isDevelopment)
        {
            buildPlayerOptions.options |= BuildOptions.Development | BuildOptions.AllowDebugging;
        }
        if (isProfiler)
        {
            buildPlayerOptions.options |= BuildOptions.ConnectWithProfiler;
        }

        Debug.Log($"Starting {platform} build at {buildPlayerOptions.locationPathName}...");
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build Failed: {report.summary.result}");
            EditorApplication.Exit(1);
        }
    }

    // Helper to find the value following a specific flag
    private static string GetArgValue(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        return (index >= 0 && index < args.Length - 1) ? args[index + 1] : null;
    }
}