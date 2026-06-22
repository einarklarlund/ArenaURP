using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    [CreateAssetMenu(menuName = "Arena/Steering/Transform Target", fileName = "TransformTarget")]
    public class TransformTargetSO : TargetProviderSO
    {
        public override ITargetProvider CreateRuntime() => new TransformTargetProvider();
    }
}
