using UnityEngine;

public struct HitInfo
{
    public Vector3 point;
    public Vector3 normal;
    public Collider collider;
    public Transform transform;

    public static HitInfo FromRaycast(RaycastHit rayHit) => new()
    {
        point = rayHit.point,
        normal = rayHit.normal,
        collider = rayHit.collider,
        transform = rayHit.transform
    };

    public static HitInfo FromCollider(Collider other, Transform self)
    {
        Vector3 closest = other.ClosestPoint(self.position);
        Vector3 dir = self.position - closest;
        return new HitInfo
        {
            point = closest,
            normal = dir.sqrMagnitude > 0.0001f ? dir.normalized : -self.forward,
            collider = other,
            transform = other.transform
        };
    }
}
