/// <summary>
/// Pure C# state machine for bot behaviour, containing transition logic.
/// </summary>
public sealed class BotStateMachine
{
    public enum BotState
    {
        /// <summary>No target visible; bot wanders between waypoints.</summary>
        Searching,

        /// <summary>Target visible and health above threshold; bot pursues and shoots.</summary>
        Chasing,

        /// <summary>Target visible (or recently seen) and health at or below threshold; bot retreats and shoots.</summary>
        Fleeing,
    }

    /// <summary>Fraction of max health below which the bot switches to Fleeing.</summary>
    public float LowHealthThreshold = 0.5f;

    public BotState CurrentState { get; private set; } = BotState.Searching;

    /// <summary>
    /// Evaluates the state machine for one tick and returns the (possibly new) state.
    /// </summary>
    /// <param name="hasTarget">Whether the sensor currently sees a target.</param>
    /// <param name="isLowHealth">Whether the bot's health is at or below LowHealthThreshold.</param>
    /// <param name="isAtDestination">Whether the bot has arrived at its current movement goal.</param>
    public BotState Evaluate(bool hasTarget, bool isLowHealth, bool isAtDestination)
    {
        switch (CurrentState)
        {
            case BotState.Searching:
                if (hasTarget && !isLowHealth) Transition(BotState.Chasing);
                else if (hasTarget && isLowHealth) Transition(BotState.Fleeing);
                break;

            case BotState.Chasing:
                if (isLowHealth)                           Transition(BotState.Fleeing);
                else if (!hasTarget && isAtDestination)    Transition(BotState.Searching);
                break;

            case BotState.Fleeing:
                if (!isLowHealth && hasTarget)             Transition(BotState.Chasing);
                else if (!hasTarget && isAtDestination)    Transition(BotState.Searching);
                break;
        }

        return CurrentState;
    }

    private void Transition(BotState next)
    {
        CurrentState = next;
    }
}
