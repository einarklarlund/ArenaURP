using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The central orchestrator for UI visibility and transitions. Exactly one primary View is
/// displayed at a time; showing one hides every other.
/// </summary>
public sealed class UIManager : MonoBehaviour
{
    [SerializeField] private List<View> views;
    [SerializeField] private Camera menuCamera;

    private View previousView;
    private View currentView;

    private void OnEnable()
    {
        NetworkUIEvents.OnClientConnectionChanged += HandleClientConnectionChanged;
        NetworkUIEvents.OnMatchStateChanged += HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;

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

        Show<MainMenuView>();
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnClientConnectionChanged -= HandleClientConnectionChanged;
        NetworkUIEvents.OnMatchStateChanged -= HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;

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

    // Deliberately a switch expression with NO default arm: an unmapped MatchState
    // throws SwitchExpressionException at runtime and emits the CS8509
    // non-exhaustive warning. (Unity ignores .editorconfig diagnostic severity, so
    // that warning is not a build error.) This side lives in Assembly-CSharp, not
    // Arena.*.Core, so it is not covered by the EditMode completeness test; the
    // switch's runtime throw is its guard. Adding a MatchState value also forces a
    // new UIMatchState arm, which MatchStateToViewKind_IsTotal does catch.
    private static UIStateMapper.UIMatchState ToUIMatchState(MatchState state) => state switch
    {
        MatchState.Pregame           => UIStateMapper.UIMatchState.Pregame,
        MatchState.PregameCountdown  => UIStateMapper.UIMatchState.PregameCountdown,
        MatchState.During            => UIStateMapper.UIMatchState.During,
        MatchState.PostgameCountdown => UIStateMapper.UIMatchState.PostgameCountdown,
        MatchState.Postgame          => UIStateMapper.UIMatchState.Postgame,
    };

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