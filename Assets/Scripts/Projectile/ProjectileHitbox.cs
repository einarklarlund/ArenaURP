using FishNet.Component.Prediction;
using FishNet.Object;
using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Exposes a method to check for collisions and call the correct behaviour
/// depending on what was hit (IDamageable, surface or deflection).
/// Uses FishNet's NetworkCollision for prediction-safe collision callbacks.
/// </summary>
public class ProjectileHitbox : NetworkBehaviour
{
    protected ProjectileData data;
    protected ProjectileDamageableImpact projectileDamageableImpact;
    protected ProjectileBulletImpact projectileBulletImpact;
    protected ProjectileSurfaceImpact projectileSurfaceImpact;
    protected PriorityQueue<Action> hitQueue;

    private NetworkCollision _networkCollision;

    public virtual bool UsesManualCollisionCheck => false;

    protected virtual void Awake()
    {
        data = GetComponent<ProjectileData>();
        projectileDamageableImpact = GetComponent<ProjectileDamageableImpact>();
        projectileBulletImpact = GetComponent<ProjectileBulletImpact>();
        projectileSurfaceImpact = GetComponent<ProjectileSurfaceImpact>();
        _networkCollision = GetComponent<NetworkCollision>();
    }

    private void OnEnable()
    {
        hitQueue = new();
        if (_networkCollision != null)
            _networkCollision.OnEnter += OnNetworkCollisionEnter;
    }

    private void OnDisable()
    {
        if (_networkCollision != null)
            _networkCollision.OnEnter -= OnNetworkCollisionEnter;
    }

    /// <summary>
    /// Called after movement has set the initial position. Override for
    /// post-spawn setup that depends on the projectile being positioned
    /// (e.g. laser raycast).
    /// </summary>
    public virtual void PostSpawnSetup() { }

    public virtual void CheckCollision(Vector3 velocity, float deltaTime)
    {
        // Base implementation is empty — collision is handled by NetworkCollision.
        // LaserHitbox overrides this with CapsuleCastAll.
    }

    private void OnNetworkCollisionEnter(Collider other, uint tick)
    {
        EnqueueHit(HitInfo.FromCollider(other, transform));
    }

    protected void EnqueueHit(HitInfo hit)
    {
        if (hit.point == Vector3.zero)
        {
            hit.normal = hit.transform.position - transform.position;
            hit.point = hit.collider.ClosestPoint(transform.position);
        }

        if (hit.collider.TryGetComponent<ProjectileData>(out var otherBullet))
        {
            if (otherBullet != data && otherBullet.deflectOtherBullets)
            {
                hitQueue.Enqueue(() =>
                {
                    var otherState = otherBullet.GetComponent<ProjectileState>();
                    projectileBulletImpact.HandleImpact(otherState, hit);
                }, 1);
            }
        }
        else if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
        {
            hitQueue.Enqueue(() =>
            {
                projectileDamageableImpact.HandleImpact(hit, damageable);
            }, 2);
        }
        else
        {
            hitQueue.Enqueue(() =>
            {
                projectileSurfaceImpact.HandleImpact(hit);
            }, 3);
        }
    }

    protected void ProcessHitQueue()
    {
        while (hitQueue.Count > 0)
        {
            var action = hitQueue.Dequeue();
            action();
        }
    }

    private void LateUpdate()
    {
        ProcessHitQueue();
    }
}
