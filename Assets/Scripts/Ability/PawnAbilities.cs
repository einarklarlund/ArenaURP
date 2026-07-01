using System.Collections.Generic;
using Arena.Ability;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>One active-ability slot: which ability, and how many uses are left before it's removed.</summary>
[System.Serializable]
public struct ActiveSlot
{
    public AbilityType Type;
    public int UsesRemaining;
}

/// <summary>
/// Server-authoritative ability inventory for a pawn. Holds an unbounded list of PASSIVE abilities
/// (each contributing modifiers to <see cref="PawnMovement"/>/<see cref="PawnWeapon"/> while equipped)
/// and up to <see cref="MaxActives"/> ACTIVE abilities with limited uses — cycled with Q/E and fired
/// with F.
///
/// Thin shell over pure helpers (functional-core / imperative-shell): the branchy inventory math lives in
/// <see cref="AbilityInventoryLogic"/> (EditMode-tested) and ability behaviour is the shared
/// <see cref="IEffect{TContext}"/> commands — the SAME commands projectile hit reactions run, authored as
/// data on the ability type — invoked here against an <see cref="AbilityAim"/> context built from the caster's
/// aim. Network identity (which abilities) rides as <see cref="AbilityDatabase"/> indices via
/// <see cref="AbilityTypeSerializer"/>.
/// </summary>
public sealed class PawnAbilities : NetworkBehaviour, IProjectileEffectProvider
{
    public const int MaxActives = 5;

    [Header("Wiring")]
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInput input;
    [SerializeField] private PawnMovement movement;
    [SerializeField] private PawnWeapon weapon;
    [Tooltip("Source of aim origin/direction for active abilities (e.g. the camera or fire point). Falls back to this transform.")]
    [SerializeField] private Transform aimSource;

    // ---- synced, server-authoritative ----
    public readonly SyncList<AbilityType> Passives = new();
    public readonly SyncList<ActiveSlot> Actives = new();
    public readonly SyncVar<int> SelectedIndex = new();

    // ---- local (per-peer) state ----
    private readonly List<MovementModifier> _appliedMove = new();
    private readonly List<WeaponModifier> _appliedWeapon = new();
    // Owner-contributed projectile operations, aggregated from equipped passives' authored lists. Read
    // server-side by the projectile's ServerCollision via IProjectileEffectProvider — never payloaded.
    private readonly List<IEffect<ProjectileSurfaceCollision>> _ownerSurface = new();
    private readonly List<IEffect<ProjectilePlayerCollision>> _ownerPlayer = new();
    private readonly List<IEffect<ProjectileProjectileCollision>> _ownerProjectile = new();

    private bool _prevCycleLeft, _prevCycleRight, _prevUse;
    private float _cooldownEndTime; // server-side gate

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Passives.OnChange += OnPassivesChanged;
        ReapplyPassiveModifiers(); // host/server + immediate state
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        Passives.OnChange -= OnPassivesChanged;
        ClearAppliedModifiers(); // don't leak modifiers when a pooled pawn is recycled
    }

    private void Update()
    {
        if (!IsOwner || input == null || !input.IsActive) return;

        var d = input.Data;
        // Edge-detect so a held button fires once regardless of whether the provider sends a held or a
        // one-frame signal (humans send held; bots could hold their intent).
        if (d.CycleAbilityPrev && !_prevCycleLeft) ServerCycleSelected(-1);
        if (d.CycleAbilityNext && !_prevCycleRight) ServerCycleSelected(+1);
        if (d.UseAbility && !_prevUse) ServerUseSelected();
        _prevCycleLeft = d.CycleAbilityPrev;
        _prevCycleRight = d.CycleAbilityNext;
        _prevUse = d.UseAbility;
    }

    // ---- server: acquisition (called by pickups) ----

    [Server]
    public void AddPassive(PassiveAbilityType ability)
    {
        if (ability == null) return;
        Passives.Add(ability);
    }

    /// <summary>Add an active ability. Returns false if it's null or the 5 slots are full.</summary>
    [Server]
    public bool AddActive(ActiveAbilityType ability)
    {
        if (ability == null) return false;
        if (Actives.Count >= MaxActives) return false;
        Actives.Add(new ActiveSlot { Type = ability, UsesRemaining = Mathf.Max(1, ability.Uses) });
        return true;
    }

    // ---- client intent -> server ----

    [ServerRpc]
    private void ServerCycleSelected(int dir)
    {
        SelectedIndex.Value = AbilityInventoryLogic.CycleIndex(SelectedIndex.Value, Actives.Count, dir);
    }

    [ServerRpc]
    private void ServerUseSelected() => ServerUse();

    /// <summary>
    /// Server-side use of the currently selected active ability: run its authored operations against the
    /// caster's aim context and consume a use. Public + <c>[Server]</c> so the editor PlayMode test rig can
    /// invoke the real path directly (calling a FishNet <c>[ServerRpc]</c> from a test is awkward).
    /// </summary>
    [Server]
    public void ServerUse()
    {
        int i = SelectedIndex.Value;
        if (i < 0 || i >= Actives.Count) return;
        if (Time.time < _cooldownEndTime) return;

        ActiveSlot slot = Actives[i];
        if (slot.Type is not ActiveAbilityType active) return;

        // Run the ability's authored operations against the caster's aim — the SAME IEffect commands a
        // projectile hit reaction runs, just invoked from an ability against an AbilityAim context.
        AbilityAim ctx = BuildContext();
        var ops = active.OnUse;
        if (ops != null)
            for (int e = 0; e < ops.Count; e++)
                ops[e]?.Apply(ctx);

        _cooldownEndTime = Time.time + Mathf.Max(0f, active.Cooldown);

        // Consume one use; remove the slot when exhausted and keep the selection valid.
        var use = AbilityInventoryLogic.UseSlot(slot.UsesRemaining);
        if (use.Removed)
        {
            Actives.RemoveAt(i);
            SelectedIndex.Value = AbilityInventoryLogic.ClampSelection(SelectedIndex.Value, Actives.Count);
        }
        else
        {
            slot.UsesRemaining = use.UsesRemaining;
            Actives[i] = slot;
        }
    }

    private AbilityAim BuildContext()
    {
        Transform src = aimSource != null ? aimSource : transform;
        // Owner = the caster's pawn; an aimed ability has only an origin + direction (no target/normal),
        // which the AbilityAim typing enforces on the operations authored here. The caster's player id, when
        // an operation needs it, is read off OwnerPlayer.ObjectId.
        return new AbilityAim(
            owner: pawn != null ? pawn.gameObject : gameObject,
            ownerPlayer: pawn != null ? pawn.ControllingPlayer.Value : null,
            point: src.position,
            direction: src.forward);
    }

    // ---- passive modifiers: rebuilt on every peer whenever the synced list changes ----

    private void OnPassivesChanged(SyncListOperation op, int index, AbilityType oldItem, AbilityType newItem, bool asServer)
        => ReapplyPassiveModifiers();

    /// <summary>
    /// Remove our previous contributions, then push one modifier per current passive into the pawn's
    /// movement/weapon modifier lists — the same Remove-old / Add-new discipline PawnTemperature uses — and
    /// re-aggregate the passives' authored projectile operations. Runs on every peer; only the peer that
    /// actually simulates movement/weapon consumes the modifiers, and only the server reads the operations.
    /// </summary>
    private void ReapplyPassiveModifiers()
    {
        ClearAppliedModifiers();
        _ownerSurface.Clear();
        _ownerPlayer.Clear();
        _ownerProjectile.Clear();
        foreach (var item in Passives)
        {
            if (item is not PassiveAbilityType p) continue;
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
