using UnityEngine;

public class ProjectileBulletImpact : ProjectileImpact
{
    protected ProjectileMovement movement;

    protected override void Awake()
    {
        base.Awake();
        movement = GetComponent<ProjectileMovement>();
    }

    public void HandleImpact(ProjectileState otherState, HitInfo hit)
    {
        if (!IsServerInitialized) return;
        var newVelocity = otherState.StartDirection * data.speed;
        movement.SetVelocity(newVelocity);
    }
}
