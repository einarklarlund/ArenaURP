using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.Linq;

public class BuildScripts
{
    public static void BuildProject()
    {
        string[] args = Environment.GetCommandLineArgs();
        
        // Extract our custom arguments
        string platform = GetArgValue(args, "-buildPlatform");
        bool isDevelopment = args.Contains("-development");
        bool isProfiler = args.Contains("-profiler");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

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