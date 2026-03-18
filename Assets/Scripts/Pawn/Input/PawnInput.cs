using FishNet.Object;

/// <summary>
/// Abstract base class for any component that supplies PawnInputData to the
/// pawn's handler components. Subclasses populate Data every frame.
///
/// Unity cannot serialize bare interfaces, so handler SerializeFields are
/// typed to this class while C# logic programs to IPawnInput.
/// </summary>
public abstract class PawnInput : NetworkBehaviour, IPawnInput
{
    public PawnInputData Data { get; protected set; }

    /// <summary>
    /// True when this machine should run the pawn's logic.
    /// </summary>
    public abstract bool IsActive { get; }
}
