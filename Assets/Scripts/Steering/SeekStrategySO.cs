using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    [CreateAssetMenu(menuName = "Arena/Steering/Seek Strategy", fileName = "SeekStrategy")]
    public class SeekStrategySO : MovementStrategySO
    {
        [Tooltip("Maximum speed the entity will try to reach toward the target.")]
        public float MaxSpeed = 6f;

        [Tooltip("Maximum steering force applied per tick (acceleration mode). 0 = unclamped.")]
        public float MaxAccel = 30f;

        [Tooltip("Distance at which to ramp speed down to zero (arrive). 0 = no arrive.")]
        public float ArriveRadius = 0f;

        [Tooltip("Ignore Y when steering — keeps ground enemies from launching upward.")]
        public bool FlatXZ = true;

        public override IMovementStrategy CreateRuntime()
        {
            return new SeekMovement(GetConfig);
        }

        private SeekConfig GetConfig() => new()
        {
            MaxSpeed = MaxSpeed,
            MaxAccel = MaxAccel,
            ArriveRadius = ArriveRadius,
            FlatXZ = FlatXZ,
        };
    }
}
