using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using UnityEngine;

public sealed class DeathmatchManager : NetworkBehaviour
{
    public static DeathmatchManager Instance;

    // Test/teardown hook so EditMode tests can reset the static reference between runs.
    public static void ResetInstance() => Instance = null;

    public event Action<NetworkPlayer> OnGameModeEnded;

    public float RespawnDelay = 3;

    public readonly SyncVar<NetworkPlayer> leadPlayer = new(); // wish i could keep this private but i'm reading it in PostgameCountdownView

    [SerializeField] private MatchTimerConfig timerConfig = MatchTimerConfig.Default;

    // Pure simulation state. The processor advances this each tick; we only react to transitions.
    private MatchTimerState timerState = MatchTimerState.Default;

    void Awake()
    {
        Instance = this;
    }

    [Server]
    public void BeginGame()
    {
        timerState = MatchTimerState.Begin();
        leadPlayer.Value = null;
    }

    private void Update()
    {
        if (!IsServerStarted || !timerState.IsRunning) return;

        var input = new MatchTimerInput
        {
            LeadingScore = leadPlayer.Value != null ? leadPlayer.Value.Score.Value : 0f,
        };

        var prevIsOver = timerState.IsOver;
        timerState = MatchTimerProcessor.Process(timerState, in timerConfig, in input, Time.deltaTime);

        // Fire the end-of-match event exactly once, on the IsOver false -> true transition.
        if (!prevIsOver && timerState.IsOver)
        {
            OnGameModeEnded?.Invoke(leadPlayer.Value);
        }
    }

    [Server]
    public void ServerRecordKill(NetworkPlayer killer, NetworkPlayer killedPlayer)
    {
        if (killer != null)
        {
            killer.Score.Value++;
            // Track the current leader. The processor reads LeadingScore via leadPlayer.Score
            // each tick, so it will pick up score-threshold wins on the next Update.
            if (killer.Score.Value > timerState.HighScore)
            {
                leadPlayer.Value = killer;
            }
        }
        else
        {
            killedPlayer.Score.Value -= 1;
        }
    }

    [Server]
    public void ResetScore(NetworkPlayer player)
    {
        player.Score.Value = 0;
    }
}
