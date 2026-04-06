using UnityEngine;

public class ProjectileMovementArc : ProjectileMovement
{
    [SerializeField] private float gravity = -9.81f;

    protected override void ApplyForces()
    {
        base.ApplyForces();
        PredictionRigidbody.AddForce(Vector3.up * gravity, ForceMode.Acceleration);
    }

    protected override Vector3 CalculatePosition(float time)
    {
        Vector3 startPos = state.StartPosition;
        Vector3 startVelocity = state.StartDirection * data.speed;

        Vector3 xzVelocity = Vector3.ProjectOnPlane(startVelocity, Vector3.up);
        Vector3 xzDisp = xzVelocity * time;

        float yDist = startVelocity.y * time + 0.5f * gravity * time * time;
        Vector3 yDisp = yDist * Vector3.up;

        return startPos + xzDisp + yDisp;
    }

    protected override Vector3 CalculateVelocityAtTime(float time)
    {
        Vector3 startVelocity = state.StartDirection * data.speed;
        return startVelocity + Vector3.up * gravity * time;
    }
}
