using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The passive-steal controller (ADR 0005): when a projectile carrying it strikes a pawn, it STEALS one passive
/// from the victim — remove-then-grant across the two rosters, the ability↔ability interaction the essence names.
/// The steal moves the KIND, not the instance: the victim's instance despawns and the attacker is granted a fresh
/// instance of the same type. A controller OWNS STATE (only the <see cref="enabledChannels"/> it reacts on) and
/// exposes the steal as its behaviour; both rosters are resolved from the hit context, never held.
///
/// It is an OWNER contribution: authored on a passive-ability INSTANCE prefab beside <see cref="PassiveAbility"/>,
/// it is an <see cref="IOwnedObjectContribution"/> that <see cref="SpawnContributionReceiver"/> self-clones onto each object
/// the owner spawns. By hit time the clone is an ordinary handler on the projectile source, dispatched the one
/// <see cref="HitMessage"/> — no list, no courier, and dispatch stays ignorant of the ability system. Runs
/// server-only by construction (handler dispatch happens inside server-side hit resolution), which is also
/// which-peer-simulates correct: both rosters are server-authoritative state.
/// </summary>
public sealed class PassiveStealController : MonoBehaviour, IHitMessageHandler, IOwnedObjectContribution
{
    [Tooltip("Which hit channels this KIND steals on — per-prefab-copy authoring (the LayerMask idiom), " +
             "intersected with the current channel at dispatch.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    // A steal needs a struck pawn with a passive roster; it self-guards on the victim's roster below, so a
    // non-Player channel (no roster on the struck object) simply no-ops.
    public HitChannels EnabledHitChannels => enabledChannels;

    // Scratch for the victim-roster read (server-only, single-threaded — same discipline the hit site relies on).
    private static readonly List<PassiveAbility> _victimPassives = new();

    public void OnHit(HitMessage hit)
    {
        var victimRoster = hit.Victim != null
            ? hit.Victim.GetComponentInParent<PawnPassiveAbilities>() : null;
        if (victimRoster == null)
            return;

        var attackerPawn = hit.Instigator != null ? hit.Instigator.ControlledPawn.Value : null;
        PawnPassiveAbilities attackerRoster = null;
        if (attackerPawn != null)
            attackerPawn.TryGetComponent(out attackerRoster);
        if (attackerRoster == null || attackerRoster == victimRoster)
            return;

        victimRoster.GetPassives(_victimPassives);
        foreach (var candidate in _victimPassives)
        {
            // Resolve the KIND before despawning — the TypeObjectRoot back-ref dies with the instance. An
            // instance with no baked type, or one a concurrent hit already removed, is skipped, not stolen.
            if (candidate.Type is not PassiveAbilityType kind)
                continue;
            if (!victimRoster.RemovePassive(candidate))
                continue;
            attackerRoster.AddPassive(kind);
            return; // one steal per hit
        }
    }

    /// <summary>Owner contribution (self-cloning): add a steal reaction onto <paramref name="target"/> unless it
    /// already has one, copying this instance's channels (its only state). Returns the added component, or null
    /// when one was already present (so pooled-reuse teardown never strips a handler the target was authored
    /// with).</summary>
    public Component Apply(GameObject target)
    {
        if (target.TryGetComponent<PassiveStealController>(out _))
            return null;
        var clone = target.AddComponent<PassiveStealController>();
        clone.enabledChannels = enabledChannels;
        return clone;
    }
}
