using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place that decides which camera is live. Anything that wants the screen claims its camera with a priority;
/// the highest claim wins (ties go to the latest claim). The live camera gets its Camera and AudioListener enabled and
/// the MainCamera tag, so Camera.main follows it; every other claimed camera is switched off and untagged.
/// Cameras nobody claims, like the character preview, are left alone.
/// </summary>
public static class CameraDirector
{
    /// <summary>Named claim priorities, lowest first.</summary>
    public static class Priority
    {
        /// <summary>Scene cameras kept for the Game tab in edit mode: live only when nothing else is claimed.</summary>
        public const int Scene = -10;
        /// <summary>The menu camera between menus, so a screen camera is always live and the overlay UI always draws.</summary>
        public const int Fallback = 0;
        public const int Pawn = 10;
        /// <summary>The menu camera while a menu view is up.</summary>
        public const int Menu = 20;
        /// <summary>A camera dropped into a scene to look through.</summary>
        public const int Override = 100;
    }

    private struct Claim
    {
        public Camera Camera;
        public int Priority;
        public int Order;
    }

    private static readonly List<Claim> claims = new();
    private static int nextOrder;

    public static Camera Live { get; private set; }

    public static event Action<Camera> OnLiveCameraChanged;

    // Statics survive entering play mode when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        claims.Clear();
        nextOrder = 0;
        Live = null;
        OnLiveCameraChanged = null;
    }

    /// <summary>Claims the screen for this camera, or changes the priority of its existing claim.</summary>
    public static void Request(Camera camera, int priority)
    {
        claims.RemoveAll(c => c.Camera == camera);
        claims.Add(new Claim { Camera = camera, Priority = priority, Order = nextOrder++ });
        Refresh();
    }

    /// <summary>Drops this camera's claim and switches it off.</summary>
    public static void Release(Camera camera)
    {
        if (claims.RemoveAll(c => c.Camera == camera) == 0)
            return;
        if (camera != null)
            SetLive(camera, false);
        Refresh();
    }

    private static void Refresh()
    {
        // A claimed camera destroyed without releasing (scene unload) gives up its claim.
        claims.RemoveAll(c => c.Camera == null);

        Camera winner = null;
        int bestPriority = int.MinValue, bestOrder = int.MinValue;
        foreach (var claim in claims)
        {
            if (claim.Priority > bestPriority || (claim.Priority == bestPriority && claim.Order > bestOrder))
            {
                winner = claim.Camera;
                bestPriority = claim.Priority;
                bestOrder = claim.Order;
            }
        }

        foreach (var claim in claims)
            if (claim.Camera != winner)
                SetLive(claim.Camera, false);
        if (winner != null)
            SetLive(winner, true);

        if (winner == Live)
            return;
        Live = winner;
        OnLiveCameraChanged?.Invoke(winner);
    }

    private static void SetLive(Camera camera, bool live)
    {
        camera.enabled = live;
        if (camera.TryGetComponent<AudioListener>(out var listener))
            listener.enabled = live;
        camera.tag = live ? "MainCamera" : "Untagged";
    }
}
