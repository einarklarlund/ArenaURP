using Arena.Steering.Core;
using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Factory for an ITargetProvider runtime instance. Stateless providers may bypass
    /// the runtime instance and return themselves; stateful ones (waypoint cursors,
    /// transform refs) follow the same flyweight split as movement strategies.
    /// </summary>
    public abstract class TargetProviderSO : ScriptableObject
    {
        public abstract ITargetProvider CreateRuntime();
    }
}
