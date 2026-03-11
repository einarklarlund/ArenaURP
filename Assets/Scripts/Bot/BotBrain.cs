using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-side state machine controller for a bot.
/// Issues move/look/fire intents to BotPawnInput.
/// Depends on BotStateMachine to tracks and evaluate state transitions.
/// Depends on Vision to inform state transitions.
/// Depends on NavMeshAgent to plan paths to target.
/// </summary>
public sealed class BotBrain : NetworkBehaviour
{
    [SerializeField] private Pawn pawn;
    [SerializeField] private BotPawnInput botInput;
    [SerializeField] private Sensor enemyVision;
    [SerializeField] private NavMeshAgent agent;

    [Header("Navigation")]
    [Tooltip("Radius used when sampling random NavMesh waypoints for the Search state.")]
    [SerializeField] private float waypointSearchRadius = 40f;

    [Tooltip("Distance the bot tries to put between itself and the threat when fleeing.")]
    [SerializeField] private float coverDistance = 20f;

    [Tooltip("How close the bot must be to its destination to consider it arrived.")]
    [SerializeField] private float arrivalThreshold = 1.5f;

    [Header("Combat")]
    [Tooltip("The bot will stop closing in on the target once within this range.")]
    [SerializeField] private float preferredCombatRange = 10f;

    private readonly BotStateMachine stateMachine = new();

    private Vector3 currentDestination;
    private BotStateMachine.BotState previousState;

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Let character controller update the transform.
        agent.updatePosition = false;
        agent.updateRotation = false;

        previousState = BotStateMachine.BotState.Searching;
        
        SetNewSearchWaypoint();
    }

    private void Update()
    {
        if (!IsServerInitialized) return;

        bool isLowHealth = (float)pawn.Health.Value / pawn.MaxHealth.Value <= stateMachine.LowHealthThreshold;
        bool isAtDest = IsAtDestination();
        var newState = stateMachine.Evaluate(enemyVision.HasVisibleTarget, isLowHealth, isAtDest);

        bool stateChanged = newState != previousState;
        previousState = newState;

        switch (newState)
        {
            case BotStateMachine.BotState.Searching:
                TickSearching(stateChanged, isAtDest);
                break;

            case BotStateMachine.BotState.Chasing:
                TickChasing(stateChanged);
                break;

            case BotStateMachine.BotState.Fleeing:
                TickFleeing(stateChanged);
                break;
        }

        // Keep the NavMeshAgent's internal position in sync with the actual transform
        // so path recalculations remain accurate.
        agent.nextPosition = transform.position;
    }

    // State tick handlers 

    private void TickSearching(bool justEntered, bool arrivedAtWaypoint)
    {
        if (justEntered || arrivedAtWaypoint)
            SetNewSearchWaypoint();

        MoveTowardDestination();
        botInput.ClearLookTarget();
        botInput.SetFiring(false);
    }

    private void TickChasing(bool justEntered)
    {
        Pawn target = enemyVision.VisibleTarget;

        if (enemyVision.HasVisibleTarget && target != null)
        {
            // Chase to within preferred combat range; stop closing in once there.
            Vector3 toTarget = target.transform.position - transform.position;
            if (toTarget.magnitude > preferredCombatRange)
                currentDestination = target.transform.position;

            botInput.SetLookTarget(target.transform.position);
        }
        else
        {
            // Lost sight ??? move to last known position.
            currentDestination = enemyVision.LastKnownTargetPosition;
            botInput.SetLookTarget(enemyVision.LastKnownTargetPosition);
        }

        agent.SetDestination(currentDestination);
        MoveTowardDestination();
        botInput.SetFiring(enemyVision.HasVisibleTarget);
    }

    private void TickFleeing(bool justEntered)
    {
        if (justEntered)
            currentDestination = FindCoverPosition();

        if (enemyVision.HasVisibleTarget && enemyVision.VisibleTarget != null)
            botInput.SetLookTarget(enemyVision.VisibleTarget.transform.position);
        else
            botInput.SetLookTarget(currentDestination);

        agent.SetDestination(currentDestination);
        MoveTowardDestination();
        botInput.SetFiring(enemyVision.HasVisibleTarget);
    }

    // Navigation helpers

    private void MoveTowardDestination()
    {
        // NavMeshAgent.desiredVelocity is the world-space velocity the agent wants.
        // BotPawnInput will convert it to local-space WASD for MovementInputHandler.
        Vector3 desired = agent.desiredVelocity;
        botInput.SetMoveIntent(desired.magnitude > 0.01f ? desired.normalized : Vector3.zero);
    }

    private bool IsAtDestination()
    {
        if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
            return true;

        // Also treat "very close" as arrived to avoid stopping exactly at origin.
        return Vector3.Distance(transform.position, currentDestination) <= arrivalThreshold;
    }

    private void SetNewSearchWaypoint()
    {
        Vector3 candidate = transform.position + Random.insideUnitSphere * waypointSearchRadius;
        candidate.y = transform.position.y;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, waypointSearchRadius, NavMesh.AllAreas))
            currentDestination = hit.position;

        agent.SetDestination(currentDestination);
    }

    private Vector3 FindCoverPosition()
    {
        Vector3 awayDir = (transform.position - enemyVision.LastKnownTargetPosition).normalized;
        Vector3 candidate = transform.position + awayDir * coverDistance;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, coverDistance, NavMesh.AllAreas))
            return hit.position;

        return transform.position; // fallback: stay put
    }
}
