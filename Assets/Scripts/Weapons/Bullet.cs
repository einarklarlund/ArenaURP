using FishNet;
using FishNet.Object;
using System;
using FishNet.Managing.Timing;
using UnityEngine;
using FishNet.Connection;
using FishNet.Serializing;
using System.Linq;
using System.Collections.Generic;

[Serializable]
public struct BulletData
{
    public string ID;
    public PreciseTick PreciseTick;
    public Vector3 StartDirection;
    public Vector3 StartPosition;
}

public class Bullet : NetworkBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField] private float speed = 50f;
    [SerializeField] private float acceleration = 0f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private LayerMask hitLayers = Physics.AllLayers;

    [Header("Capsule Dimensions")]
    [SerializeField] private float radius = 0.05f;
    [SerializeField] private float height = 0.2f;
    [SerializeField] private int directionAxis = 2;

    public BulletData data;

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteString(data.ID);
        writer.WritePreciseTick(data.PreciseTick);
        writer.WriteVector3(data.StartDirection);
        writer.WriteVector3(data.StartPosition);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        var id = reader.ReadStringAllocated();
        var preciseTick = reader.ReadPreciseTick();
        var startDirection = reader.ReadVector3();
        var startPosition = reader.ReadVector3();
        data = new()
        {
            ID = id,
            PreciseTick = preciseTick,
            StartDirection = startDirection,
            StartPosition = startPosition,
        };
        Initialize(data);
    }

    private void Initialize(BulletData bulletData)
    {
        data = bulletData;
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.PreciseTick);
        transform.position = CalculateKinematicPosition(elapsed);
    }

    private void Update()
    {
        // Calculate time passed since the bullet was logically created
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.PreciseTick);

        // Kinematic Calculation
        Vector3 nextPosition = CalculateKinematicPosition(elapsed);
        Vector3 velocity = (data.StartDirection * speed) + (acceleration * elapsed * data.StartDirection);

        // Server-side hit detection 
        if (IsServerInitialized)
            ServerCheckCollision(velocity, Time.deltaTime);

        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(data.StartDirection));

        // Server-side cleanup
        if (IsServerInitialized && elapsed > lifetime)
            Despawn(DespawnType.Pool);
    }

    public Vector3 CalculateKinematicPosition(float time)
    {
        return data.StartPosition +
               (speed * time * data.StartDirection) +
               (0.5f * acceleration * time * time * data.StartDirection);
    }

    [Server]
    private void ServerCheckCollision(Vector3 velocity, float deltaTime)
    {
        float stepDistance = velocity.magnitude * deltaTime;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);

        var hits = Physics.CapsuleCastAll(p1, p2, radius, velocity.normalized, stepDistance + 0.05f, hitLayers);
        var validHits = new List<RaycastHit>();
        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
            {
                if (!ShouldIgnoreCollision(damageable))
                {
                    ServerHandleHit(hit, damageable);
                    validHits.Add(hit); // a valid damageable was hit
                }
            }
            else
            {
                validHits.Add(hit); // a wall was hit
            }
        };

        if (validHits.Count > 0)
        {
            ObserversHandleHit(validHits.First().point, validHits.First().normal);
            Despawn(DespawnType.Pool);
        }
    }

    private bool ShouldIgnoreCollision(IDamageable damageable) =>
        damageable is Pawn pawn && pawn.Owner == Owner;

    [Server]
    private void ServerHandleHit(RaycastHit hit, IDamageable damageable)
    {
        transform.position = hit.point;

        if (damageable != null)
        {
            DamageInfo info = new()
            {
                Amount = damage,
                Attacker = Owner,
                HitPoint = hit.point,
                Direction = transform.forward,
                Type = DamageType.Bullet
            };
            damageable.ServerTakeDamage(info);
        }
    }

    [ObserversRpc]
    private void ObserversHandleHit(Vector3 point, Vector3 normal)
    {
        PlayHitEffects(point, normal);
    }

    private void PlayHitEffects(Vector3 point, Vector3 normal)
    {
        if(IsServerOnlyInitialized) return; // don't play hit effects on non-host server
    }

    private void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
    {
        Vector3 offset = directionAxis switch { 0 => transform.right, 1 => transform.up, _ => transform.forward };
        float halfHeight = height / 2f;
        p1 = center + offset * halfHeight;
        p2 = center - offset * halfHeight;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);
        Gizmos.DrawWireSphere(p1, radius);
        Gizmos.DrawWireSphere(p2, radius);
        Gizmos.DrawLine(p1, p2);
    }
}