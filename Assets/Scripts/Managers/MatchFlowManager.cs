using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public enum MatchState
{
    Pregame,
    PregameCountdown,
    During,
    PostgameCountdown,
    Postgame,
}

/// <summary>
/// Governs the specific rules, states, and flow of a game match.
/// Responsibilities:
/// - Tracks and exposes the match state and the pre-game countdown.
/// - Executes the server-authoritative countdown logic by ticking
///   <see cref="MatchCountdownProcessor"/> each frame on the server.
/// </summary>
public sealed class MatchFlowManager : NetworkBehaviour
{
    public static MatchFlowManager Instance { get; private set; }

    // Test/teardown hook so EditMode tests can reset the static reference between runs.
    public static void ResetInstance() => Instance = null;

    public readonly SyncVar<int> CountdownTime = new();
    public readonly SyncVar<MatchState> State = new(MatchState.Pregame);

    [SerializeField] private int beginMatchCountdown = 3;
    [SerializeField] private int endMatchCountdown = 20;

    // Server-only countdown simulation.
    private MatchCountdownState _countdownState = MatchCountdownState.Default;
    private MatchCountdownConfig _countdownConfig = MatchCountdownConfig.Default;
    private MatchState _countdownNextState;

    private void Awake() => Instance = this;

    [Server]
    public void StartDebugMatch()
    {
        State.Value = MatchState.During;
    }

    [Server]
    public void EnterPregame()
    {
        State.Value = MatchState.Pregame;
    }

    [Server]
    public void EnterPregameCountdown()
    {
        ServerStopCountdown();
        State.Value = MatchState.PregameCountdown;
        BeginCountdown(beginMatchCountdown, MatchState.During);
    }

    [Server]
    public void EnterPostgameCountdown()
    {
        ServerStopCountdown();
        State.Value = MatchState.PostgameCountdown;
        BeginCountdown(endMatchCountdown, MatchState.Postgame);
    }

    [Server]
    public void ServerStopCountdown()
    {
        if (!_countdownState.IsActive) return;
        _countdownState = MatchCountdownState.Default;
    }

    [Server]
    private void BeginCountdown(int durationSeconds, MatchState nextState)
    {
        _countdownConfig = new MatchCountdownConfig { DurationSeconds = durationSeconds };
        _countdownState = MatchCountdownState.Begin(durationSeconds);
        _countdownNextState = nextState;
        // Mirror the original coroutine: CountdownTime.Value is set to the starting
        // value immediately on the same tick the countdown begins.
        CountdownTime.Value = _countdownState.DisplaySeconds;
    }

    private void Update()
    {
        // Server-authoritative: only the server runs the countdown simulation.
        if (!IsServerInitialized) return;
        if (!_countdownState.IsActive) return;

        int previousDisplay = _countdownState.DisplaySeconds;

        var input = MatchCountdownInput.Default;
        _countdownState = MatchCountdownProcessor.Process(
            _countdownState, in _countdownConfig, in input, Time.deltaTime);

        // Project the simulation onto the SyncVar surface other systems read.
        if (_countdownState.DisplaySeconds != previousDisplay)
        {
            CountdownTime.Value = _countdownState.DisplaySeconds;
        }

        if (_countdownState.TransitionFired)
        {
            State.Value = _countdownNextState;
        }
    }
}
