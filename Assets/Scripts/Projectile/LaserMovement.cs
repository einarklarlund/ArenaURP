using UnityEngine;

public class LaserMovement : ProjectileMovement
{
    public override Vector3 CalculatePosition(float time)
    {
        return data.spawnState.StartPosition;
    }
}
