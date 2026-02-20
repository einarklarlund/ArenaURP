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
        NetworkUIEvents.OnMatchStateChanged += HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned += HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath += HandleLocalDeath;
        NetworkUIEvents.OnClientConnectionChanged += HandleClientConnectionChanged;

        LocalUIEvents.OnSettingsOpened += Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed += ShowPreviousView;
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnMatchStateChanged -= HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;

        LocalUIEvents.OnSettingsOpened -= Show<SettingsView>;
        LocalUIEvents.OnSettingsClosed -= ShowPreviousView;
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