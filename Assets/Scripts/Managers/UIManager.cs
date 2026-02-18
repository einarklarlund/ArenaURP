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
    }

    private void OnDisable()
    {
        NetworkUIEvents.OnMatchStateChanged -= HandleMatchStateChanged;
        NetworkUIEvents.OnLocalPawnSpawned -= HandleLocalSpawn;
        NetworkUIEvents.OnLocalDeath -= HandleLocalDeath;
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
            case MatchState.During:
                Show<MainView>();
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
        foreach(var view in views)
        {
            if (view is T)
                view.Show();
            else
                view.Hide();
        }
    }
}