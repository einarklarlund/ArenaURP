using UnityEngine;

/// <summary>
/// Standing movement buff: multiply the pawn's max walk speed while the source passive is equipped. Typed to
/// <see cref="IMovementModifierContext"/> so the variance-aware picker offers it on a passive's stat-buff list.
/// Contributes one <see cref="MovementModifier"/> (folded multiplicatively by the pure MovementProcessor) rather
/// than mutating the pawn — the passive facade owns applying/removing it. Stateless flyweight: only the
/// immutable serialized multiplier.
/// </summary>
[System.Serializable]
public sealed class BuffWalkSpeed : IEffect<IMovementModifierContext>
{
    [SerializeField] float multiplier = 1.5f;

    public BuffWalkSpeed() { }
    public BuffWalkSpeed(float multiplier) { this.multiplier = multiplier; }

    public void Apply(IMovementModifierContext ctx)
    {
        var m = MovementModifier.Identity;
        m.WalkSpeedMultiplier = multiplier;
        ctx.AddMovementModifier(m);
    }
}
