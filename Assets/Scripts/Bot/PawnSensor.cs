using UnityEngine;

/// <summary>
/// Server-side vision sensor. Periodically checks which player pawns are
/// within the bot's field of view and have unobstructed line of sight.
/// 
/// Depends on PawnManager for a list of live pawns.
/// Depends on Pawn for identity (to not target itself).
/// </summary>
public sealed class PawnSensor : Sensor
{
    [SerializeField] private PawnManager pawnManager;
    [SerializeField] private Pawn selfPawn;

    [Header("Vision Cone")]
    [SerializeField] private float viewRange  = 30f;
    [SerializeField] private float viewAngle  = 60f;  // total degrees, centred on forward

    [Header("Performance")]
    [Tooltip("Seconds between full vision scans. Lower = more responsive, more CPU.")]
    [SerializeField] private float scanInterval = 0.1f;

    [SerializeField] private LayerMask obstructionMask;

    private float nextScanTime;

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (pawnManager == null)
            pawnManager = FindAnyObjectByType<PawnManager>();
    }

    private void Update()
    {
        if (!IsServerInitialized) return;
        if (Time.time < nextScanTime) return;

        nextScanTime = Time.time + scanInterval;
        ScanForTargets();
    }

    private void ScanForTargets()
    {
        Pawn bestTarget = null;

        foreach (Pawn candidate in pawnManager.ActivePawns)
        {
            // Ignore self
            if (candidate == selfPawn) continue;

            Vector3 eyePos    = transform.position;
            Vector3 targetPos = candidate.transform.position;

            if (!BotVisionMath.IsInFieldOfView(eyePos, transform.forward, targetPos, viewAngle, viewRange))
                continue;

            if (!HasLineOfSight(eyePos, targetPos))
                continue;

            bestTarget = candidate;
            break; // take the first valid target
        }

        if (bestTarget != null)
        {
            HasVisibleTarget        = true;
            VisibleTarget           = bestTarget;
            LastKnownTargetPosition = bestTarget.transform.position;
        }
        else
        {
            HasVisibleTarget = false;
            VisibleTarget    = null;
            // LastKnownTargetPosition retains its last value intentionally.
        }
    }

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        return !Physics.Raycast(from, direction.normalized, direction.magnitude, obstructionMask);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = HasVisibleTarget ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRange);

        if (HasVisibleTarget && VisibleTarget != null)
            Gizmos.DrawLine(transform.position, VisibleTarget.transform.position);
    }
}
