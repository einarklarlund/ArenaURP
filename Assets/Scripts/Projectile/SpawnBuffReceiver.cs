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
    }
}
