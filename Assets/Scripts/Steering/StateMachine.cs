using UnityEngine;

namespace Arena.Steering
{
    /// <summary>
    /// Bare-bones FSM container. Owners (e.g. TestEnemy) feed Tick() each network tick
    /// from a server-only context. Holds no behaviour itself — states are constructed
    /// by the owner so they can close over MovementController, TargetProvider refs etc.
    /// </summary>
    public class StateMachine : MonoBehaviour
    {
        public IEnemyState Current { get; private set; }

        public void SetState(IEnemyState next)
        {
            if (next == Current) return;
            Current?.OnExit();
            Current = next;
            Current?.OnEnter();
        }

        public void Tick(float dt)
        {
            if (Current == null) return;
            var next = Current.Tick(dt);
            if (next != null && next != Current)
                SetState(next);
        }
    }
}
