using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owner-contributed hit handler (component-handler shape, DESIGN.md migration thread): while its host passive
/// is equipped, each of the owner's projectiles that strikes a pawn STEALS one passive from it —
/// remove-then-grant across the two rosters, the ability↔ability interaction the essence names. The steal moves
/// the KIND, not the instance: the victim's instance despawns and the attacker is granted a fresh instance of
/// the same type.
///
/// Authored on a passive-ability INSTANCE prefab beside <see cref="PassiveAbility"/> and harvested at the hit
/// site by <see cref="HitResolver"/> (after the projectile's own handlers) — presence is the wiring; no list,
/// no courier. Runs server-only by construction (handler dispatch happens inside server-side hit resolution),
/// which is also which-peer-simulates correct: both rosters are server-authoritative state.
/// </summary>
public sealed class PassiveStealOnHit : MonoBehaviour, IHitHandler
{
    [Tooltip("Which hit channels this KIND steals on — per-prefab-copy authoring (the LayerMask idiom), " +
             "intersected with SupportedChannels and the current channel at dispatch.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    // A steal needs a struck pawn with a passive roster: Player is the only channel this behaviour CAN serve.
    public HitChannels SupportedChannels => HitChannels.Player;
    public HitChannels EnabledChannels => enabledChannels;

    // Scratch for the victim-roster read (server-only, single-threaded — same discipline as HitResolver's).
    private static readonly List<PassiveAbility> _victimPassives = new();

    public void OnHit(in HitContext context)
    {
        var victimRoster = context.Victim != null
            ? context.Victim.GetComponentInParent<PawnPassiveAbilities>() : null;
        if (victimRoster == null)
            return;

        var attackerPawn = context.Instigator != null ? context.Instigator.ControlledPawn.Value : null;
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
}
