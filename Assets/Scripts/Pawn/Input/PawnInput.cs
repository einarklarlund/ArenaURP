using FishNet.Managing.Timing;
using FishNet.Object;

/// <summary>
/// Abstract base class for any component that supplies movement data to the
/// pawn's handler components. Subclasses populate Data every frame.
/// </summary>
public abstract class PawnInput : NetworkBehaviour
{
    public MovementInputData MovementData { get; protected set; }
    public abstract void FireWeapon(PreciseTick tick);

    /// <summary>
    /// True when this machine should run the pawn's logic.
    /// </summary>
    public abstract bool IsActive { get; }
}
