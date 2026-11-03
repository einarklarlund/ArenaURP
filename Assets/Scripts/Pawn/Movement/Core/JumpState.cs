/// <summary>
/// Jump state the processor carries from one tick to the next. Unlike MovementState,
/// the CharacterController can't tell us any of this, so the caller owns it.
/// </summary>
[System.Serializable]
public struct JumpState
{
    public float TimeSinceJumpPressed;
    public float TimeSinceJumpBegan;

    /// <summary>
    /// Whether the mario jump (hold-to-jump-higher) has been interrupted
    /// by releasing the jump button.
    /// </summary>
    public bool MarioJumpInterrupted;

    /// <summary>
    /// Whether a jump or knockback has lifted the character and it hasn't started falling yet.
    /// Keeps it airborne while the ground probe can still reach the ground it left.
    /// </summary>
    public bool IsLaunched;

    /// <summary>
    /// Large sentinel value indicating "jump was never pressed" or
    /// "so long ago that the jump buffer has expired."
    /// </summary>
    public const float JumpTimeNotSet = 1000f;

    public static JumpState Default => new()
    {
        TimeSinceJumpPressed = JumpTimeNotSet,
        TimeSinceJumpBegan = JumpTimeNotSet,
        MarioJumpInterrupted = true,
    };
}
