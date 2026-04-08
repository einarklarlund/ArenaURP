using UnityEngine;

public class ProjectileSurfaceImpact : ProjectileImpact
{
    protected ProjectileState state;
    protected ProjectileMovement movement;
    protected ProjectileLifecycle lifecycle;

    protected override void Awake()
    {
        base.Awake();
        state = GetComponent<ProjectileState>();
        movement = GetComponent<ProjectileMovement>();
        lifecycle = GetComponent<ProjectileLifecycle>();
    }

    public void HandleImpact(HitInfo hit)
    {
        foreach (var prefab in data.spawnOnAnyImpact)
        {
            SpawnPrefab(prefab);
        }

        if (state.Health > 1)
        {
            state.Health--;
        }
        else
        {
            lifecycle.Kill();
        }
    }
}
