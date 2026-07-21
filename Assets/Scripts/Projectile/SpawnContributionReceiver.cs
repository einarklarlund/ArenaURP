using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// On EVERY peer, at spawn, applies what the firer's owned passives contribute to this spawned object: the
/// component-coupled buff passives (speed/scale/radius/max-health — each a <see cref="Passive"/> that finds its one
/// component on this object) AND the on-hit HANDLER contributions (each owned kind's <see cref="IOwnedObjectContribution"/>
/// self-clones its reaction onto this object). Generic: it reads the sibling <see cref="NetPlayerOwnership"/> and
/// knows nothing about projectiles, so it works on any owned spawnable (projectiles, ability children).
///
/// The source is the firer's <see cref="PlayerPassiveLoadout"/> — a <see cref="SyncList{T}"/> of REFERENCES to the
/// shared registry-child KINDS, resolved locally on every peer. Nothing extra rides the wire: the owner id already
/// rides the spawn payload (<see cref="NetPlayerOwnership.SpawnOwnerId"/>), the loadout replicates, and every peer
/// runs this the same way. Because FishNet's ReadPayload precedes ALL OnStartNetwork callbacks, <c>SpawnOwnerId</c>
/// is always available here regardless of component order.
///
/// Applies ON-SPAWN ONLY — a per-peer snapshot of the owner's loadout as resolved on this peer at the spawn instant.
/// The BUFF passives configure idempotent per-spawn multiplier seams that reset in ResetState, so they are pool-safe
/// for free. The CONTRIBUTED HANDLERS are the one thing that must be UNDONE: a pooled object can be reused by a
/// different owner, so the handlers WE added (tracked in <see cref="_contributed"/>) are destroyed in
/// <see cref="OnStopNetwork"/> — a prefab-authored handler is never touched (Apply returns null for those). The null
/// paths (unowned spawn, owner despawned, empty loadout, owner not yet in the replicated roster) are expected
/// transients, so they are QUIET no-ops — not errors.
/// </summary>
[RequireComponent(typeof(NetPlayerOwnership))]
public sealed class SpawnContributionReceiver : NetworkBehaviour
{
    private NetPlayerOwnership _ownership;

    // Scratch for the per-spawn contribution enumeration (server-and-client, single-threaded at spawn).
    private readonly List<IOwnedObjectContribution> _contribBuf = new();
    // The handler components THIS receiver added at spawn — destroyed on despawn so a pooled reuse by another
    // owner starts clean. Only non-null Apply results land here (an already-present/authored handler is skipped).
    private readonly List<Component> _contributed = new();

    private void Awake() => _ownership = GetComponent<NetPlayerOwnership>();

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (_ownership == null)
            return;

        int id = _ownership.SpawnOwnerId;
        if (id < 0)
            return;                                         // unowned spawn

        NetworkPlayer firer = NetPlayerResolver.Resolve(id);
        PlayerPassiveLoadout loadout = firer != null ? firer.PassiveLoadout : null;
        if (loadout == null)
            return;                                         // owner despawned / not yet replicated / no loadout

        // Buff side: apply every owned buff passive that can act on this object through its one coupled component's
        // idempotent per-spawn multiplier seam (Movement/Weapon passives no-op here — no PawnMovement/PawnWeapon on
        // a spawnable). Composing multipliers, so several owned speed/scale/etc. passives multiply together.
        loadout.ApplyOwnedTo(gameObject);

        // Handler side: each owned kind's IOwnedObjectContribution self-clones its on-hit reaction onto this object
        // (idempotently). By hit time these are ordinary handlers on the source, so HitMessage.Dispatch reaches them
        // without knowing anything about passives. Track what we ADD (Apply returns null for a handler already
        // present) so OnStopNetwork can remove exactly it on pooled reuse.
        for (int i = 0; i < loadout.Owned.Count; i++)
        {
            NetworkObject kind = loadout.Owned[i];
            if (kind == null)
                continue;
            kind.GetComponents(_contribBuf); // GetComponents(List) clears the buffer itself
            for (int j = 0; j < _contribBuf.Count; j++)
            {
                Component added = _contribBuf[j].Apply(gameObject);
                if (added != null)
                    _contributed.Add(added);
            }
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        // Pooling correctness: this instance can be reused by a DIFFERENT owner next spawn, so tear down exactly
        // the contributed handlers we added (never a prefab-authored one). Destroy defers to end of frame, which
        // is before this instance's next OnStartNetwork, so the next owner's contributions apply onto a clean object.
        for (int i = 0; i < _contributed.Count; i++)
            if (_contributed[i] != null)
                Destroy(_contributed[i]);
        _contributed.Clear();
    }
}
