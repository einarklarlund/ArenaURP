using UnityEngine;

/// <summary>
/// Per-frame snapshot of the world as seen by the bot.
/// Built by BotBrain from its component references and passed into BotBrainLogic.
/// </summary>
public struct BotWorldSnapshot
{
    public Vector3 SelfPosition;
    public float HealthFraction;
    public bool HasVisibleTarget;
    public Vector3 TargetPosition;
    public Vector3 LastKnownTargetPosition;
    public bool IsAtDestination;
    public Vector3 CurrentDestination;
}

/// <summary>
/// Tells BotBrain how to handle the navigation destination this frame.
/// </summary>
public enum NavigationIntent
{
    /// <summary>Don't change the agent's destination.</summary>
    KeepCurrent,

    /// <summary>Set the agent's destination to BotDecision.MovePosition.</summary>
    MoveToPosition,

    /// <summary>Sample a random reachable waypoint and set it as the destination.</summary>
    FindSearchWaypoint,

    /// <summary>Sample a cover position away from the threat and set it as the destination.</summary>
    FindCoverPosition,
}

/// <summary>
/// The output of BotBrainLogic.Evaluate. Describes what the bot should do this frame
/// without referencing any Unity components. BotBrain reads this and applies it
/// to BotPawnInput, NavMeshAgent, etc.
/// </summary>
public struct BotDecision
{
    public NavigationIntent Navigation;
    public Vector3 MovePosition;
    public Vector3? LookTarget;
    public bool Fire;
}

/// <summary>
/// Pure decision-making logic for a bot. Owns a BotStateMachine and produces
/// a BotDecision each frame given a world snapshot. Contains no Unity component
/// references, no NavMesh calls, and no MonoBehaviour dependencies.
/// </summary>
public sealed class BotBrainLogic
{
    public readonly BotStateMachine StateMachine = new();
    public float PreferredCombatRange = 10f;

    private BotStateMachine.BotState? previousState = null;

    public BotDecision Evaluate(BotWorldSnapshot world)
    {
        bool isLowHealth = world.HealthFraction <= StateMachine.LowHealthThreshold;
        var state = StateMachine.Evaluate(world.HasVisibleTarget, isLowHealth, world.IsAtDestination);

        bool stateChanged = state != previousState;
        previousState = state;

        return state switch
        {
            BotStateMachine.BotState.Searching => EvaluateSearching(world, stateChanged),
            BotStateMachine.BotState.Chasing => EvaluateChasing(world),
            BotStateMachine.BotState.Fleeing => EvaluateFleeing(world, stateChanged),
            _ => default,
        };
    }

    private BotDecision EvaluateSearching(BotWorldSnapshot world, bool justEntered)
    {
        return new BotDecision
        {
            Navigation = (justEntered || world.IsAtDestination)
                ? NavigationIntent.FindSearchWaypoint
                : NavigationIntent.KeepCurrent,
            LookTarget = null,
            Fire = false,
        };
    }

    private BotDecision EvaluateChasing(BotWorldSnapshot world)
    {
        if (world.HasVisibleTarget)
        {
            float distance = Vector3.Distance(world.SelfPosition, world.TargetPosition);

            return new BotDecision
            {
                Navigation = distance > PreferredCombatRange
                    ? NavigationIntent.MoveToPosition
                    : NavigationIntent.KeepCurrent,
                MovePosition = world.TargetPosition,
                LookTarget = world.TargetPosition,
                Fire = true,
            };
        }

        return new BotDecision
        {
            Navigation = NavigationIntent.MoveToPosition,
            MovePosition = world.LastKnownTargetPosition,
            LookTarget = world.LastKnownTargetPosition,
            Fire = false,
        };
    }

    private BotDecision EvaluateFleeing(BotWorldSnapshot world, bool justEntered)
    {
        return new BotDecision
        {
            Navigation = justEntered
                ? NavigationIntent.FindCoverPosition
                : NavigationIntent.KeepCurrent,
            LookTarget = world.HasVisibleTarget
                ? world.TargetPosition
                : world.CurrentDestination,
            Fire = world.HasVisibleTarget,
        };
    }
}
