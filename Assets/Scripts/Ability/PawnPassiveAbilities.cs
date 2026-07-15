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
/// (<see cref="GetProjectileEffects"/> — read server-side by ServerCollision, never payloaded).
/// Instances call <see cref="NotifyInventoryChanged"/> from their network start/stop. Active abilities live
/// on the sibling <see cref="PawnActiveAbilities"/>.
/// </summary>
public sealed class PawnPassiveAbilities : NetworkBehaviour
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
    private readonly List<IEffect<ProjectileImpact>> _ownerSurface = new();
    private readonly List<IEffect<ProjectileImpact>> _ownerPlayer = new();
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

    /// <summary>Fill <paramref name="results"/> with the LIVE passive-ability instances under this pawn.
    /// An individually-removed instance is destroyed at END of frame, so its corpse stays enumerable as a
    /// child for the rest of the frame — filter on FishNet's own spawn state so it contributes no modifiers,
    /// spawn buffs, hit handlers, or count. (IsSpawned is true throughout OnStart callbacks — InitializeEarly
    /// runs first — so a starting instance is never filtered.)</summary>
    public void GetPassives(List<PassiveAbility> results)
    {
        results.Clear();
        GetComponentsInChildren(includeInactive: true, results);
        results.RemoveAll(static p => !p.IsSpawned);
    }

    public int PassivesCount { get { GetPassives(_passivesBuf); return _passivesBuf.Count; } }

    /// <summary>
    /// The combined on-spawn buff this pawn's current passives contribute — each passive's authored
    /// <see cref="PassiveAbility.SpawnBuff"/> MULTIPLIED together (identity = 1 contributes nothing), for the
    /// on-spawn buff path (<c>SpawnBuffReceiver</c>). A FRESH PER-PEER read off the replicated child passives —
    /// it runs identically on observers, and is deliberately NOT routed through the server-only owner
    /// contributions, which only the server ever reads.
    /// </summary>
    public SpawnModifier FoldSpawnBuffs()
    {
        var folded = SpawnModifier.Identity;
        GetPassives(_passivesBuf);                 // child enumeration, replicated on every peer
        foreach (var p in _passivesBuf)
        {
            folded.SpeedMultiplier *= p.SpawnBuff.SpeedMultiplier;
            folded.ScaleMultiplier *= p.SpawnBuff.ScaleMultiplier;
            folded.MaxHealthMultiplier *= p.SpawnBuff.MaxHealthMultiplier;
        }
        return folded;
    }

    /// <summary>
    /// Append the <see cref="IHitHandler"/> components authored on this pawn's passive instance prefabs — the
    /// owner-contribution harvest of handler dispatch (component-handler shape, DESIGN.md migration thread).
    /// Read SERVER-SIDE by <see cref="HitResolver"/> at hit time, after the projectile's own handlers; presence
    /// on the instance IS the contribution, so there is no serialized list and nothing to re-derive.
    /// </summary>
    public void CollectHitHandlers(List<IHitHandler> results)
    {
        GetPassives(_passivesBuf);
        foreach (var p in _passivesBuf)
        {
            p.GetComponents(_handlerBuf); // GetComponents(List) clears the buffer itself
            for (int i = 0; i < _handlerBuf.Count; i++)
                results.Add(_handlerBuf[i]);
        }
    }

    private readonly List<IHitHandler> _handlerBuf = new();

    // ---- server: acquisition / removal ----

    [Server]
    public void AddPassive(PassiveAbilityType ability) => AbilityGrant.Spawn(this, ability);
    // Membership is derived from the replicated children directly: child enumeration alone is a sufficient
    // source of truth — unlike actives, which keep a SyncList roster for ordering. Removal is therefore just
    // despawning the child instance (below); its OnStopNetwork re-derives every fold.

    /// <summary>
    /// Server: remove ONE granted passive by despawning its instance. Returns false when the instance is not a
    /// live child of this pawn (already stolen/despawned — a hit resolving twice must not double-remove).
    /// Callers that move the KIND elsewhere (a steal) must resolve <see cref="PassiveAbility.Type"/> BEFORE
    /// calling this: the TypeObjectRoot back-ref dies with the instance.
    /// </summary>
    [Server]
    public bool RemovePassive(PassiveAbility instance)
    {
        if (instance == null || !instance.IsSpawned)
            return false;
        if (instance.GetComponentInParent<PawnPassiveAbilities>() != this)
            return false;
        instance.Despawn(); // OnStopNetwork → NotifyInventoryChanged re-derives modifiers + owner contributions
        return true;
    }

    // ---- change propagation: passive instances call this when they start/stop ----

    /// <summary>
    /// Re-derive everything that depends on the current child instances (modifiers, owner projectile
    /// operations) and raise <see cref="InventoryChanged"/>. <paramref name="leaving"/> is the instance whose
    /// network-stop triggered this notify, excluded from the re-derive: FishNet flips IsSpawned only AFTER the
    /// stop callbacks, so inside that window the spawn-state filter alone would still see the corpse
    /// (removal-first — the RemoveAt-before-Despawn discipline, keeping derived state off dying objects).
    /// </summary>
    public void NotifyInventoryChanged(PassiveAbility leaving = null)
    {
        ReapplyPassiveModifiers(leaving);
        InventoryChanged?.Invoke();
    }

    /// <summary>
    /// Remove our previous contributions, then collect each current passive's authored stat-buff modifiers
    /// (identity entries contribute nothing and are skipped) and apply them to the pawn's movement/weapon
    /// modifier lists, and re-aggregate the passives' authored projectile operations. Runs on every peer;
    /// only the peer that actually simulates movement/weapon consumes the modifiers, and only the server
    /// reads the operations.
    /// </summary>
    private void ReapplyPassiveModifiers(PassiveAbility leaving = null)
    {
        ClearAppliedModifiers(); // removes previous contributions from the live lists and clears the tracking lists
        _ownerSurface.Clear();
        _ownerPlayer.Clear();
        _ownerProjectile.Clear();

        GetPassives(_passivesBuf);
        foreach (var p in _passivesBuf)
        {
            if (p == leaving)
                continue;
            if (!p.MovementBuff.Equals(MovementModifier.Identity)) _appliedMove.Add(p.MovementBuff);
            if (!p.WeaponBuff.Equals(WeaponModifier.Identity)) _appliedWeapon.Add(p.WeaponBuff);
            // Aggregate the passive's authored projectile operations so the owner's projectiles can find them.
            if (p.ProjectileOnSurface != null) _ownerSurface.AddRange(p.ProjectileOnSurface);
            if (p.ProjectileOnPlayer != null) _ownerPlayer.AddRange(p.ProjectileOnPlayer);
            if (p.ProjectileOnProjectile != null) _ownerProjectile.AddRange(p.ProjectileOnProjectile);
        }

        // Push the contributed modifiers onto the pawn's live modifier lists (ClearAppliedModifiers removes them).
        // A null ref here is valid ONLY for pawns without that system (minimal test pawns) — dropping an actual
        // contribution on a full pawn is a wiring bug (it hid TestCombatant's unwired refs), so it at least
        // warns; the steal test's fold assertion is the hard regression guard.
        if (movement != null)
            foreach (var m in _appliedMove) movement.Modifiers.Add(m);
        else if (_appliedMove.Count > 0)
            Debug.LogWarning($"{name}: dropping {_appliedMove.Count} movement-buff contribution(s) — no movement ref wired", this);
        if (weapon != null)
            foreach (var w in _appliedWeapon) weapon.Modifiers.Add(w);
        else if (_appliedWeapon.Count > 0)
            Debug.LogWarning($"{name}: dropping {_appliedWeapon.Count} weapon-buff contribution(s) — no weapon ref wired", this);
    }

    /// <summary>
    /// The owner's aggregated projectile operations as a per-category bundle. Read SERVER-SIDE during
    /// collision resolution — looked up from the live owner, never serialized onto the projectile.
    /// ServerCollision decides which category applies to a given hit.
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
