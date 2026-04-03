using UnityEngine;

public class ProjectileMovement : MonoBehaviour
{
    protected ProjectileData data;

    public virtual void OnInitialize(ProjectileData projectileData, float elapsed)
    {
        data = projectileData;
    }

    public virtual Vector3 CalculatePosition(float time)
    {
        return data.spawnState.StartPosition
             + (data.speed * time * data.spawnState.StartDirection)
             + (0.5f * data.acceleration * time * time * data.spawnState.StartDirection);
    }
}
