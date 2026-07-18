using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// On EVERY peer, at spawn, applies what the firer's passive abilities contribute to this spawned object: the
/// authored on-spawn STAT buffs (speed/scale/health) AND the on-hit HANDLER contributions (each passive's
/// <see cref="IOwnedObjectContribution"/> self-clones its reaction onto this object). Generic: it reads the sibling
/// <see cref="NetPlayerOwnership"/> and knows nothing about projectiles, so it works on any owned spawnable
/// (projectiles, ability children).
///
/// Reuses three existing mechanisms and adds NOTHING to the wire: the owner id already rides the spawn payload
/// (<see cref="NetPlayerOwnership.SpawnOwnerId"/>), the owner's passives are replicated child NetworkObjects whose
/// identity resolves locally, and every peer runs this the same way. Because FishNet's ReadPayload precedes ALL
/// OnStartNetwork callbacks, <c>SpawnOwnerId</c> is always available here regardless of component order. Running
/// on every peer keeps the object's component set identical everywhere; only the server ever dispatches hits, so
/// the contributed handlers sit inert on observers.
///
/// Applies ON-SPAWN ONLY — a per-peer snapshot of the owner's passives as resolved on this peer at the spawn
/// instant. The contributed handlers are the one thing that must be UNDONE: a pooled object can be reused by a
/// different owner, so the handlers WE added (tracked in <see cref="_contributed"/>) are destroyed in
/// <see cref="OnStopNetwork"/> — a prefab-authored handler is never touched (Apply returns null for those). The
/// null paths (unowned spawn, owner despawned, passive-less pawn, owner not yet in the replicated roster) are
/// expected transients, so they are QUIET no-ops — not errors.
/// </summary>
[RequireComponent(typeof(NetPlayerOwnership))]
public sealed class SpawnContributionReceiver : NetworkBehaviour
{
    private NetPlayerOwnership _ownership;

    // Scratch for the per-spawn contribution enumeration (server-and-client, single-threaded at spawn).
    private readonly List<PassiveAbility> _passivesBuf = new();
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
        Pawn pawn = firer != null ? firer.ControlledPawn.Value : null;
        if (pawn == null)
            return;                                         // owner despawned / not yet replicated on this peer

        if (!pawn.TryGetComponent<PawnPassiveAbilities>(out var passives))
            return;

        // Fold the owner's passives' authored SpawnBuffs (product; identity is a no-op) and poke each spawned-object
        // system through its dedicated per-spawn multiplier seam — never a raw scale/health write. A buff channel is
        // applied only when it differs from identity, so a spawnable carrying only some of the siblings is buffed on
        // whichever it has and the others are left at their reset identity.
        SpawnModifier buff = passives.FoldSpawnBuffs();

        if (buff.SpeedMultiplier != 1f && TryGetComponent<NetworkedMotion>(out var motion))
            motion.SetSpeedMultiplier(buff.SpeedMultiplier);

        if (buff.ScaleMultiplier != 1f)
        {
            if (TryGetComponent<SpawnScale>(out var scale))          // visual (per-peer cosmetic)
                scale.SetScaleMultiplier(buff.ScaleMultiplier);
            if (TryGetComponent<ServerCollision>(out var collision)) // collision radius (server-only)
                collision.SetRadiusMultiplier(buff.ScaleMultiplier);
        }

        if (buff.MaxHealthMultiplier != 1f && TryGetComponent<HealthController>(out var health))
            health.SetMaxHealthMultiplier(buff.MaxHealthMultiplier);

        // Handler-side of the same on-spawn model: each passive's IOwnedObjectContribution self-clones its on-hit
        // reaction onto this object (idempotently). By hit time these are ordinary handlers on the source, so
        // HitMessage.Dispatch reaches them without knowing anything about passives. Track what we ADD (Apply
        // returns null for a handler already present) so OnStopNetwork can remove exactly it on pooled reuse.
        passives.GetPassives(_passivesBuf);
        for (int i = 0; i < _passivesBuf.Count; i++)
        {
            _passivesBuf[i].GetComponents(_contribBuf); // GetComponents(List) clears the buffer itself
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
