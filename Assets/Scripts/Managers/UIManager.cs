using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The central orchestrator for UI visibility and transitions. Exactly one primary View is
/// displayed at a time; showing one hides every other. While a level loads, LoadingView stays up: the view a match-state,
/// spawn or death event asks for meanwhile is shown once the load finishes.
/// </summary>
public sealed class UIManager : MonoBehaviour
{
    [SerializeField] private List<View> views;
    [SerializeField] private Camera menuCamera;
    [Tooltip("What the menu camera orbits. Moved to the middle of each generated level.")]
    [SerializeField] private Transform menuCameraPivot;

    private View previousView;
    private View currentView;

    private bool levelLoading;
    private View viewBeforeLoad;
    private System.Action pendingShow;

    private void OnEnable()
    {
        NetworkUIEvents.OnClientConnectionChanged += HandleClientConnectionChanged;
        NetworkUIEvents.OnMatchStateChanged += HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;
        NetworkUIEvents.OnRespawnTimerStarted += HandleRespawnTimerStarted;

        LocalUIEvents.OnHostInitiated += Show<LoadingView>;
        LocalUIEvents.OnJoinInitiated += Show<LoadingView>;
        LocalUIEvents.OnJoinCancelled += Show<MainMenuView>;
        LocalUIEvents.OnSettingsOpened += Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed += ShowPreviousView;
        LocalUIEvents.OnPause += Show<PauseView>;
        LocalUIEvents.OnUnpause += Show<MainView>;
        LocalUIEvents.OnRoomBrowserOpened += Show<RoomBrowserView>;
        LocalUIEvents.OnRoomBrowserClosed += ShowPreviousView;
        LocalUIEvents.OnHostOptionsOpened += Show<HostOptionsView>;
        LocalUIEvents.OnHostOptionsClosed += ShowPreviousView;
        LocalUIEvents.OnDebugViewOpened += Show<DebugView>;
        LocalUIEvents.OnDebugViewClosed += ShowPreviousView;
        LocalUIEvents.OnCharacterSelectOpened += Show<CharacterSelectorView>;
        LocalUIEvents.OnCharacterSelectClosed += ShowPreviousView;
        LocalUIEvents.OnLevelLoadStarted += HandleLevelLoadStarted;
        LocalUIEvents.OnLevelLoadFinished += HandleLevelLoadFinished;

        Show<MainMenuView>();
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnClientConnectionChanged -= HandleClientConnectionChanged;
        NetworkUIEvents.OnMatchStateChanged -= HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;
        NetworkUIEvents.OnRespawnTimerStarted -= HandleRespawnTimerStarted;

        LocalUIEvents.OnHostInitiated -= Show<LoadingView>;
        LocalUIEvents.OnJoinInitiated -= Show<LoadingView>;
        LocalUIEvents.OnJoinCancelled -= Show<MainMenuView>;
        LocalUIEvents.OnSettingsOpened -= Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed -= ShowPreviousView;
        LocalUIEvents.OnPause -= Show<PauseView>;
        LocalUIEvents.OnUnpause -= Show<MainView>;
        LocalUIEvents.OnRoomBrowserOpened -= Show<RoomBrowserView>;
        LocalUIEvents.OnRoomBrowserClosed -= ShowPreviousView;
        LocalUIEvents.OnHostOptionsOpened -= Show<HostOptionsView>;
        LocalUIEvents.OnHostOptionsClosed -= ShowPreviousView;
        LocalUIEvents.OnDebugViewOpened -= Show<DebugView>;
        LocalUIEvents.OnDebugViewClosed -= ShowPreviousView;
        LocalUIEvents.OnCharacterSelectOpened -= Show<CharacterSelectorView>;
        LocalUIEvents.OnCharacterSelectClosed -= ShowPreviousView;
        LocalUIEvents.OnLevelLoadStarted -= HandleLevelLoadStarted;
        LocalUIEvents.OnLevelLoadFinished -= HandleLevelLoadFinished;

        CameraDirector.Release(menuCamera);
    }

    private void HandleClientConnectionChanged(bool connected)
    {
        if (!connected)
        {
            levelLoading = false;
            pendingShow = null;
            Show<MainMenuView>();
        }
    }

