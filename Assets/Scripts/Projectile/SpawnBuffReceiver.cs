using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// On EVERY peer, at spawn, applies the firer's passive abilities' on-spawn buffs to this spawned object. Generic:
/// it reads the sibling <see cref="NetPlayerOwnership"/> and knows nothing about projectiles, so it works on any
/// owned spawnable (projectiles, ability children).
///
/// Reuses three existing mechanisms and adds NOTHING to the wire: the owner id already rides the spawn payload
/// (<see cref="NetPlayerOwnership.SpawnOwnerId"/>), the owner's passives are replicated child NetworkObjects whose
/// identity resolves locally, and every peer runs this the same way. Because FishNet's ReadPayload precedes ALL
/// OnStartNetwork callbacks, <c>SpawnOwnerId</c> is always available here regardless of component order.
///
/// Buffs apply ON-SPAWN ONLY — a per-peer snapshot of the owner's passives as resolved on this peer at the spawn
/// instant. No mid-flight updates, no removal, no re-sync (matching passives already having no removal path). The
/// null paths (unowned spawn, owner despawned, passive-less pawn, owner not yet in the replicated roster) are
/// expected transients, so they are QUIET no-ops — not errors.
/// </summary>
[RequireComponent(typeof(NetPlayerOwnership))]
public sealed class SpawnBuffReceiver : NetworkBehaviour
{
    private NetPlayerOwnership _ownership;
    // Scratch buffer for the per-peer passive read (avoid per-spawn allocation).
    private readonly List<IEffect<OnSpawnContext>> _buf = new();

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
        Pawn pawn = firer != null ? firer.ControlledPawn.Value : null;
        if (pawn == null)
            return;                                         // owner despawned / not yet replicated on this peer

        if (!pawn.TryGetComponent<PawnPassiveAbilities>(out var passives))
            return;

        passives.GetSpawnBuffs(_buf);                       // fresh per-peer read of the owner's passives
        if (_buf.Count == 0)
            return;

        var ctx = new OnSpawnContext(gameObject, firer);
        for (int i = 0; i < _buf.Count; i++)
            _buf[i]?.Apply(ctx);                            // effects poke generic sibling components
    }

    public override void ResetState(bool asServer)
    {
        base.ResetState(asServer);
        // Holds no per-spawn state beyond the scratch list, but clear it anyway so the invariant "every component
        // in the buff path resets its per-spawn layer" stays true if this ever grows state.
        _buf.Clear();
    }
}
