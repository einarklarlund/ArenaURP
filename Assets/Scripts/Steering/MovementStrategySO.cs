using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Tuning data + factory for an IMovementStrategy. One asset per "kind" of movement
    /// (FastSeeker, SlowSeeker, etc.); shared across all entities using it. Tweakable in
    /// Play mode — runtime instances should read tunables through CurrentConfig() so edits
    /// flow through immediately.
    /// </summary>
    public abstract class MovementStrategySO : ScriptableObject
    {
        public abstract IMovementStrategy CreateRuntime();
    }
}
