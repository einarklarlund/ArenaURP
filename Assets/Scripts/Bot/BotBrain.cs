using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-side orchestrator for a bot. Each frame it builds a world snapshot,
/// passes it to BotBrainLogic for a decision, and applies that decision to the
/// bot's components (BotPawnInput, NavMeshAgent).
///
/// All behavioral logic (when to chase, flee, fire, etc.) lives in BotBrainLogic.
/// This class only handles component wiring and NavMesh sampling.
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

    private readonly BotBrainLogic logic = new();
    private Vector3 currentDestination;

    public override void OnStartServer()
    {
        base.OnStartServer();

        agent.updatePosition = false;
        agent.updateRotation = false;
        logic.PreferredCombatRange = preferredCombatRange;

        SetNewSearchWaypoint();
    }

    private void Update()
    {
        if (!IsServerInitialized) return;

        var snapshot = BuildSnapshot();
        var decision = logic.Evaluate(snapshot);
        ApplyDecision(decision);

        agent.nextPosition = transform.position;
    }

    private BotWorldSnapshot BuildSnapshot()
    {
        return new BotWorldSnapshot
        {
            SelfPosition = transform.position,
            HealthFraction = (float)pawn.Health.Value / pawn.MaxHealth.Value,
            HasVisibleTarget = enemyVision.HasVisibleTarget,
            TargetPosition = enemyVision.HasVisibleTarget && enemyVision.VisibleTarget != null
                ? enemyVision.VisibleTarget.transform.position
                : enemyVision.LastKnownTargetPosition,
            LastKnownTargetPosition = enemyVision.LastKnownTargetPosition,
            IsAtDestination = IsAtDestination(),
            CurrentDestination = currentDestination,
        };
    }

    private void ApplyDecision(BotDecision decision)
    {
        // Navigation
        switch (decision.Navigation)
        {
            case NavigationIntent.MoveToPosition:
                currentDestination = decision.MovePosition;
                agent.SetDestination(currentDestination);
                break;
            case NavigationIntent.FindSearchWaypoint:
                SetNewSearchWaypoint();
                break;
            case NavigationIntent.FindCoverPosition:
                currentDestination = FindCoverPosition();
                agent.SetDestination(currentDestination);
                break;
        }

        // Movement
        Vector3 desired = agent.desiredVelocity;
        botInput.SetMoveIntent(desired.magnitude > 0.01f ? desired.normalized : Vector3.zero);

        // Look
        if (decision.LookTarget.HasValue)
            botInput.SetLookTarget(decision.LookTarget.Value);
        else
            botInput.ClearLookTarget();

        // Fire
        botInput.SetFiring(decision.Fire);
    }

    // Navigation helpers (NavMesh-dependent, not extractable to pure C#)

    private bool IsAtDestination()
    {
        if (!agent.pathPending && agent.remainingDistance <= arrivalThreshold)
            return true;

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

        return transform.position;
    }
}
