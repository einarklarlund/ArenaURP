using UnityEngine;

public struct MovementInputData
{
    public Vector2 Move;
    /// <summary>Held, or pressed at any time since the last movement tick.</summary>
    public bool Jump;
    /// <summary>Jump went down since the last movement tick, even if it is still held or went down more than once:
    /// each press refills the jump buffer, however fast the presses come.</summary>
    public bool JumpPressed;
    public bool Crouch;
    public bool Sprint;
    public Vector2 Look;
}
