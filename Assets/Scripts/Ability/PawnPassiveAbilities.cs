using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Server-authoritative PASSIVE-ability inventory facade for a pawn (unbounded). Each granted passive is a
/// spawned <see cref="PassiveAbility"/> instance (its type's prefab) parented under this pawn's
/// NetworkObject; FishNet replicates the instances, so every peer enumerates the same children and re-derives
/// the same modifiers locally.
///
/// This component folds each equipped passive's multipliers into the pawn's <see cref="PawnMovement"/> /
/// <see cref="PawnWeapon"/> modifier lists (Remove-old / Add-new, the PawnTemperature discipline) and
/// aggregates the passives' authored projectile operations for the owner-contribution path
/// (<see cref="IProjectileEffectProvider"/> — read server-side by ServerCollision, never payloaded).
/// Instances call <see cref="NotifyInventoryChanged"/> from their network start/stop. Active abilities live
/// on the sibling <see cref="PawnActiveAbilities"/>.
/// </summary>
public sealed class PawnPassiveAbilities : NetworkBehaviour, IProjectileEffectProvider
{
    [Header("Wiring")]
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnWeapon weapon;

    /// <summary>Raised on any peer whenever the child passive instances change.</summary>
    public event System.Action InventoryChanged;

    // ---- local (per-peer) state ----
    private readonly List<MovementModifier> _appliedMove = new();
    private readonly List<WeaponModifier> _appliedWeapon = new();
    // Owner-contributed projectile operations, aggregated from equipped passives' authored lists.
    private readonly List<IEffect<ProjectileSurfaceCollision>> _ownerSurface = new();
    private readonly List<IEffect<ProjectilePlayerCollision>> _ownerPlayer = new();
    private readonly List<IEffect<ProjectileProjectileCollision>> _ownerProjectile = new();

    // Scratch buffer for child enumeration (avoid per-call allocation).
    private readonly List<PassiveAbility> _passivesBuf = new();

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        NotifyInventoryChanged(); // fold in any already-replicated children (late join / pooled reuse)
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        ClearAppliedModifiers(); // don't leak modifiers when a pooled pawn is recycled
    }

    // ---- inventory enumeration (children of this pawn's NetworkObject) ----

    /// <summary>Fill <paramref name="results"/> with the passive-ability instances under this pawn.</summary>
    public void GetPassives(List<PassiveAbility> results)
    {
        results.Clear();
        GetComponentsInChildren(includeInactive: true, results);
    }

    public int PassivesCount { get { GetPassives(_passivesBuf); return _passivesBuf.Count; } }

    /// <summary>
    /// Fill <paramref name="results"/> with the on-spawn buffs contributed by this pawn's current passives, for
    /// the on-spawn buff path (<c>SpawnBuffReceiver</c>). This is a FRESH PER-PEER read off the replicated child
    /// passives — it runs identically on observers, and is deliberately NOT routed through the server-only owner
    /// aggregation (<see cref="_ownerSurface"/> et al.), which only the server ever reads.
    /// </summary>
    public void GetSpawnBuffs(List<IEffect<OnSpawnContext>> results)
    {
        results.Clear();
        GetPassives(_passivesBuf);                 // child enumeration, replicated on every peer
        foreach (var p in _passivesBuf)
            if (p.OnSpawn != null) results.AddRange(p.OnSpawn);
    }

    // ---- server: acquisition (called by pickups) ----

    [Server]
    public void AddPassive(PassiveAbilityType ability) => AbilityGrant.Spawn(this, ability);
    // Membership is derived from the replicated children directly (passives have no individual-removal
    // path, so child enumeration alone is a sufficient source of truth — unlike actives, which keep a
    // SyncList roster for ordering + removal hooks).

    // ---- change propagation: passive instances call this when they start/stop ----

    /// <summary>
    /// Re-derive everything that depends on the current child instances (modifiers, owner projectile
    /// operations) and raise <see cref="InventoryChanged"/>.
    /// </summary>
    public void NotifyInventoryChanged()
    {
        ReapplyPassiveModifiers();
        InventoryChanged?.Invoke();
    }

    /// <summary>
    /// Remove our previous contributions, then push one modifier per current passive instance into the pawn's
    /// movement/weapon modifier lists, and re-aggregate the passives' authored projectile operations. Runs on
    /// every peer; only the peer that actually simulates movement/weapon consumes the modifiers, and only the
    /// server reads the operations.
    /// </summary>
    private void ReapplyPassiveModifiers()
    {
        ClearAppliedModifiers();
        _ownerSurface.Clear();
        _ownerPlayer.Clear();
        _ownerProjectile.Clear();
        GetPassives(_passivesBuf);
        foreach (var p in _passivesBuf)
        {
            if (movement != null)
            {
                MovementModifier m = p.ToMovementModifier();
                movement.Modifiers.Add(m);
                _appliedMove.Add(m);
            }
            if (weapon != null)
            {
                WeaponModifier w = p.ToWeaponModifier();
                weapon.Modifiers.Add(w);
                _appliedWeapon.Add(w);
            }
            // Aggregate the passive's authored projectile operations so the owner's projectiles can find them.
            if (p.ProjectileOnSurface != null) _ownerSurface.AddRange(p.ProjectileOnSurface);
            if (p.ProjectileOnPlayer != null) _ownerPlayer.AddRange(p.ProjectileOnPlayer);
            if (p.ProjectileOnProjectile != null) _ownerProjectile.AddRange(p.ProjectileOnProjectile);
        }
    }

    /// <summary>
    /// <see cref="IProjectileEffectProvider"/>: expose the owner's aggregated projectile operations as a
    /// per-category bundle. Read SERVER-SIDE during collision resolution — looked up from the live owner,
    /// never serialized onto the projectile. ServerCollision decides which category applies to a given hit.
    /// </summary>
    public ProjectileEffects GetProjectileEffects() => new(_ownerSurface, _ownerPlayer, _ownerProjectile);

    private void ClearAppliedModifiers()
    {
        if (movement != null)
            foreach (var m in _appliedMove) movement.Modifiers.Remove(m);
        if (weapon != null)
            foreach (var w in _appliedWeapon) weapon.Modifiers.Remove(w);
        _appliedMove.Clear();
        _appliedWeapon.Clear();
    }
}
