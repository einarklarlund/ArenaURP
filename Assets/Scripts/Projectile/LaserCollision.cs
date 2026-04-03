using UnityEngine;

public class LaserCollision : ProjectileCollision
{
    [Header("Laser Settings")]
    [SerializeField] private float collisionInterval = 0.08f;
    [SerializeField] private float maxRange = 100f;
    [SerializeField] private LineRenderer lineRenderer;

    private float _collisionTimer;

    public override void OnInitialize(ProjectileData projectileData)
    {
        base.OnInitialize(projectileData);

        // Raycast forward from start position to determine laser length
        float laserLength = maxRange;
        if (Physics.Raycast(data.spawnState.StartPosition, data.spawnState.StartDirection,
                            out RaycastHit hit, maxRange, data.hitLayers))
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

    public override bool CheckCollision(Vector3 velocity, float deltaTime)
    {
        if (!data.IsServerInitialized) return false;

        _collisionTimer -= deltaTime;
        if (_collisionTimer > 0f)
            return false;

        _collisionTimer = collisionInterval;

        base.CheckCollision(data.spawnState.StartDirection, deltaTime);
        return false; // Laser never despawns on hit
    }
}
