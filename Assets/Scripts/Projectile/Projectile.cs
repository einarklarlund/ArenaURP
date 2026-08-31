using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Managing.Timing;

/// <summary>
/// All logic + data for projectiles, in one class.
/// Includes logic + data for motion, scale, collision, and player ownership.
/// 
/// Collision operations can probably be significantly improved for performance.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Projectile : NetworkBehaviour
{
    [Header("Motion")]
    public float speed = 50f;

    [Header("Collision")]
    public LayerMask includeLayers;
    public SphereCollider collider;

    [Header("Damage")]
    public int damage = 1;

    [Header("Lifetime")]
    public float lifetime = 0.5f;

    [Header("Visuals")]
    public MeshRenderer meshRenderer;
    public GameObject damageEffects;
    public GameObject smokeEffects;
    public Rigidbody ragdoll;
    public int visualPredictionTicks = 1;

    // When a projectile is spawned, it will stay at this point for a network tick.
    // Helps with visual artifacts caused by kinematic position calculations.
    public Transform visualPredictionPoint { get; set; }
    // Collision and movement logic is calculated kinematically, based on the PreciseTick and
    // location at which the projectile was spawned.
    public PreciseTick spawnTick { get; set; }


    // Spawn payload
    // Until we get a better way of spawning projectiles (ie. one that only sends a server RPC
    // from PawnWeaponController to spawn each projectile) we'll inject it here.
    public Pawn owningPawn { get; set; }

    // Tracking whether the bullet is "alive" or not allows us to disable client-side collision 
    // detection after it's triggered once.
    private bool _isAlive = true;
    private Vector3 _previousTickPosition;
    private Vector3 _spawnPosition;


    // ------------------------------ OBJECT CREATION -----------------------------------


    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _spawnPosition = transform.position;
        _previousTickPosition = _spawnPosition;
        spawnTick = TimeManager.GetPreciseTick(TickType.Tick);
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        owningPawn = (Pawn) reader.ReadNetworkBehaviour();
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.Write(owningPawn);
    }


    // ------------------------------ OBJECT REMOVAL ------------------------------


    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);

        // Reset stateful values to support object pooling.
        meshRenderer.enabled = true;
        _isAlive = true;
        visualPredictionPoint = null;
    }


    // ------------------------------ TICK ------------------------------


    private void Update()
    {
        // Update may run before network lifecycle events -- guard against it specifically
        if (!IsSpawned || !_isAlive)
            return;

        // Given that we can calculate the trajectory of projectiles kinematically, we can
        // do movement and collision detection in Update rather than on network tick.
        Move();
        var collisions = GatherCollisionsBetween(_previousTickPosition, GetCurrentPosition());
        HandleCollisions(collisions);

        if(TimeManager.TimePassed(spawnTick) >= lifetime)
        {
            Die();
            PlaySmokeVfx();
        }

        _previousTickPosition = GetCurrentPosition();
    }


    // ------------------------------ MOVEMENT/POSITION ------------------------------

    
    private void Move()
    {
        transform.position = GetCurrentPosition();

        if (visualPredictionPoint == null)
            return;

        // Ensure that the projectile's mesh always appears visually at the muzzle of
        // the gun when it is predicted spawned.
        if (TimeManager.Tick - spawnTick.Tick <= visualPredictionTicks)
        {
            meshRenderer.transform.position = visualPredictionPoint.position;
        }
        else
        {
            meshRenderer.transform.localPosition = Vector3.zero;
        }
    }

    private Vector3 GetCurrentPosition()
    {
        Vector3 velocity = speed * transform.forward;
        double currentTime = TimeManager.TicksToTime(TickType.Tick);
        double spawnTime = TimeManager.TicksToTime(spawnTick);
        float elapsedTime = (float) (currentTime - spawnTime);
        return _spawnPosition + elapsedTime * velocity;
    }


    // ------------------------------ COLLISION ------------------------------


    private RaycastHit[] GatherCollisionsBetween(Vector3 from, Vector3 to)
    {
        var sweep = to - from;
        var hits = Physics.SphereCastAll(
            from,
            collider.radius,
            sweep,
            sweep.magnitude,
            includeLayers,
            QueryTriggerInteraction.Collide
        );

        _previousTickPosition = GetCurrentPosition();
        return hits;
    }

    private void HandleCollisions(RaycastHit[] hits)
    {
        foreach (var hit in hits)
            HandleCollision(hit);
    }

    private void HandleCollision(RaycastHit hit)
    {
        if (hit.collider == null || hit.collider == collider)
            return;

        // Handle collisions against pawns and projectiles.
        if (hit.collider.TryGetComponent(out Projectile otherProjectile))
        {
            if (owningPawn == otherProjectile.owningPawn)
                return;

            // When 2 projectiles collide, whichever one calls HandleCollision first
            // deals the damage.
            otherProjectile.TakeCollisionDamage(hit.point, -1 * hit.normal);
        }
        else if (hit.collider.TryGetComponent(out Pawn pawn))
        {
            if (pawn == owningPawn)
                return;

            pawn.TakeDamage(new DamageInfo()
            {
                Amount = damage,
                Attacker = null,
                HitPoint = hit.point,
                Direction = transform.forward
            });
        }

        Vector3 point = hit.point;
        Vector3 normal = hit.normal;
        if (point == Vector3.zero)
        {
            // Estimate the hit point and normal. Helps in case the projectile is instantiated
            // inside a collider (in which case point == Vector3.zero).
            point = hit.collider.ClosestPointOnBounds(_previousTickPosition);

            // Raycast from _spawnPosition to point to get the normal.
            Vector3 spawnPosToHitPoint = point - _spawnPosition;
            if (hit.collider.Raycast(
                new Ray(_spawnPosition, spawnPosToHitPoint),
                out var normalHit,
                _spawnPosition.magnitude + 0.1f
            ))
                normal = normalHit.normal;
            else
                normal = -1 * transform.forward;
        }

        TakeCollisionDamage(point, normal);
    }


    // ------------------------------ DAMAGE HANDLING ------------------------------


    private void TakeCollisionDamage(Vector3 hitPoint, Vector3 hitNormal)
    {
        Die();
        PlayDamageVfx(hitPoint, hitNormal);
    }

    private void Die()
    {
        if (!_isAlive)
            return;

        if (IsServerStarted && IsSpawned)
            Despawn(DespawnType.Pool);

        _isAlive = false;
    }


    // ------------------------------ VFX ------------------------------


    private void PlayDamageVfx(Vector3 hitPoint, Vector3 hitNormal)
    {
        // Only run VFX client-side
        if (IsServerStarted && !IsClientStarted)
            return;

        meshRenderer.enabled = false;
        Quaternion rotation = Quaternion.LookRotation(hitNormal);
        Instantiate(damageEffects, hitPoint, rotation);
    }

    private void PlaySmokeVfx()
    {
        if (IsServerStarted && !IsClientStarted)
            return;

        Quaternion rotation = Quaternion.LookRotation(Vector3.up);
        Instantiate(smokeEffects, GetCurrentPosition(), rotation);
    }

    private void PlayRagdollVfx(Vector3 spawnPoint, Vector3 launchVelocity)
    {
        if (IsServerStarted && !IsClientStarted)
            return;

        Rigidbody rd = Instantiate(ragdoll, spawnPoint, transform.rotation);
        rd.AddForce(launchVelocity, ForceMode.VelocityChange);
    }
}