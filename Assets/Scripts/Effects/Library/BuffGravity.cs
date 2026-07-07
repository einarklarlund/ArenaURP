using UnityEngine;

/// <summary>
/// Standing movement buff: multiply the gravity applied to the pawn while the source passive is equipped (less
/// than 1 = floatier). Typed to <see cref="IMovementModifierContext"/>; contributes one
/// <see cref="MovementModifier"/> folded by the pure MovementProcessor. Stateless flyweight: only the immutable
/// serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffGravity : IEffect<IMovementModifierContext>
{
    [SerializeField] float multiplier = 1f;

    public BuffGravity() { }
    public BuffGravity(float multiplier) { this.multiplier = multiplier; }

    public void Apply(IMovementModifierContext ctx)
    {
        var m = MovementModifier.Identity;
        m.GravityMultiplier = multiplier;
        ctx.AddMovementModifier(m);
    }
}
