using UnityEngine;

/// <summary>
/// Bot implementation of PawnInputProvider. Translates AI intent (move direction,
/// look target, fire flag) into PawnInputData each frame for input handlers.
/// </summary>
public sealed class BotPawnInput : PawnInputProvider
{
    [Header("Rotation")]
    [Tooltip("Maximum degrees per second the bot can rotate toward its look target.")]
    [SerializeField] private float maxTurnSpeed = 360f;

    [Tooltip("Maximum upward/downward pitch in degrees.")]
    [SerializeField] private float maxPitch = 60f;

    private Transform aimTransform; // set by BotBrain to the firePoint / camera transform

    // Intents written by BotBrain each frame
    private Vector3 moveIntentWorld = Vector3.zero;
    private Vector3 lookTargetWorld = Vector3.zero;
    private bool hasMoveIntent;
    private bool hasLookTarget;
    private bool wantsFire;

    // Track accumulated pitch so we can compute a delta for CameraInputHandler
    private float currentPitch;

    public override bool IsActive => IsServerInitialized;

    private void Update()
    {
        if (!IsServerInitialized) return;

        Vector3 effectiveTarget = hasLookTarget ? lookTargetWorld
            : hasMoveIntent ? transform.position + moveIntentWorld
            : transform.position + transform.forward;

        var lookResult = BotInputMath.ComputeLookInput
        (
            transform.position,
            transform.eulerAngles.y,
            currentPitch,
            effectiveTarget,
            hasLookTarget,
            maxTurnSpeed,
            maxPitch,
            Time.deltaTime
        );

        currentPitch = lookResult.UpdatedPitch;

        Data = new PawnInputData
        {
            Move = BotInputMath.ComputeMoveInput(moveIntentWorld, transform.rotation),
            Look = lookResult.LookDelta,
            Jump = false,
            Fire = wantsFire,
        };
    }

    // Intent setters called by BotBrain

    /// <summary>Sets the world-space direction the bot should move toward this frame.</summary>
    public void SetMoveIntent(Vector3 worldDirection)
    {
        moveIntentWorld = worldDirection;
        hasMoveIntent = worldDirection != Vector3.zero;
    }

    /// <summary>Sets the world-space position the bot should aim at.</summary>
    public void SetLookTarget(Vector3 worldPosition)
    {
        lookTargetWorld = worldPosition;
        hasLookTarget = true;
    }

    /// <summary>Clears the look target so the bot faces its movement direction.</summary>
    public void ClearLookTarget()
    {
        hasLookTarget = false;
    }

    /// <summary>Sets whether the bot should pull the trigger this frame.</summary>
    public void SetFiring(bool fire) => wantsFire = fire;
}
