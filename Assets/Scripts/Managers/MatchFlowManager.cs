using System;
using System.Collections;
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
/// - Executes the server-authoritative countdown logic.
/// </summary>
public sealed class MatchFlowManager : NetworkBehaviour
{
    private static WaitForSeconds _waitForSeconds1 = new WaitForSeconds(1f);

    public static MatchFlowManager Instance { get; private set; }
    
    public readonly SyncVar<int> CountdownTime = new();
    public readonly SyncVar<MatchState> State = new();
    
    [SerializeField] private int beginMatchCountdown = 3;
    [SerializeField] private int endMatchCountdown = 20;

    private Coroutine countdownRoutine;

    private void Awake() => Instance = this;

    public override void OnStartServer()
    {
        base.OnStartServer();
        State.Value = MatchState.Pregame;
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
        countdownRoutine = StartCoroutine(ServerCountdownSequence(beginMatchCountdown, MatchState.During));
    }

    [Server]
    public void EnterPostgameCountdown()
    {
        ServerStopCountdown();
        State.Value = MatchState.PostgameCountdown;
        countdownRoutine = StartCoroutine(ServerCountdownSequence(endMatchCountdown, MatchState.Postgame));
    }


    [Server]
    public void ServerStopCountdown()
    {
        if(countdownRoutine == null) return;
        StopCoroutine(countdownRoutine);
        countdownRoutine = null;
    }

    [Server]
    private IEnumerator ServerCountdownSequence(int countdownTime, MatchState nextState)
    {
        for (int i = countdownTime; i > 0; i--)
        {
            CountdownTime.Value = i;
            yield return _waitForSeconds1;
        }

        countdownRoutine = null;
        
        State.Value = nextState;
    }
}