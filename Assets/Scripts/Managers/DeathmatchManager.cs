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

    // How the leader is stored is this manager's business. Views learn about lead
    // changes through NetworkUIEvents.OnLeadPlayerChanged, published below.
    private readonly SyncVar<NetworkPlayer> leadPlayer = new();

    [SerializeField] private MatchTimerConfig timerConfig = MatchTimerConfig.Default;

    // Pure simulation state. The processor advances this each tick; we only react to transitions.
    private MatchTimerState timerState = MatchTimerState.Default;

    void Awake()
    {
        Instance = this;
    }

    // --- UI bus publishing ---------------------------------------------------
    // The manager broadcasts lead/score changes onto the NetworkUIEvents bus so the
    // postgame view never reaches through DeathmatchManager.Instance.leadPlayer.

    public override void OnStartClient()
    {
        base.OnStartClient();
        leadPlayer.OnChange += HandleLeadPlayerChanged;
        // Seed the bus with the current leader for views coming up mid-match.
        NetworkUIEvents.OnLeadPlayerChanged?.Invoke(leadPlayer.Value);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        leadPlayer.OnChange -= HandleLeadPlayerChanged;
    }

    private void HandleLeadPlayerChanged(NetworkPlayer prev, NetworkPlayer next, bool asServer)
    {
        if (asServer) return;
        NetworkUIEvents.OnLeadPlayerChanged?.Invoke(next);
    }

    /// <summary>Re-broadcast the current leader onto the bus. The lead SyncVar last changed
    /// mid-match, so a postgame view that only just came up would otherwise miss it; the
    /// MatchMediator asks for this re-emit once the flow reaches PostgameCountdown.</summary>
    public void PublishLeadToBus() => NetworkUIEvents.OnLeadPlayerChanged?.Invoke(leadPlayer.Value);

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
