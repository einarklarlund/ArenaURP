using UnityEngine;

/// <summary>
/// Standing movement buff: multiply the pawn's jump force while the source passive is equipped. Typed to
/// <see cref="IMovementModifierContext"/>; contributes one <see cref="MovementModifier"/> folded by the pure
/// MovementProcessor. Stateless flyweight: only the immutable serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffJumpForce : IEffect<IMovementModifierContext>
{
    [SerializeField] float multiplier = 1.5f;

    public BuffJumpForce() { }
    public BuffJumpForce(float multiplier) { this.multiplier = multiplier; }

    public void Apply(IMovementModifierContext ctx)
    {
        var m = MovementModifier.Identity;
        m.JumpForceMultiplier = multiplier;
        ctx.AddMovementModifier(m);
    }
}
