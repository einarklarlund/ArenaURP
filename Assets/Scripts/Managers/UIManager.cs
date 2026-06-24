using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The central orchestrator for UI visibility and transitions.
/// Responsibilities:
/// - Manages the lifecycle of UI Views (Show/Hide/Initialize).
/// - Listens to the NetworkUIEvents bus to decide which View should be active.
/// - Ensures only one primary View is displayed at a time to prevent UI overlap.
/// </summary>
public sealed class UIManager : MonoBehaviour
{
    [SerializeField] private List<View> views;
    [SerializeField] private Camera menuCamera;

    private View previousView;
    private View currentView;

    private void OnEnable()
    {
        // High level network events
        NetworkUIEvents.OnClientConnectionChanged += HandleClientConnectionChanged;

        // Match events
        NetworkUIEvents.OnMatchStateChanged += HandleMatchStateChanged;

        // Pawn events
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;

        // Loading screen events
        LocalUIEvents.OnHostInitiated += Show<LoadingView>;
        LocalUIEvents.OnJoinInitiated += Show<LoadingView>;

        // Settings events
        LocalUIEvents.OnSettingsOpened += Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed += ShowPreviousView;

        // Pause events
        LocalUIEvents.OnPause += Show<PauseView>;
        LocalUIEvents.OnUnpause += Show<MainView>;

        // Server browser events
        LocalUIEvents.OnRoomBrowserOpened += Show<RoomBrowserView>;
        LocalUIEvents.OnRoomBrowserClosed += ShowPreviousView;

        // Host options events
        LocalUIEvents.OnHostOptionsOpened += Show<HostOptionsView>;
        LocalUIEvents.OnHostOptionsClosed += ShowPreviousView;

        // Debug events
        LocalUIEvents.OnDebugViewOpened += Show<DebugView>;
        LocalUIEvents.OnDebugViewClosed += ShowPreviousView;

        Show<MainMenuView>();
    }

    private void OnDisable()
    {
        // High level network events
        NetworkUIEvents.OnClientConnectionChanged -= HandleClientConnectionChanged;

        // Match events
        NetworkUIEvents.OnMatchStateChanged -= HandleMatchStateChanged;
        
        // Pawn events
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;

        // Loading screen events
        LocalUIEvents.OnHostInitiated -= Show<LoadingView>;
        LocalUIEvents.OnJoinInitiated -= Show<LoadingView>;

        // Settings events
        LocalUIEvents.OnSettingsOpened -= Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed -= ShowPreviousView;

        // Pause events
        LocalUIEvents.OnPause -= Show<PauseView>;
        LocalUIEvents.OnUnpause -= Show<MainView>;

        // Server browser events
        LocalUIEvents.OnRoomBrowserOpened -= Show<RoomBrowserView>;
        LocalUIEvents.OnRoomBrowserClosed -= ShowPreviousView;

        // Host options events
        LocalUIEvents.OnHostOptionsOpened -= Show<HostOptionsView>;
        LocalUIEvents.OnHostOptionsClosed -= ShowPreviousView;

        // Debug events
        LocalUIEvents.OnDebugViewOpened -= Show<DebugView>;
        LocalUIEvents.OnDebugViewClosed -= ShowPreviousView;
    }

    private void HandleClientConnectionChanged(bool connected)
    {
        if(!connected)
            Show<MainMenuView>();
    }

    private void HandleMatchStateChanged(MatchState state)
    {
        var viewKind = UIStateMapper.MatchStateToViewKind(ToUIMatchState(state));
        switch (viewKind)
        {
            case UIStateMapper.ViewKind.Lobby:
                Show<LobbyView>();
                break;
            case UIStateMapper.ViewKind.PregameCountdown:
                Show<PregameCountdownView>();
                break;
            case UIStateMapper.ViewKind.PostgameCountdown:
                Show<PostgameCountdownView>();
                break;
            case UIStateMapper.ViewKind.Main:
                Show<MainView>();
                break;
            case UIStateMapper.ViewKind.None:
            default:
                // No view change for this match state (e.g. During / Postgame are
                // driven by pawn-spawn / death events instead).
                break;
        }
    }

    private static UIStateMapper.UIMatchState ToUIMatchState(MatchState state)
    {
        switch (state)
        {
            case MatchState.Pregame:           return UIStateMapper.UIMatchState.Pregame;
            case MatchState.PregameCountdown:  return UIStateMapper.UIMatchState.PregameCountdown;
            case MatchState.During:            return UIStateMapper.UIMatchState.During;
            case MatchState.PostgameCountdown: return UIStateMapper.UIMatchState.PostgameCountdown;
            case MatchState.Postgame:          return UIStateMapper.UIMatchState.Postgame;
            default:                           return UIStateMapper.UIMatchState.Pregame;
        }
    }

    private void HandleLocalDeath()
    {
        Show<DeathView>();
    }

    private void HandleLocalSpawn(Pawn pawn)
    {
        Show<MainView>();
    }

    private void Show<T>() where T : View
    {
        var viewKind = ViewTypeToViewKind(typeof(T));
        menuCamera.gameObject.SetActive(UIStateMapper.ShouldActivateMenuCamera(viewKind));
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

    private void ShowPreviousView()
    {
        currentView.Hide();
        previousView.Show();
        (previousView, currentView) = (currentView, previousView);
    }

    private static UIStateMapper.ViewKind ViewTypeToViewKind(System.Type viewType)
    {
        if (viewType == typeof(MainView))               return UIStateMapper.ViewKind.Main;
        if (viewType == typeof(LobbyView))              return UIStateMapper.ViewKind.Lobby;
        if (viewType == typeof(PregameCountdownView))   return UIStateMapper.ViewKind.PregameCountdown;
        if (viewType == typeof(PostgameCountdownView))  return UIStateMapper.ViewKind.PostgameCountdown;
        return UIStateMapper.ViewKind.None;
    }
}