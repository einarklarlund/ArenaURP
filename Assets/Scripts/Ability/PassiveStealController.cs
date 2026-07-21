using UnityEngine;

/// <summary>
/// The passive-steal controller: when a projectile carrying it strikes a pawn, it STEALS one passive KIND from the
/// victim to the attacker — the ability↔ability interaction the essence names. In the shared-registry model this is
/// a pure REFERENCE MOVE across two <see cref="PlayerPassiveLoadout"/>s: remove the shared registry-child ref from
/// the victim's <see cref="PlayerPassiveLoadout.Owned"/> and grant it to the attacker's — no despawn, no spawn, no
/// per-player instance. Server-authoritative (handler dispatch happens inside server-side hit resolution), which is
/// also which-peer-simulates correct: both loadouts are server-authoritative <see cref="FishNet.Object.Synchronizing.SyncList{T}"/>s.
/// A controller OWNS STATE (only the <see cref="enabledChannels"/> it reacts on); both loadouts are resolved from
/// the hit context, never held.
///
/// It is an OWNER contribution: authored on a passive-KIND child of the shared registry beside its
/// <see cref="PassiveInfo"/>, it is an <see cref="IOwnedObjectContribution"/> that <see cref="SpawnContributionReceiver"/>
/// self-clones onto each object the owner spawns. By hit time the clone is an ordinary handler on the projectile
/// source, dispatched the one <see cref="HitMessage"/> — no list, no courier, and dispatch stays ignorant of the
/// ability system.
/// </summary>
public sealed class PassiveStealController : MonoBehaviour, IHitMessageHandler, IOwnedObjectContribution
{
    [Tooltip("Which hit channels this KIND steals on — per-kind authoring (the LayerMask idiom), " +
             "intersected with the current channel at dispatch.")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    // A steal needs a struck pawn with a loadout; it self-guards on the victim's loadout below, so a non-Player
    // channel (no pawn/loadout on the struck object) simply no-ops.
    public HitChannels EnabledHitChannels => enabledChannels;

    public void OnHit(HitMessage hit)
    {
        PlayerPassiveLoadout attackerLoadout = hit.Instigator != null ? hit.Instigator.PassiveLoadout : null;
        if (attackerLoadout == null)
            return;

        Pawn victimPawn = hit.Victim != null ? hit.Victim.GetComponentInParent<Pawn>() : null;
        NetworkPlayer victimPlayer = victimPawn != null ? victimPawn.ControllingPlayer.Value : null;
        PlayerPassiveLoadout victimLoadout = victimPlayer != null ? victimPlayer.PassiveLoadout : null;
        if (victimLoadout == null || victimLoadout == attackerLoadout)
            return;

        // Move the first owned KIND the attacker does not already hold — a shared registry-child ref, so removing it
        // from the victim and granting it to the attacker is one SyncList transfer. Grant de-dups, so skipping kinds
        // the attacker already owns keeps this a genuine transfer (a real loss for the victim, a real gain for the
        // attacker) rather than a one-sided removal.
        for (int i = 0; i < victimLoadout.Owned.Count; i++)
        {
            var kind = victimLoadout.Owned[i];
            if (kind == null || attackerLoadout.Owned.Contains(kind))
                continue;
            if (!victimLoadout.Remove(kind)) // false if a concurrent hit already removed it
                continue;
            attackerLoadout.Grant(kind);
            return; // one steal per hit
        }
    }

    /// <summary>Owner contribution (self-cloning): add a steal reaction onto <paramref name="target"/> unless it
    /// already has one, copying this KIND's channels (its only state). Returns the added component, or null when one
    /// was already present (so pooled-reuse teardown never strips a handler the target was authored with).</summary>
    public Component Apply(GameObject target)
    {
        if (target.TryGetComponent<PassiveStealController>(out _))
            return null;
        var clone = target.AddComponent<PassiveStealController>();
        clone.enabledChannels = enabledChannels;
        return clone;
    }
}
