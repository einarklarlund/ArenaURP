using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Picks the closest live Pawn to the steering entity and caches it for
    /// <see cref="RefreshInterval"/> seconds before rescanning. Returns
    /// HasTarget=false until a Pawn has been found at least once, or while the
    /// PawnManager is unavailable.
    /// </summary>
    public class ClosestPawnTargetProvider : ITargetProvider
    {
        public float RefreshInterval = 3f;

        private PawnManager _pawnManager;
        private Transform _cachedTarget;
        private float _nextRefreshTime;

        public TargetInfo GetTargetInfo(PerceptionContext ctx)
        {
            if (Time.time >= _nextRefreshTime || _cachedTarget == null)
            {
                _cachedTarget = FindClosestPawn(ctx.SelfPosition);
                _nextRefreshTime = Time.time + RefreshInterval;
            }

            if (_cachedTarget == null)
                return default;

            return new TargetInfo
            {
                HasTarget = true,
                Transform = _cachedTarget,
            };
        }

        public void DrawGizmos(PerceptionContext ctx)
        {
            if (_cachedTarget == null) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_cachedTarget.position, 0.4f);
        }

        private Transform FindClosestPawn(Vector3 from)
        {
            if (_pawnManager == null)
                _pawnManager = Object.FindAnyObjectByType<PawnManager>();
            if (_pawnManager == null) return null;

            Transform best = null;
            float bestSqr = float.PositiveInfinity;
            foreach (Pawn candidate in _pawnManager.ActivePawns)
            {
                if (candidate == null) continue;
                Vector3 delta = candidate.transform.position - from;
                float sqr = delta.sqrMagnitude;
                // Skip self: anything sitting exactly on the steering entity is
                // either the entity itself or stacked overlap — neither makes a
                // useful seek target.
                if (sqr < 1e-6f) continue;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate.transform;
                }
            }
            return best;
        }
    }
}
