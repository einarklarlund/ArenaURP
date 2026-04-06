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

    private void Awake()
    {
        data = GetComponent<ProjectileData>();
        state = GetComponent<ProjectileState>();
        movement = GetComponent<ProjectileMovement>();
        collision = GetComponent<ProjectileHitbox>();
    }

    public void Initialize()
    {
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(state.PreciseTick);
        movement.SetInitialMotion(elapsed);

        if (collision != null)
            collision.PostSpawnSetup();

        IgnoreOwnerCollision();
    }

    private void IgnoreOwnerCollision()
    {
        var myCollider = GetComponent<Collider>();
        Pawn firer = state.Firer != null ? state.Firer.ControlledPawn.Value : null;
        if (myCollider != null && firer != null)
        {
            foreach (var col in firer.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(myCollider, col);
        }
    }

    public void Kill()
    {
        if (!IsSpawned || state.Health <= 0)
            return;

        state.Health = 0;

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

        Despawn(DespawnType.Pool);
    }
}