    private void HandleLevelLoadStarted()
    {
        if (levelLoading) return;
        levelLoading = true;
        viewBeforeLoad = currentView;
        pendingShow = null;
        Show<LoadingView>();
    }

    private void HandleLevelLoadFinished()
    {
        CenterMenuCameraPivot();
        if (!levelLoading) return;
        levelLoading = false;
        if (pendingShow != null)
            pendingShow();
        else if (viewBeforeLoad != null && viewBeforeLoad is not LoadingView)
            ShowView(viewBeforeLoad);
        pendingShow = null;
        viewBeforeLoad = null;
    }

    // Levels are built in level space, from the world origin to their size on x and z.
    private void CenterMenuCameraPivot()
    {
        var level = LevelSync.Instance != null ? LevelSync.Instance.Level : null;
        if (level == null || menuCameraPivot == null) return;
        var position = menuCameraPivot.position;
        menuCameraPivot.position = new Vector3(level.Size.x * 0.5f, position.y, level.Size.z * 0.5f);
    }

    /// <summary>Whether to hold <paramref name="show"/> until the level load finishes.</summary>
    private bool DeferWhileLoading(System.Action show)
    {
        if (!levelLoading) return false;
        pendingShow = show;
        return true;
    }

    private void HandleMatchStateChanged(MatchState state)
    {
        if (DeferWhileLoading(() => HandleMatchStateChanged(state))) return;
        switch (state)
        {
            case MatchState.Pregame:
                Show<LobbyView>();
                break;
            case MatchState.PregameCountdown:
                Show<PregameCountdownView>();
                break;
            case MatchState.PostgameCountdown:
                Show<PostgameCountdownView>();
                break;
            case MatchState.During:
                // Every player picks upgrades before their first spawn, so there may be no pawn yet.
                NetworkPlayer local = NetworkPlayer.LocalInstance;
                if (local != null && local.ControlledPawn.Value == null)
                    Show<FirstSpawnView>();
                else
                    Show<MainView>();
                break;
            default:
                // No view change for this match state (e.g. During / Postgame are
                // driven by pawn-spawn / death events instead).
                break;
        }
    }

    private void HandleLocalDeath()
    {
        if (DeferWhileLoading(Show<DeathView>)) return;
        Show<DeathView>();
    }

    // Picking upgrades before a spawn. After a death DeathView is the pick screen (the death and the pick timer
    // arrive in either order); otherwise this is the first spawn of the match.
    private void HandleRespawnTimerStarted(double endTime)
    {
        if (currentView is DeathView) return;
        if (DeferWhileLoading(Show<FirstSpawnView>)) return;
        Show<FirstSpawnView>();
    }

    private void HandleLocalSpawn(Pawn pawn)
    {
        if (DeferWhileLoading(Show<MainView>)) return;
        Show<MainView>();
    }

    private void Show<T>() where T : View
    {
        RequestMenuCamera(!ShowsPawnCamera(views.Find(view => view is T)));
        previousView = currentView;
        foreach(var view in views)
        {
            if (view is T)
            {
                currentView = view;
                view.Show();
            }
            else
                view.Hide();
        }
    }

    // A menu takes the screen from the pawn. Under MainView the menu camera stays claimed as the fallback, so a screen
    // camera is always live to draw the overlay UI.
    private void RequestMenuCamera(bool menuShowing)
    {
        CameraDirector.Request(menuCamera, menuShowing ? CameraDirector.Priority.Menu : CameraDirector.Priority.Fallback);
    }

    // The pause menu, and the settings or debug view opened from it, draw over the pawn's camera.
    private bool ShowsPawnCamera(View target) =>
        target is MainView or PauseView
        || (target is SettingsView or DebugView && currentView != null && ShowsPawnCamera(currentView));

    private void ShowView(View target)
    {
        RequestMenuCamera(!ShowsPawnCamera(target));
        previousView = currentView;
        foreach (var view in views)
        {
            if (view == target)
            {
                currentView = view;
                view.Show();
            }
            else
                view.Hide();
        }
    }

    private void ShowPreviousView()
    {
        currentView.Hide();
        previousView.Show();
        (previousView, currentView) = (currentView, previousView);
    }
}