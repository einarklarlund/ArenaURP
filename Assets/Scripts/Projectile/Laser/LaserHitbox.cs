using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LaserHitbox : ProjectileHitbox
{
    [Header("Laser Settings")]
    [SerializeField] private float collisionInterval = 0.08f;
    [SerializeField] private float maxRange = 100f;
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private LayerMask raycastLayers = Physics.AllLayers;

    public override bool UsesManualCollisionCheck => true;

    private float _collisionTimer;
    private ProjectileState _state;

    protected override void Awake()
    {
        base.Awake();
        _state = GetComponent<ProjectileState>();
    }

    public override void PostSpawnSetup()
    {
        // Raycast forward from start position to determine laser length
        float laserLength = maxRange;
        if (Physics.Raycast(transform.position, transform.forward,
                            out RaycastHit hit, maxRange, raycastLayers))
        {
            laserLength = hit.distance;
        }

        data.height = laserLength;
        _collisionTimer = 0f;

        // Update the line renderer to match the beam length (local space)
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, Vector3.forward * data.height);
        }
    }

    public override void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
    {
        p1 = center;
        p2 = center + transform.forward * data.height;
    }

    public override void CheckCollision(Vector3 velocity, float deltaTime)
    {
        if (!data.IsServerInitialized) return;

        _collisionTimer -= deltaTime;
        if (_collisionTimer > 0f)
            return;

        _collisionTimer = collisionInterval;

        float stepDistance = velocity.magnitude * deltaTime;
        var vel = velocity == Vector3.zero ? Vector3.forward : velocity.normalized;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);

        var hits = Physics.CapsuleCastAll(p1, p2, data.radius, vel, stepDistance + 0.05f, data.hitLayers);
        HandleHits(hits, velocity);
    }


    private void HandleHits(RaycastHit[] hits, Vector3 velocity)
    {
        List<HitInfo> surfaceHits = new();
        foreach (var hit in hits)
        {
            var hitInfo = HitInfo.FromRaycast(hit);

            if (hit.collider.TryGetComponent<ProjectileData>(out var otherBullet))
            {
                if (otherBullet != data && otherBullet.deflectOtherBullets)
                {
                    var otherState = otherBullet.GetComponent<ProjectileState>();
                    projectileBulletImpact.HandleImpact(otherState, hitInfo);
                }
                continue;
            }

            if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
            {
                projectileDamageableImpact.HandleImpact(hitInfo, damageable);
            }
            else
            {
                surfaceHits.Add(hitInfo);
            }
        }
        if (surfaceHits.Count > 0)
        {
            projectileSurfaceImpact.HandleImpact(new HitInfo
            {
                point = surfaceHits.Select(h => h.point).Aggregate(Vector3.zero, (acc, v) => acc + v) / surfaceHits.Count,
                normal = surfaceHits.Select(h => h.normal).Aggregate(Vector3.zero, (acc, v) => acc + v) / surfaceHits.Count,
                collider = surfaceHits[0].collider,
                transform = surfaceHits[0].transform
            });
        }
    }
}
