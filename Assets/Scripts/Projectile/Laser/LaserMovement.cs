using UnityEngine;

public class LaserMovement : ProjectileMovement
{
    protected override Vector3 CalculatePosition(float time)
    {
        return state.StartPosition;
    }

    protected override Vector3 CalculateVelocityAtTime(float time)
    {
        return Vector3.zero;
    }
}
