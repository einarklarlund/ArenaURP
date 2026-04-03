using FishNet;
using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(ProjectileMovement))]
[RequireComponent(typeof(ProjectileCollision))]
public class Projectile : MonoBehaviour
{
    private ProjectileData data;
    private ProjectileMovement movement;
    private ProjectileCollision collision;
    private ProjectileDeflection deflection;

    private void Awake()
    {
        data = GetComponent<ProjectileData>();
        movement = GetComponent<ProjectileMovement>();
        collision = GetComponent<ProjectileCollision>();
        deflection = GetComponent<ProjectileDeflection>();
    }

    public void Initialize()
    {
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.spawnState.PreciseTick);

        movement.OnInitialize(data, elapsed);
        collision.OnInitialize(data);
        deflection.OnInitialize(data);

        var hitEffects = GetComponent<ProjectileHitEffects>();
        if (hitEffects != null)
            hitEffects.OnInitialize(data);

        transform.position = movement.CalculatePosition(elapsed);
    }

    private void Update()
    {
        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(data.spawnState.PreciseTick);

        // Movement
        Vector3 nextPosition = movement.CalculatePosition(elapsed);
        Vector3 velocity = (data.spawnState.StartDirection * data.speed)
                         + (data.acceleration * elapsed * data.spawnState.StartDirection);

        // Collision
        bool hasCollided = collision.CheckCollision(velocity, Time.deltaTime);

        // Apply transform
        transform.SetPositionAndRotation(nextPosition, Quaternion.LookRotation(data.spawnState.StartDirection));

        // Server despawn
        if (data.IsServerInitialized && (elapsed > data.lifetime || hasCollided))
            data.Despawn(DespawnType.Pool);
    }
}
