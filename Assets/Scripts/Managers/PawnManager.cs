using System;
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The authority on when and where pawns spawn and despawn.
/// Responsibilities:
/// - Spawns and respawns pawns for both human players and bots.
/// - Determines Pawn spawn locations and times.
/// - Listens to pawn lifecycle and reports lifecycle events.
///
/// Respawn timing is delegated to the pure <see cref="RespawnProcessor"/> in
/// <c>Arena.Respawn.Core</c>. This MonoBehaviour is responsible only for the Unity glue:
/// maintaining a NetworkPlayer &lt;-&gt; key map, ticking the processor each frame on the
/// server, and invoking <see cref="SpawnPawnForPlayer"/> when the processor reports a
/// player is ready to come back.
/// </summary>
public sealed class PawnManager : NetworkBehaviour
{
    public event Action<Pawn, DamageInfo> OnPawnKilled;

    [SerializeField] private Pawn humanPawnPrefab;
    [SerializeField] private Pawn botPawnPrefab;
    [SerializeField] private Transform[] spawnPoints;

    private int nextSpawnIndex;
    private readonly List<Pawn> pawns = new();
    private Dictionary<Pawn, Action<DamageInfo>> deathHandlers = new();

    // Respawn simulation. RespawnState owns the list of pending respawns; we feed in the
    // NetworkPlayer reference itself as the opaque key, so reference-equality lookups
    // round-trip through Core without it ever needing a FishNet dependency.
    private RespawnConfig respawnConfig = RespawnConfig.Default;
    private RespawnState respawnState = RespawnState.Default;
    private readonly List<object> newlyKilledThisTick = new();

    /// <summary>
    /// All currently living pawns.
    /// </summary>
    public IReadOnlyList<Pawn> ActivePawns => pawns;

    [Server]
    public void SpawnPawnForPlayer(NetworkPlayer player)
    {
        if (spawnPoints.Length == 0)
        {
            Debug.LogError("No spawn points assigned to PawnManager!");
            return;
        }

        Transform sp = spawnPoints[nextSpawnIndex];
        nextSpawnIndex = (nextSpawnIndex + 1) % spawnPoints.Length;

        var prefab = player.IsBot ? botPawnPrefab : humanPawnPrefab;
        Pawn pawnInstance = Instantiate(prefab, sp.position, sp.rotation);
        pawnInstance.GetComponent<NetworkPlayerOwnership>().AssignOwner(player);
        Spawn(pawnInstance, player.Owner);

        pawnInstance.ControllingPlayer.Value = player;
        player.ControlledPawn.Value = pawnInstance;

        Action<DamageInfo> handler = (damageInfo) => OnPawnDeath(pawnInstance, damageInfo);
        pawnInstance.OnDeath += handler;
        deathHandlers[pawnInstance] = handler;

        pawns.Add(pawnInstance);
    }

    [Server]
    public void UnregisterPawnForPlayer(NetworkPlayer player)
    {
        var pawn = player.ControlledPawn.Value;
        if (pawn != null)
            UnregisterPawn(pawn);
    }

    [Server]
    public void ClearPawns()
    {
        foreach(var pawn in pawns)
        {
            pawn.OnDeath -= deathHandlers[pawn];
            deathHandlers.Remove(pawn);
            Despawn(pawn);
        }

        pawns.Clear();

        // Any pending respawns belong to the match that just ended; drop them.
        respawnState.PendingRespawns?.Clear();
        respawnState.ReadyThisTick?.Clear();
        newlyKilledThisTick.Clear();
    }

    [Server]
    private void UnregisterPawn(Pawn pawn)
    {
        pawn.OnDeath -= deathHandlers[pawn];
        deathHandlers.Remove(pawn);
        pawns.Remove(pawn);
        Despawn(pawn);
    }

    [Server]
    private void OnPawnDeath(Pawn pawn, DamageInfo damageInfo)
    {
        UnregisterPawn(pawn);
        OnPawnKilled?.Invoke(pawn, damageInfo);
    }

    [Server]
    public void ServerStartRespawnTimerFor(NetworkPlayer player, float respawnDelay)
    {
        if (player == null) return;

        // Project the per-call delay into the processor's config. Each call effectively
        // sets the delay used for the entry enqueued this tick. (All entries enqueued in
        // a single Process call share whatever DelaySeconds the config holds at that moment.)
        respawnConfig.DelaySeconds = respawnDelay;

        // SyncVar projection of the absolute respawn time, matching the previous behaviour
        // so existing HUD code (NetworkUIController) keeps working unchanged.
        double spawnTime = TimeManager.TicksToTime() + respawnDelay;
        player.RespawnTimeEnd.Value = spawnTime;

        // Stage the player; the next Update tick will enqueue it in the processor.
        newlyKilledThisTick.Add(player);
    }

    private void Update()
    {
        if (!IsServerStarted) return;

        // Nothing to do when no one is pending and no one just died.
        if (newlyKilledThisTick.Count == 0
            && (respawnState.PendingRespawns == null || respawnState.PendingRespawns.Count == 0))
        {
            return;
        }

        var input = new RespawnInput
        {
            NewlyKilled = newlyKilledThisTick.Count > 0 ? newlyKilledThisTick : null,
        };

        respawnState = RespawnProcessor.Process(respawnState, in respawnConfig, in input, Time.deltaTime);
        newlyKilledThisTick.Clear();

        // Spawn anyone whose timer hit zero this tick. ReadyThisTick is a one-shot signal -
        // it will be cleared by the processor on the next Process call.
        var ready = respawnState.ReadyThisTick;
        if (ready == null || ready.Count == 0) return;

        for (int i = 0; i < ready.Count; i++)
        {
            if (ready[i] is NetworkPlayer player && player != null)
            {
                player.RespawnTimeEnd.Value = -1;
                SpawnPawnForPlayer(player);
            }
        }
    }
}
