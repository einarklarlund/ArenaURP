namespace Arena.Steering
{
    /// <summary>
    /// One state in an enemy FSM. Returning a different state from Tick triggers a transition;
    /// returning the same instance (or itself) keeps the state. Implementations are responsible
    /// for swapping the MovementController's strategy and target provider in OnEnter.
    /// </summary>
    public interface IEnemyState
    {
        void OnEnter();
        void OnExit();
        IEnemyState Tick(float dt);
    }
}
