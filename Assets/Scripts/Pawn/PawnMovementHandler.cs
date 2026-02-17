using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Executes the physical translation and simulation of the Pawn in the game world.
/// Responsibilities:
/// - Calculates horizontal velocity based on the Pawn's current forward orientation.
/// - Manages vertical forces including gravity, jumping, and ground sticking.
/// - Applies final movement to the CharacterController during the physics (FixedUpdate) step.
/// - Operates independently of the Camera, allowing for headless/server-side validation.
/// </summary>
public sealed class PawnMovementHandler : NetworkBehaviour
{

    [SerializeField]
    private CharacterController controller;
    [SerializeField]
    private Pawn pawn;

    [Header("Ground Movement")]
    [SerializeField]
    private float initialMaxSpeedXZ = 10f;
    [SerializeField]
    private float speedUpTime = 0.3f;
    [SerializeField]
    private float slowDownTime = 0.2f;

    [Header("Jumping")]
    [SerializeField]
    private float marioJumpTime = 0.2f;
    [SerializeField]
    private float jumpHeight = 3f;
    [SerializeField]
    private float jumpBufferWindow = 0.2f;

    [Header("On Hit Effects")]
    [SerializeField]
    private float speedOnHit = 10f;
    [SerializeField]
    private float minimumAngleOnHit = 60f;

    // stateful variables
    private float maxSpeedXZ;
    private float jumpPressedAt = float.MinValue;
    private float jumpBeganAt = float.MinValue;
    private bool marioJumpInterrupted = true;
    private Vector3 previousVelocity = Vector3.zero; // The velocity that the player had on the previous timestep  (frame)
    private bool wasHitThisFrame = false;
    private DamageInfo damageInfo;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if(!IsOwner) return;

        pawn.OnDamageTaken += OnDamageTaken;
    }

    void OnDamageTaken(DamageInfo damageInfo)
    {
        if(!IsOwner) return;

        wasHitThisFrame = true;
        this.damageInfo = damageInfo;
    }

    void Update()
    {
        if (!IsOwner)
            return;

        maxSpeedXZ = controller.isGrounded ? initialMaxSpeedXZ : initialMaxSpeedXZ + 5;

        Vector3 finalVelocityXZ = GetXZVelocity();
        float finalVelocityY = GetYVelocity();
        
        // Combine horizontal and vertical movement
        Vector3 finalVelocity = new Vector3(finalVelocityXZ.x, finalVelocityY, finalVelocityXZ.z);
        
        if (wasHitThisFrame)
        {
            finalVelocity = ApplyOnHitEffects(finalVelocity);
            wasHitThisFrame = false;
        }

        controller.Move(finalVelocity * Time.deltaTime);
        previousVelocity = finalVelocity;
    }

    Vector3 GetXZVelocity()
    {
        Vector3 previousVelocityXZ = Vector3.ProjectOnPlane(previousVelocity, Vector3.up);
        Vector3 finalVelocityXZ;

        // Read input
        float horizontalInput = pawn.Input.Data.Horizontal;
        float verticalInput = pawn.Input.Data.Vertical;
        Vector3 inputDirection = new
        (
            horizontalInput,
            0f,
            verticalInput
        );
        inputDirection = Vector3.ClampMagnitude(inputDirection, 1f); // the direction, in local space, that the player is trying to go

        // apply drag or input acceleration
        if (inputDirection == Vector3.zero && controller.isGrounded)
        {
            float dragAcceleration = maxSpeedXZ / slowDownTime;
            float deltaSpeedFromDrag = dragAcceleration * Time.deltaTime;
            if (previousVelocityXZ.magnitude - deltaSpeedFromDrag <= deltaSpeedFromDrag)
            {
                finalVelocityXZ = Vector3.zero;
            }
            else
            {
                finalVelocityXZ = Vector3.ClampMagnitude(previousVelocityXZ, previousVelocityXZ.magnitude - deltaSpeedFromDrag);
            }
        }
        else
        {
            Vector3 inputDirectionWorld = transform.TransformDirection(inputDirection); // direction that the player is trying to go
            Vector3 inputVelocity = inputDirectionWorld * maxSpeedXZ; // the velocity that the player "wants" be moving at
            float walkAcceleration = maxSpeedXZ / speedUpTime;

            if (!controller.isGrounded) // less acceleration if not grounded
                walkAcceleration /= 12;

            float deltaSpeedFromInput = walkAcceleration * Time.deltaTime; // the maximum difference in speed that the player can make in one frame
            Vector3 deltaVelocity = Vector3.ClampMagnitude(inputVelocity - previousVelocityXZ, deltaSpeedFromInput); // given what the velocity the player "wants" to move at, this is how much change in velocity they can make in one frame
            finalVelocityXZ = Vector3.ClampMagnitude(previousVelocityXZ + deltaVelocity, initialMaxSpeedXZ);
        }

        return finalVelocityXZ;
    }

    float GetYVelocity()
    {
        // Record the time at which the jump button was pressed
        if (pawn.Input.Data.Jump)
        {
            jumpPressedAt = Time.time;
        }
        else
        {
            // Interrupt mario jump if the jump key is let-go
            marioJumpInterrupted = true;
        }

        if (!controller.isGrounded)
        {
            return GetAerialYVelocity();
        }
        else
        {
            return GetGroundedYVelocity();
        }
    }

    private float GetAerialYVelocity()
    {
        float yVelocity = previousVelocity.y;

        // Continue applying jump force (Mario Jump) if Mario Jump time hasn't run out and the jump press hasn't been interrupted
        if (!marioJumpInterrupted && Time.time - jumpBeganAt <= marioJumpTime)
        {
            yVelocity = GetJumpYVelocity();
        }

        // Apply gravity
        yVelocity += Physics.gravity.y * Time.deltaTime;
        return yVelocity;
    }

    private float GetGroundedYVelocity()
    {
        // Calculate whether jump buffer window has passed
        bool isJumpBuffered = Time.time - jumpPressedAt <= jumpBufferWindow;

        // Initiate the jump
        if (controller.isGrounded && isJumpBuffered)
        {
            marioJumpInterrupted = false;
            jumpBeganAt = Time.time;
            return GetJumpYVelocity();
        }

        // There is no grounded Y velocity if not jumping
        return 0;
    }

    float GetJumpYVelocity()
    {
        return Mathf.Sqrt(-1 * jumpHeight * Physics.gravity.y);
    }

    private Vector3 ApplyOnHitEffects(Vector3 velocity)
    {
        var dir = damageInfo.Direction.normalized;

        float horizontalMag = Vector3.ProjectOnPlane(dir, Vector3.up).magnitude;

        // Calculate the minimum Y required for a 30-degree incline 
        // tan(30°) * horizontal distance = required height
        float minHeight = horizontalMag * Mathf.Tan(minimumAngleOnHit * Mathf.Deg2Rad);

        // Ensure dir.y is at least that height
        dir.y = Mathf.Max(dir.y, minHeight);
        dir = dir.normalized;

        return speedOnHit * dir;
    }
}
