using FishNet.Object;
using FishNet.Object.Synchronizing;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class DeathmatchManager : NetworkBehaviour
{
    public static DeathmatchManager Instance;

    public event Action<NetworkPlayer> OnGameModeEnded;

    public float RespawnDelay = 3;

    public readonly SyncVar<NetworkPlayer> leadPlayer = new(); // wish i could keep this private but i'm reading it in PostgameCountdownView

    [SerializeField] private float matchDuration = 600;
    [SerializeField] private float maxKills = 5;

    private float highScore = 0;
    private float timeRemaining;
    private bool isRunning = false;

    void Awake()
    {
        Instance = this;
    }

    [Server]
    public void BeginGame()
    {
        timeRemaining = matchDuration;
        isRunning = true;
        highScore = 0;
    }

    private void Update()
    {
        if (!IsServerStarted || !isRunning) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0)
        {
            ServerEndGame();
        }
    }

    [Server]
    public void ServerRecordKill(NetworkPlayer killer, NetworkPlayer killedPlayer)
    {
        if (killer != null)
        {
            killer.Score.Value++;
            if (killer.Score.Value > highScore)
            {
                leadPlayer.Value = killer;
                highScore = killer.Score.Value;
            }
            CheckGameEnd(killer);
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

    private void CheckGameEnd(NetworkPlayer player)
    {
        if (player.Score.Value >= maxKills)
            ServerEndGame();
    }

    [Server]
    private void ServerEndGame()
    {
        timeRemaining = 0;
        isRunning = false;
        OnGameModeEnded?.Invoke(leadPlayer.Value);
    }
}