using FishNet;
using FishNet.Object;
using System;
using FishNet.Managing.Timing;
using UnityEngine;
using FishNet.Connection;
using FishNet.Serializing;

[Serializable]
public struct BulletData
{
    public string ID;
    public PreciseTick PreciseTick;
    public Vector3 StartDirection;
    public Vector3 StartPosition;

    /// <summary>
    /// The NetworkPlayer who fired this bullet (the controlling player of the
    /// firing pawn). Null for environment damage. Serialized via the FishNet
    /// NetworkBehaviour payload pattern so clients receive the reference on spawn.
    /// </summary>
    public NetworkPlayer Firer;
}

// TODO - componentize these bullets
public class Bullet : NetworkBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField] protected float speed = 50f;
    [SerializeField] protected float acceleration = 0f;
    [SerializeField] protected int damage = 10;
    [SerializeField] protected float lifetime = 5f;
    [SerializeField] protected LayerMask hitLayers = Physics.AllLayers;
    [SerializeField] protected bool deflectOtherBullets = false;

    [Header("Capsule Dimensions")]
    [SerializeField] protected float radius = 0.05f;
    [SerializeField] protected float height = 0.2f;
    [SerializeField] protected int directionAxis = 2;

    public BulletData data;

    public float Lifetime => lifetime;
    
    private CapsuleCollider deflectionCollider;

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.WriteString(data.ID);
        writer.WritePreciseTick(data.PreciseTick);
        writer.WriteVector3(data.StartDirection);
        writer.WriteVector3(data.StartPosition);
        writer.WriteNetworkBehaviour(data.Firer);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        var id             = reader.ReadStringAllocated();
        var preciseTick    = reader.ReadPreciseTick();
        var startDirection = reader.ReadVector3();
        var startPosition  = reader.ReadVector3();
        var firer          = reader.ReadNetworkBehaviour() as NetworkPlayer;
        data = new()
        {
            ID             = id,
            PreciseTick    = preciseTick,
            StartDirection = startDirection,
            StartPosition  = startPosition,
            Firer          = firer,
        };
        Initialize(data);
    }

    protected virtual void Initialize(BulletData bulletData)
    {
        data = bulletData;
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.PreciseTick);
        transform.position = CalculateKinematicPosition(elapsed);

        if (deflectOtherBullets)
            AddDeflectionCollider();
    }

    private void AddDeflectionCollider()
    {
        if (deflectionCollider != null)
            return;

        deflectionCollider = gameObject.AddComponent<CapsuleCollider>();
        deflectionCollider.radius = radius;
        deflectionCollider.height = height;
        deflectionCollider.direction = directionAxis;
        deflectionCollider.isTrigger = true;
    }

    protected virtual void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
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

    private void Update()
    {
        // Calculate time passed since the bullet was logically created
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.PreciseTick);

        // Kinematic Calculation
        Vector3 nextPosition = CalculateKinematicPosition(elapsed);
        Vector3 velocity = (data.StartDirection * speed) + (acceleration * elapsed * data.StartDirection);

        // Hit detection 
        bool hasCollided = CheckCollision(velocity, Time.deltaTime);

        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(data.StartDirection));

        // Server-side cleanup
        if (IsServerInitialized && (elapsed > lifetime || hasCollided))
            Despawn(DespawnType.Pool);
    }

    protected virtual Vector3 CalculateKinematicPosition(float time)
    {
        return data.StartPosition +
               (speed * time * data.StartDirection) +
               (0.5f * acceleration * time * time * data.StartDirection);
    }

    /// <summary>
    /// Check for collisions and handle them. 
    /// Returns true if the collision should destory the bullet.
    /// </summary>
    /// <param name="velocity">The velocity of the bullet on this frame.</param>
    /// <param name="deltaTime">The delta time between this frame and the last.</param>
    /// <returns></returns>
    protected virtual bool CheckCollision(Vector3 velocity, float deltaTime)
    {
        float stepDistance = velocity.magnitude * deltaTime;
        var vel = velocity == Vector3.zero ? Vector3.forward : velocity.normalized;
        bool hasCollided = false;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);

        var hits = Physics.CapsuleCastAll(p1, p2, radius, vel, stepDistance + 0.05f, hitLayers);
        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent<Bullet>(out var otherBullet))
            {
                // other bullets don't count as collisions
                if (otherBullet != this && otherBullet.deflectOtherBullets)
                    Deflect(otherBullet, hit);
                continue;
            }

            if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
            {
                // don't hit the owner of the bullet
                if (!(damageable is Pawn pawn && pawn.ControllingPlayer.Value == data.Firer))
                {
                    HandleHitDamageable(hit, damageable);
                    hasCollided = true;
                }
            }
            else
            {
                hasCollided = true;
            }
        };

        return hasCollided;
    }

    protected virtual void Deflect(Bullet otherBullet, RaycastHit hit)
    {
        // client-authoritative for now
        if (!IsOwner) return;

        PreciseTick tick = TimeManager.GetPreciseTick(TimeManager.Tick);
        Vector3 reflectedDirection = otherBullet.data.StartDirection;
        // move capsule so that it doesn't overlap with the hit point
        Vector3 startPosition = hit.point;
        startPosition += (height / 2 + 0.01f) * reflectedDirection;

        // todo - use object pooling
        var newBullet = Instantiate(this, startPosition, Quaternion.LookRotation(reflectedDirection));

        newBullet.data = new()
        {
            PreciseTick = tick,
            StartDirection = reflectedDirection,
            StartPosition = startPosition,
            ID = Guid.NewGuid().ToString()
        };
        newBullet.data.Firer = otherBullet.data.Firer;

        Spawn(newBullet, otherBullet.Owner);

        if (IsSpawned)
            Despawn(this);
    }

    /// <summary>
    /// Hit behaviour that runs when a damageable has been hit.
    /// </summary>
    private void HandleHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        if(IsServerInitialized)
        {
            ServerHitDamageable(hit, damageable);
        }
        else
        {
            ClientHitDamageable(hit, damageable);
        }
    }

    [Server]
    private void ServerHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        transform.position = hit.point;

        if (damageable != null)
        {
            DamageInfo info = new()
            {
                Amount    = damage,
                Attacker  = data.Firer,
                HitPoint  = hit.point,
                Direction = transform.forward,
                Type      = DamageType.Bullet
            };
            damageable.ServerTakeDamage(info);
        }
    }

    [Client]
    private void ClientHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        PlayHitEffects(hit.point, hit.normal);
    }

    private void PlayHitEffects(Vector3 point, Vector3 normal)
    {
        if(IsServerOnlyInitialized) return;
    }
}
