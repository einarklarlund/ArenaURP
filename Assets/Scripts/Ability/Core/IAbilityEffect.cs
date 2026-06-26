using System.Collections.Generic;
using Arena.Projectile;

namespace Arena.Ability
{
    /// <summary>
    /// A composable ability effect. Mirrors <see cref="IHitReaction"/> exactly: it is pure, it APPENDS
    /// shared <see cref="WorldEffect"/> values to the buffer, and it never touches the engine or gates.
    /// Because abilities emit the SAME <see cref="WorldEffect"/> vocabulary that projectile reactions
    /// emit, the imperative shell executes both through one path
    /// (<see cref="ProjectileWorldProvider.Execute"/>) — so an effect authored once is reusable across
    /// projectiles and abilities.
    ///
    /// New effect types are added by implementing this interface (authored on an ability via
    /// <c>[SerializeReference]</c>, like reactions on a <see cref="ProjectileType"/>).
    /// </summary>
    public interface IAbilityEffect
    {
        void Collect(in AbilityContext ctx, List<WorldEffect> effects);
    }
}
