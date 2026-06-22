using UnityEngine;

public class ProjectileBulletImpact : ProjectileImpact
{
    protected ProjectileMovement movement;

    protected override void Awake()
    {
        base.Awake();
        movement = GetComponent<ProjectileMovement>();
    }

    public virtual void HandleImpact(ProjectileHitbox otherHitbox, HitInfo hit)
    {
        if (!IsServerInitialized) return;
        if (otherHitbox.TryGetComponent(out ProjectileState otherState))
        {
            var newVelocity = otherState.StartDirection * data.speed;
            movement.SetVelocity(newVelocity);
        }
    }
}
