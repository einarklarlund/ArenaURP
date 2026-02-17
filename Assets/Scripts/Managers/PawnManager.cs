using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The authority on when and where pawns spawn and despawn.
/// Responsibilities:
/// - Spawns and respawns pawns.
/// - Determines Pawn spawn locations and times.
/// - Listens to pawn lifecycle and reports lifecycle events.
/// </summary>
public sealed class PawnManager : NetworkBehaviour
{
    public event Action<Pawn, DamageInfo> OnPawnKilled;

    [SerializeField] private Pawn pawnPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private int nextSpawnIndex;
    private readonly List<Pawn> pawns = new();
    private Dictionary<Pawn, Action<DamageInfo>> deathHandlers = new();


    [Server]
    public void SpawnPawnForPlayer(NetworkPlayer player)
    {
        if (spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points assigned to PawnManager!");
            return;
        }

        // Find spawn point
        Transform sp = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;

        // Spawn and initialize pawn
        Pawn pawnInstance = Instantiate(pawnPrefab, sp.position, sp.rotation);
        Spawn(pawnInstance, player.Owner);

        pawnInstance.ControllingPlayer.Value = player;
        player.ControlledPawn.Value = pawnInstance;

        // Listen to lifecycle
        Action<DamageInfo> handler = (damageInfo) => OnPawnDeath(pawnInstance, damageInfo);
        pawnInstance.OnDeath += handler;
        deathHandlers[pawnInstance] = handler;

        pawns.Add(pawnInstance);
    }

    [Server]
    public void ClearPawns()
    {
        foreach(var pawn in pawns)
        {
            pawn.OnDeath -= deathHandlers[pawn];
            Despawn(pawn);
        }

        pawns.Clear();
    }

    [Server]
    private void OnPawnDeath(Pawn pawn, DamageInfo damageInfo)
    {
        pawn.OnDeath -= deathHandlers[pawn];
        pawns.Remove(pawn);
        OnPawnKilled?.Invoke(pawn, damageInfo);
    }

    [Server]
    public void ServerStartRespawnTimerFor(NetworkPlayer player, float respawnDelay)
    {
        // Calculate the exact moment in server time the player should respawn
        double spawnTime = TimeManager.TicksToTime() + respawnDelay;
        player.RespawnTimeEnd.Value = spawnTime;

        StartCoroutine(ServerExecuteSpawnAfterDelay(player, respawnDelay));
    }

    private IEnumerator ServerExecuteSpawnAfterDelay(NetworkPlayer player, float delay)
    {
        yield return new WaitForSeconds(delay);
        player.RespawnTimeEnd.Value = -1; // Reset timer
        SpawnPawnForPlayer(player);
    }
}