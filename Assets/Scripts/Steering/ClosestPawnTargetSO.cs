using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    [CreateAssetMenu(menuName = "Arena/Steering/Closest Pawn Target", fileName = "ClosestPawnTarget")]
    public class ClosestPawnTargetSO : TargetProviderSO
    {
        [Tooltip("Seconds between target rescans. The closest Pawn is cached between refreshes.")]
        public float RefreshInterval = 3f;

        public override ITargetProvider CreateRuntime() => new ClosestPawnTargetProvider
        {
            RefreshInterval = RefreshInterval,
        };
    }
}
