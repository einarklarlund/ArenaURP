using UnityEngine;

[RequireComponent(typeof(ProjectileHitEffects))]
[RequireComponent(typeof(ProjectileDeflection))]
public class ProjectileCollision : MonoBehaviour
{
    protected ProjectileData data;
    protected ProjectileDeflection deflection;
    protected ProjectileHitEffects hitEffects;

    public virtual void OnInitialize(ProjectileData projectileData)
    {
        data = projectileData;
        deflection = GetComponent<ProjectileDeflection>();
        hitEffects = GetComponent<ProjectileHitEffects>();
    }

    public virtual void GetCapsulePoints(Vector3 center, out Vector3 p1, out Vector3 p2)
    {
        Vector3 offset = data.directionAxis switch
        {
            0 => transform.right,
            1 => transform.up,
            _ => transform.forward
        };
        float halfHeight = data.height / 2f;
        p1 = center + offset * halfHeight;
        p2 = center - offset * halfHeight;
    }

    public virtual bool CheckCollision(Vector3 velocity, float deltaTime)
    {
        float stepDistance = velocity.magnitude * deltaTime;
        var vel = velocity == Vector3.zero ? Vector3.forward : velocity.normalized;
        bool hasCollided = false;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);

        var hits = Physics.CapsuleCastAll(p1, p2, data.radius, vel, stepDistance + 0.05f, data.hitLayers);
        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent<ProjectileData>(out var otherBullet))
            {
                if (otherBullet != data && otherBullet.deflectOtherBullets)
                    deflection.Deflect(otherBullet, hit);
                continue;
            }

            if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
            {
                if (!(damageable is Pawn pawn && pawn.ControllingPlayer.Value == data.spawnState.Firer))
                {
                    HandleHitDamageable(hit, damageable);
                    hasCollided = true;
                }
            }
            else
            {
                hasCollided = true;
            }
        }

        return hasCollided;
    }

    private void HandleHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        if (data.IsServerInitialized)
            ServerHitDamageable(hit, damageable);
        else
            ClientHitDamageable(hit, damageable);
    }

    private void ServerHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        transform.position = hit.point;

        if (damageable != null)
        {
            DamageInfo info = new()
            {
                Amount    = data.damage,
                Attacker  = data.spawnState.Firer,
                HitPoint  = hit.point,
                Direction = transform.forward,
                Type      = DamageType.Bullet
            };
            damageable.ServerTakeDamage(info);
        }
    }

    private void ClientHitDamageable(RaycastHit hit, IDamageable damageable)
    {
        hitEffects.PlayHitEffects(hit.point, hit.normal);
    }

    private void OnDrawGizmosSelected()
    {
        if (data == null) data = GetComponent<ProjectileData>();
        if (data == null) return;

        Gizmos.color = Color.red;
        GetCapsulePoints(transform.position, out Vector3 p1, out Vector3 p2);
        Gizmos.DrawWireSphere(p1, data.radius);
        Gizmos.DrawWireSphere(p2, data.radius);
        Gizmos.DrawLine(p1, p2);
    }
}
