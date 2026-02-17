using FishNet;
using FishNet.Managing.Timing;
using UnityEngine;


public class TestBullet : MonoBehaviour
{
    [Header("Projectile Settings")]
    [SerializeField] private float speed = 100f;
    [SerializeField] private float lifetime = 5f;
    [SerializeField] private LayerMask hitLayers = Physics.AllLayers;

    [Header("Capsule Dimensions")]
    [SerializeField] private float radius = 0.05f;
    [SerializeField] private float height = 0.2f;
    // 0 = X, 1 = Y, 2 = Z. Usually you want Z (Forward) for a bullet.
    [SerializeField] private int directionAxis = 2;

    private float timeSpawned;
    private Vector3 startPosition;
    private Vector3 startDirection;
    private PreciseTick startTick;

    private void Start()
    {
        timeSpawned = Time.time;
        startPosition = transform.position;
        startDirection = transform.forward;
        startTick = InstanceFinder.TimeManager.GetPreciseTick(TickType.Tick);
    }

    private void Update()
    {
        if(Time.time - timeSpawned >= lifetime)
            Destroy(gameObject);
        
        MoveAndCheckCollision();
    }

    private void MoveAndCheckCollision()
    {   
        // GetCapsulePoints(transform.position, out Vector3 point1, out Vector3 point2);

        // if (Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit, stepDistance + 0.1f, hitLayers))
        // {
        //     transform.position = hit.point;
        //     if (HandleHit(hit)) return; // don't move if a hit was detected
        // }

        transform.position = CalculateKinematicPosition((float) InstanceFinder.TimeManager.TimePassed(startTick));
    }

    public Vector3 CalculateKinematicPosition(float time)
    {
        return startPosition +
               (speed * time * startDirection) +
               (0.5f * 1 * time * time * startDirection);
    }

    private void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
    {
        // Determine the offset direction based on the chosen axis
        Vector3 offset;
        if (directionAxis == 0) offset = transform.right;
        else if (directionAxis == 1) offset = transform.up;
        else offset = transform.forward;

        float halfHeight = height / 2f;

        // P1 and P2 are the centers of the two spheres forming the capsule
        p1 = center + offset * halfHeight;
        p2 = center - offset * halfHeight;
    }

    private bool HandleHit(RaycastHit hit)
    {
        // Despawn back to the FishNet pool
        Destroy(gameObject);
        return true;
    }

    // Useful for debugging the "size" of your bullet in the Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);
        Gizmos.DrawWireSphere(p1, radius);
        Gizmos.DrawWireSphere(p2, radius);
        Gizmos.DrawLine(p1, p2);
    }
}