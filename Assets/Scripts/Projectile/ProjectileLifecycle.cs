using System;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(ProjectileData))]
[RequireComponent(typeof(ProjectileState))]
public class ProjectileLifecycle : NetworkBehaviour
{
    private ProjectileData data;
    private ProjectileState state;
    private ProjectileMovement movement;
    private ProjectileHitbox collision;
    private ProjectileVfx vfx;
    
    private bool isInitialized;

    private void Awake()
    {
        data = GetComponent<ProjectileData>();
        state = GetComponent<ProjectileState>();
        movement = GetComponent<ProjectileMovement>();
        collision = GetComponent<ProjectileHitbox>();
        vfx = GetComponent<ProjectileVfx>();
    }

    private void OnDisable()
    {
        isInitialized = false;
    }

    public void Initialize()
    {
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(state.PreciseTick);
        movement.SetInitialMotion(elapsed);

        if (isInitialized)
            return;

        collision.PostSpawnSetup();

        isInitialized = true;
    }

    public void Kill()
    {
        if (!IsSpawned)
            return;

        if (IsServerInitialized)
            ServerKill();
    }

    private void ServerKill()
    {
        Despawn(DespawnType.Pool);
        SpawnPrefabs();
    }
    
    // does this work correctly with predicted spawn if the prefabs in data.spawnOnDeath have
    // predicted spawn components?
    private void SpawnPrefabs()
    {
        foreach (var prefab in data.spawnOnDeath)
        {
            PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);

            NetworkObject newObj = InstanceFinder.NetworkManager.GetPooledInstantiated(
                prefab,
                Vector3.zero,
                Quaternion.identity,
                false
            );

            var newState = newObj.GetComponent<ProjectileState>();
            var newData = newObj.GetComponent<ProjectileData>();

            newState.PreciseTick    = tick;
            newState.StartDirection = Vector3.up;
            newState.StartPosition  = transform.position;
            newState.ID             = Guid.NewGuid().ToString();
            newState.Firer          = state.Firer;
            newState.Health         = newData.MaxHealth;

            InstanceFinder.ServerManager.Spawn(newObj);
        }
    }
}
