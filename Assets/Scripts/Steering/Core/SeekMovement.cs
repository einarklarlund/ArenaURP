using System;
using UnityEngine;

namespace Arena.Steering.Core
{
    public struct SeekConfig
    {
        public float MaxSpeed;
        public float MaxAccel;
        /// <summary>If > 0, ramp desired speed down inside this radius. 0 = no arrive behaviour.</summary>
        public float ArriveRadius;
        /// <summary>If true, ignore Y when seeking (XZ-only steering). Useful for ground enemies.</summary>
        public bool FlatXZ;
    }

    /// <summary>
    /// Classic seek-with-optional-arrive. Reads its config through a delegate so SO
    /// edits in Play mode flow through each tick. Stateless aside from the gizmo cache.
    /// </summary>
    public class SeekMovement : IMovementStrategy
    {
        private readonly Func<SeekConfig> _configSource;

        // Cached per-tick values for gizmo / debug overlay.
        public Vector3 LastDesiredVelocity { get; private set; }
        public Vector3 LastSteeringForce { get; private set; }

        public SeekMovement(Func<SeekConfig> configSource)
        {
            _configSource = configSource ?? throw new ArgumentNullException(nameof(configSource));
        }

        public void Tick(PerceptionContext ctx, TargetInfo target)
        {
            if (!target.HasTarget)
            {
                LastDesiredVelocity = Vector3.zero;
                LastSteeringForce = Vector3.zero;
                return;
            }

            SeekConfig cfg = _configSource();

            Vector3 toTarget = target.Transform.position - ctx.SelfPosition;
            Vector3 currentVelocity = ctx.SelfVelocity;
            if (cfg.FlatXZ)
            {
                toTarget.y = 0f;
                currentVelocity.y = 0f;
            }

            float distance = toTarget.magnitude;
            float speed = cfg.MaxSpeed;
            if (cfg.ArriveRadius > 0f && distance < cfg.ArriveRadius)
                speed = cfg.MaxSpeed * (distance / cfg.ArriveRadius);

            Vector3 desiredVelocity = distance > 1e-4f
                ? (toTarget / distance) * speed
                : Vector3.zero;

            Vector3 steering = desiredVelocity - currentVelocity;
            if (cfg.MaxAccel > 0f && steering.sqrMagnitude > cfg.MaxAccel * cfg.MaxAccel)
                steering = steering.normalized * cfg.MaxAccel;

            LastDesiredVelocity = desiredVelocity;
            LastSteeringForce = steering;

            ctx.Body?.AddForce(steering, ForceMode.Acceleration);
        }

        public void DrawGizmos(PerceptionContext ctx, TargetInfo target)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(ctx.SelfPosition, ctx.SelfPosition + LastDesiredVelocity);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(ctx.SelfPosition, ctx.SelfPosition + LastSteeringForce);
            if (target.HasTarget)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(ctx.SelfPosition, target.Transform.position);
            }
        }
    }
}
