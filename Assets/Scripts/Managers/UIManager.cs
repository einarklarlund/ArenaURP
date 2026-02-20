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

    private void Start()
    {
        Show<MainMenuView>();
    }

    private void OnEnable()
    {
        // High level network events
        NetworkUIEvents.OnClientConnectionChanged += HandleClientConnectionChanged;

        // Match events
        NetworkUIEvents.OnMatchStateChanged += HandleMatchStateChanged;

        // Pawn events
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;

        // Settings events
        LocalUIEvents.OnSettingsOpened += Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed += ShowPreviousView;

        // Pause events
        LocalUIEvents.OnPause += Show<PauseView>;
        LocalUIEvents.OnUnpause += Show<MainView>;
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

        // Settings events
        LocalUIEvents.OnSettingsOpened -= Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed -= ShowPreviousView;

        // Pause events
        LocalUIEvents.OnPause -= Show<PauseView>;
        LocalUIEvents.OnUnpause -= Show<MainView>;
    }

    private void HandleClientConnectionChanged(bool connected)
    {
        if(!connected)
            Show<MainMenuView>();
    }

    private void HandleMatchStateChanged(MatchState state)
    {
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
        bool isMenuCameraActive = typeof(T) != typeof(MainView);
        menuCamera.gameObject.SetActive(isMenuCameraActive);
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
}