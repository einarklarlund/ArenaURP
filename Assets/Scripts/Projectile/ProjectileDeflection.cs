using System;
using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

public class ProjectileDeflection : MonoBehaviour
{
    protected ProjectileData data;

    private CapsuleCollider deflectionCollider;

    public void OnInitialize(ProjectileData projectileData)
    {
        data = projectileData;

        if (data.deflectOtherBullets)
            AddDeflectionCollider();
    }

    private void AddDeflectionCollider()
    {
        if (deflectionCollider != null)
            return;

        deflectionCollider = gameObject.AddComponent<CapsuleCollider>();
        deflectionCollider.radius = data.radius;
        deflectionCollider.height = data.height;
        deflectionCollider.direction = data.directionAxis;
        deflectionCollider.isTrigger = true;
    }

    public virtual void Deflect(ProjectileData otherBullet, RaycastHit hit)
    {
        if (!data.IsOwner) return;

        PreciseTick tick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
        Vector3 reflectedDirection = otherBullet.spawnState.StartDirection;
        Vector3 startPosition = hit.point;
        startPosition += (data.height / 2 + 0.01f) * reflectedDirection;

        // todo - use object pooling
        var newObj = Instantiate(data.gameObject, startPosition, Quaternion.LookRotation(reflectedDirection));
        var newData = newObj.GetComponent<ProjectileData>();

        newData.spawnState = new BulletSpawnState
        {
            PreciseTick = tick,
            StartDirection = reflectedDirection,
            StartPosition = startPosition,
            ID = Guid.NewGuid().ToString(),
            Firer = otherBullet.spawnState.Firer,
        };

        InstanceFinder.ServerManager.Spawn(newObj.GetComponent<NetworkObject>(), otherBullet.Owner);

        if (data.NetworkObject.IsSpawned)
            InstanceFinder.ServerManager.Despawn(data.NetworkObject);
    }
}
