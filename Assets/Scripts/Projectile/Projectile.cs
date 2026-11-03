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

    [Header("Explosion")]
    [Tooltip("Explodes on surface contact when a VFX prefab is set. Empty = no explosion.")]
    [SerializeField] private ExplosionSettings explosion = new();

    [Header("Lifetime")]
    public float lifetime = 0.5f;

    [Header("Upgrades")]
    [Tooltip("Written by the shooter's upgrades when the projectile comes alive; reset when pooled.")]
    public int damageBonus;
    public float speedMultiplier = 1f;
    [Tooltip("Seconds of haste the shooter's held weapon gets when this hits another pawn. 0 = off.")]
    public float hasteOnHitDuration;

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
    // Public accessor for _isAlive so that other projectiles can check it before running
    // collision logic.
    public bool IsAlive => _isAlive;


    // Spawn payload
    // Until we get a better way of spawning projectiles (ie. one that only sends a server RPC
    // from PawnWeaponController to spawn each projectile) we'll inject it here.
    public Pawn owningPawn { get; set; }

    // Tracking whether the bullet is "alive" or not allows us to disable client-side collision 
    // detection after it's triggered once.
    private bool _isAlive = true;
    private Vector3 _previousCollisionCastPosition;
    private Vector3 _spawnPosition;
    private uint _instantiationTick;
    // Projectile-to-projectile collisions lower damage at runtime; pooled instances restore this.
    private int _baseDamage;


    // ------------------------------ OBJECT CREATION -----------------------------------


    private void Awake()
    {
        _baseDamage = damage;
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        _spawnPosition = transform.position;
        _previousCollisionCastPosition = _spawnPosition;
        _instantiationTick = TimeManager.Tick;

        // Pulled, on every peer, so all of them move it at the same speed. owningPawn is set before the predicted
        // spawn on the shooter, and arrives in the payload everywhere else.
        if (owningPawn != null && owningPawn.ControllingPlayer.Value != null)
            owningPawn.ControllingPlayer.Value.ApplyUpgradesTo(this);
        damage = _baseDamage + damageBonus;
    }

    public override void ReadPayload(NetworkConnection connection, Reader reader)
    {
        base.ReadPayload(connection, reader);
        owningPawn = (Pawn) reader.ReadNetworkBehaviour();
        spawnTick = reader.ReadPreciseTick();
    }

    public override void WritePayload(NetworkConnection connection, Writer writer)
    {
        base.WritePayload(connection, writer);
        writer.Write(owningPawn);
        writer.Write(spawnTick);
    }


    // ------------------------------ OBJECT REMOVAL ------------------------------

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);

        // Reset stateful values to support object pooling.
        meshRenderer.enabled = true;
        _isAlive = true;
        damage = _baseDamage;
        damageBonus = 0;
        speedMultiplier = 1f;
        hasteOnHitDuration = 0f;
        visualPredictionPoint = null;
    }


    // ------------------------------ TICK ------------------------------


    private void Update()
    {
        // Update may run before network lifecycle events -- guard against it specifically
        if (!IsSpawned || !_isAlive)
            return;

        Move();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !_isAlive)
            return;

        var collisions = GatherCollisionsBetween(_previousCollisionCastPosition, GetCurrentPosition());
        HandleCollisions(collisions);

        // A collision this tick may already have killed the projectile.
        if (!_isAlive)
            return;

        if(TimeManager.TimePassed(spawnTick) >= lifetime)
        {
            Die(playDamageVfx: false);
            PlaySmokeVfx();
        }
    }

    // ------------------------------ MOVEMENT/POSITION ------------------------------


    private void Move()
    {
        transform.position = GetCurrentPosition();

        if (!IsClientInitialized)
            return;

        if (IsOwner)
        {
            if (visualPredictionPoint == null)
                return;

            // Ensure that the projectile's mesh always appears exactly at the muzzle of
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
        else
        {
            // Lerp the projectile's mesh from it's spawning position to it's actual
            // position for 1 tick after spawning
            // if (TimeManager.Tick - _instantiationTick <= visualPredictionTicks)
            // {
            //     float nextTickAsElapsedTime = (float) TimeManager.TimePassed(_instantiationTick + 1);
            //     Vector3 velocity = speed * transform.forward;
            //     Vector3 positionNextTick = _spawnPosition + nextTickAsElapsedTime * velocity;
            //     double spawnTickAsTime = TimeManager.TicksToTime(spawnTick);
            //     double currentTickAsTime = TimeManager.TicksToTime(TimeManager.GetPreciseTick(TickType.Tick));
            //     double lerpDuration = 1 - spawnTick.PercentAsDouble;
            //     float t = (float) ((currentTickAsTime - spawnTickAsTime) / lerpDuration);
            //     meshRenderer.transform.position = Vector3.Lerp(_spawnPosition, positionNextTick, t);
            // }
        }
    }

    private Vector3 GetCurrentPosition()
    {
        Vector3 velocity = speed * speedMultiplier * transform.forward;
        float elapsedTime = (float) TimeManager.TimePassed(spawnTick);
        return _spawnPosition + elapsedTime * velocity;
    }


    // ------------------------------ COLLISION ------------------------------


    private RaycastHit[] GatherCollisionsBetween(Vector3 from, Vector3 to)
    {
        var sweep = to - from;
        var hits = Physics.SphereCastAll(
            from,
            collider.radius * transform.localScale.x, // assuming tranform.x == tranform.y == tranform.z
            sweep,
            sweep.magnitude,
            includeLayers,
            QueryTriggerInteraction.Collide
        );

        _previousCollisionCastPosition = GetCurrentPosition();
        return hits;
    }

    private void HandleCollisions(RaycastHit[] hits)
    {
        // SphereCastAll returns hits in no particular order. Damage can drop mid-loop, so resolve
        // them nearest-first.
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // Stop at the first hit that kills it. A late-spawned copy (the server's copy of a client's shot)
        // first sweeps its whole path so far, and would otherwise hit what's behind the wall that stopped it.
        foreach (var hit in hits)
        {
            if (!_isAlive)
                break;
            HandleCollision(hit);
        }
    }

    private void HandleCollision(RaycastHit hit)
    {
        if (hit.collider == null || hit.collider == collider)
            return;

        // Handle collisions against pawns and projectiles.
        if (hit.collider.TryGetComponent(out Projectile otherProjectile))
        {
            if (owningPawn == otherProjectile.owningPawn || !otherProjectile.IsAlive)
                return;

            // When 2 projectiles collide, whichever one calls HandleCollision first
            // deals the damage to the other projectile, then takes damage from the
            // other projectile.
            int thisDamage = damage;
            int otherDamage = otherProjectile.damage;
            otherProjectile.TakeProjectileDamage(thisDamage);
            TakeProjectileDamage(otherDamage);
            return;
        }
        else if (hit.collider.TryGetComponent(out Pawn pawn))
        {
            if (pawn == owningPawn)
                return;

            pawn.TakeDamage(new DamageInfo()
            {
                Amount = damage,
                // The kill (score and death drop) goes to whoever fired.
                Attacker = owningPawn != null ? owningPawn.ControllingPlayer.Value : null,
                HitPoint = hit.point,
                Direction = transform.forward
            });

            // Every copy does this, but only the shooter's weapon fires, so the haste matters there alone. (Gating on
            // IsOwner misses close hits on a host, where the client side starts a tick after the spawn.)
            if (hasteOnHitDuration > 0f && owningPawn != null && owningPawn.PrimaryWeapon != null)
                owningPawn.PrimaryWeapon.ApplyHaste(hasteOnHitDuration);
        }
        else if (hit.collider.TryGetComponent<ExperienceCapsule>(out var capsule))
        {
            capsule.TakeDamage(new DamageInfo()
            {
                Amount = damage,
                Attacker = owningPawn != null ? owningPawn.ControllingPlayer.Value : null,
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
            point = hit.collider.ClosestPointOnBounds(_previousCollisionCastPosition);

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


    private void TakeProjectileDamage(int otherDamage)
    {
        // When 2 projectiles with the same damage value collide, they both die.
        // When 2 projectiles with different damage values collide, the weaker one dies,
        // and the stronger one continues to live with its the weaker's damage subtracted
        // from its own.
        if (otherDamage >= damage)
        {
            Die(playDamageVfx: true);
        }
        else
        {
            damage -= otherDamage;
        }
    }

    private void TakeCollisionDamage(Vector3 hitPoint, Vector3 hitNormal)
    {
        Die(playDamageVfx: true);
    }

    /// <summary>
    /// Kills the projectile: plays VFX, resolves its explosion, and despawns it on the server.
    /// </summary>
    /// <param name="playDamageVfx">False for deaths that aren't hits, such as running out of lifetime.</param>
    private void Die(bool playDamageVfx)
    {
        // HandleCollisions can kill the projectile several times in a single FixedUpdate. Only the
        // first call does anything. IsSpawned covers the server, where a pooled despawn may reset
        // _isAlive before the next hit is handled.
        if (!_isAlive || !IsSpawned)
            return;

        _isAlive = false;

        if (playDamageVfx)
            PlayDamageVfx();

        Explosion.Resolve(explosion, transform.position, Vector3.up, owningPawn, playVfx: IsClientStarted, bonusDamage: damageBonus);

        if (IsServerStarted)
            Despawn(DespawnType.Pool);
    }


    // ------------------------------ VFX ------------------------------


    private void PlayDamageVfx()
    {
        // Only run VFX client-side
        if (IsServerStarted && !IsClientStarted)
            return;

        meshRenderer.enabled = false;
        Quaternion rotation = Quaternion.LookRotation(Vector3.up);
        Instantiate(damageEffects, transform.position, rotation);
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