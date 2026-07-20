using UnityEngine;

/// <summary>
/// One effect a passive ability applies (ADR-pending: shared-registry passive model). A <see cref="Passive"/> is
/// an APPLICATOR, not the behaviour: <see cref="Apply"/> creates or configures a logic-component ON a target
/// GameObject, and that installed/configured component owns the actual logic — a movement modifier on a pawn, a
/// durability multiplier or an <c>IHitHandler</c> on a projectile. The passive itself only knows WHAT to
/// install/configure and WHICH targets can receive it (<see cref="CanApplyTo"/>); it is deliberately NOT an
/// <c>IHitHandler</c> (that is the target's hit-response system).
///
/// Concrete passives are components authored on a child of the shared passive registry; a passive KIND may
/// compose several (e.g. "faster + tougher bullets"). They are shared across all owners, so they hold NO
/// per-owner state — <see cref="Apply"/> acts only on the passed <paramref name="target"/>.
///
/// TARGETS drive timing: a pawn applies its owner's passives on grant/spawn, a projectile on spawn. A passive
/// that CONFIGURES an existing idempotent per-spawn seam (durability, the modifier fold) is pool-safe for free.
/// A passive that ADDS a component to a POOLED target (a projectile) must make it removable on despawn — that
/// teardown mechanism is built with the concrete create-passive that needs it (steal / instantiate-on-hit), not
/// speculatively here.
/// </summary>
public abstract class Passive : MonoBehaviour
{
    /// <summary>True if this passive can act on <paramref name="target"/> (e.g. a movement buff → only pawns; a
    /// ricochet → only projectiles). The target calls this before <see cref="Apply"/>.</summary>
    public abstract bool CanApplyTo(GameObject target);

    /// <summary>Create or configure this passive's logic-component on <paramref name="target"/> — the passive
    /// knows which components in the target's hierarchy to modify. Precondition: <see cref="CanApplyTo"/> returned
    /// true. Configure through an existing seam where one exists; only ADD a component when none does.</summary>
    public abstract void Apply(GameObject target);

    /// <summary>Apply to <paramref name="target"/> iff this passive can act on it. The convenience the loadout
    /// applies each owned passive with (at grant, or when a target spawns).</summary>
    public void TryApply(GameObject target)
    {
        if (target != null && CanApplyTo(target))
            Apply(target);
    }
}
