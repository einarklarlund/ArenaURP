using FishNet.Object;
using UnityEngine;

public class Laser : Bullet
{
    [Header("Laser Settings")]
    [SerializeField] private float collisionInterval = 0.04f;
    [SerializeField] private float maxRange = 100f;
    [SerializeField] private LineRenderer lineRenderer;

    private float _collisionTimer;

    protected override void Initialize(BulletData bulletData)
    {
        base.Initialize(bulletData);

        // Raycast forward from start position to determine laser length
        float laserLength = maxRange;
        if (Physics.Raycast(data.StartPosition, data.StartDirection, out RaycastHit hit, maxRange, hitLayers))
        {
            laserLength = hit.distance;
        }

        height = laserLength;
        _collisionTimer = 0f;

        // Update the line renderer to match the beam length (local space)
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, Vector3.forward * height);
        }
    }

    /// <summary>
    /// Laser is stationary — always returns the fire point.
    /// </summary>
    protected override Vector3 CalculateKinematicPosition(float time)
    {
        return data.StartPosition;
    }

    /// <summary>
    /// Override capsule points so the capsule extends forward from the fire point
    /// rather than being centered symmetrically around it.
    /// </summary>
    protected override void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
    {
        p1 = center;
        p2 = center + transform.forward * height;
    }

    /// <summary>
    /// Called every frame by Update. Gates the actual overlap check behind
    /// a timer so collisions are only evaluated at the configured interval.
    /// Damages every tick and does NOT despawn on hit.
    /// </summary>
    [Server]
    protected override bool CheckCollision(Vector3 velocity, float deltaTime)
    {
        _collisionTimer -= deltaTime;
        if (_collisionTimer > 0f)
            return false;

        _collisionTimer = collisionInterval;
        
        base.CheckCollision(data.StartDirection, deltaTime);
        return false; // No despawn — laser persists for its full lifetime
    }
}
