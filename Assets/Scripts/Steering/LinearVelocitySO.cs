using System;
using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Drives the predicted rigidbody at a fixed linear velocity baked into the SO.
    /// Useful for blind-drift entities (rich projectiles, conveyor-belt enemies) where
    /// the FSM doesn't need world reads. Ignores TargetInfo — pair with a provider that
    /// always returns true.
    /// </summary>
    [CreateAssetMenu(menuName = "Arena/Steering/Linear Velocity", fileName = "LinearVelocity")]
    public class LinearVelocitySO : MovementStrategySO
    {
        [Tooltip("Linear velocity (m/s, body-local space) the predicted rigidbody is set to each tick.")]
        public Vector3 Velocity = Vector3.zero;

        public override IMovementStrategy CreateRuntime()
        {
            return new LinearVelocity(() => Velocity);
        }

        private class LinearVelocity : IMovementStrategy
        {
            private readonly Func<Vector3> _velocitySource;

            public LinearVelocity(Func<Vector3> velocitySource)
            {
                _velocitySource = velocitySource ?? throw new ArgumentNullException(nameof(velocitySource));
            }

            public void Tick(PerceptionContext ctx, TargetInfo target)
            {
                ctx.Body?.Velocity(ctx.SelfRotation * _velocitySource());
            }

            public void DrawGizmos(PerceptionContext ctx, TargetInfo target)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(ctx.SelfPosition, ctx.SelfPosition + ctx.SelfRotation * _velocitySource());
            }
        }
    }
}
