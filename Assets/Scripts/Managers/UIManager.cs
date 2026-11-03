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
        if (!levelLoading) return;
        levelLoading = false;
        if (pendingShow != null)
            pendingShow();
        else if (viewBeforeLoad != null && viewBeforeLoad is not LoadingView)
            ShowView(viewBeforeLoad);
        pendingShow = null;
        viewBeforeLoad = null;
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
        bool shouldShowMenuCamera = typeof(T) != typeof(MainView);
        menuCamera.gameObject.SetActive(shouldShowMenuCamera);
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

    private void ShowView(View target)
    {
        menuCamera.gameObject.SetActive(target is not MainView);
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