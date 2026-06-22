using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Returns a target tracking a Unity Transform. Target is assigned at runtime
    /// (e.g. by an FSM state) since SOs cannot reference scene objects.
    /// Returns HasTarget=false when Target is null.
    /// </summary>
    public class TransformTargetProvider : ITargetProvider
    {
        public Transform Target;
        private Vector3 _lastPosition;
        private bool _hasLast;

        public TargetInfo GetTargetInfo(PerceptionContext ctx)
        {
            if (Target == null)
            {
                _hasLast = false;
                return default;
            }

            Vector3 pos = Target.position;
            Vector3 vel = Vector3.zero;
            if (_hasLast && ctx.DeltaTime > 0f)
                vel = (pos - _lastPosition) / ctx.DeltaTime;
            _lastPosition = pos;
            _hasLast = true;

            return new TargetInfo
            {
                HasTarget = true,
                Transform = Target,
            };
        }

        public void DrawGizmos(PerceptionContext ctx)
        {
            if (Target == null) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(Target.position, 0.4f);
        }
    }
}
