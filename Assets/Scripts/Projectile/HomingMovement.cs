using UnityEngine;

public class HomingMovement : ProjectileMovement
{
    private NetworkPlayerManager _playerManager;
    private Pawn _targetPawn;

    protected override void ApplyForces()
    {
        if (!IsTargetValid())
        {
            _targetPawn = PickTarget();
        }

        if (_targetPawn == null)
            return;

        Vector3 toTarget = _targetPawn.transform.position - transform.position;
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 desiredVelocity = toTarget.normalized * data.speed;
        Vector3 velocity = PredictionRigidbody.Rigidbody.linearVelocity;
        float deltaTime = (float) TimeManager.TickDelta;
        float maxDeltaVelocity = data.acceleration * deltaTime;
        Vector3 deltaVelocity = desiredVelocity - velocity;
        Vector3 deltaVelocityClamped = Vector3.ClampMagnitude(deltaVelocity, maxDeltaVelocity);

        PredictionRigidbody.Velocity(velocity + deltaVelocityClamped);
    }

    private bool IsTargetValid()
    {
        return _targetPawn != null && _targetPawn.IsSpawned && _targetPawn.Health.Value > 0;
    }

    private Pawn PickTarget()
    {
        if (_playerManager == null)
            _playerManager = FindAnyObjectByType<NetworkPlayerManager>();

        if (_playerManager == null)
            return null;

        Pawn closest = null;
        float closestSqr = float.PositiveInfinity;
        Vector3 origin = transform.position;

        foreach (var player in _playerManager.ActivePlayers)
        {
            if (player == null || player == state.Firer)
                continue;

            var pawn = player.ControlledPawn.Value;
            if (pawn == null || !pawn.IsSpawned || pawn.Health.Value <= 0)
                continue;

            float sqr = (pawn.transform.position - origin).sqrMagnitude;
            if (sqr < closestSqr)
            {
                closestSqr = sqr;
                closest = pawn;
            }
        }

        return closest;
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        _targetPawn = null;
    }
}
