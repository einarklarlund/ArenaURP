using System.Collections.Generic;
using Arena.Ability;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Server-authoritative ACTIVE-ability inventory facade for a pawn: up to <see cref="MaxActives"/> triggered
/// abilities, cycled with Q/E and fired with F. Each granted ability is a spawned <see cref="ActiveAbility"/>
/// instance (its type's prefab) parented under this pawn's NetworkObject — the instances ARE the inventory's
/// substance (config + live state) — while <see cref="Actives"/>, a SyncList of references to those
/// instances, is the authoritative ROSTER: it fixes slot order and provides deterministic change hooks on
/// every peer (FishNet guarantees spawns are processed before any packet referencing them, so the entries
/// always resolve).
///
/// Server-side ordering is load-bearing: an instance is SPAWNED before it is added to the roster (an
/// unspawned reference serializes as permanent null), and REMOVED from the roster before it is despawned
/// (so no delta references a dead object, and the removal is what drives HUD/slot updates — independent of
/// FishNet's despawn-callback timing).
///
/// This component owns the pawn-side concerns: input edges, the selection SyncVar, the server-side use path
/// (cooldown gate, building the <see cref="AbilityAim"/> context, despawning exhausted instances), and the
/// <see cref="InventoryChanged"/> HUD hook. Passive abilities live on the sibling <see cref="PawnPassiveAbilities"/>.
/// </summary>
public sealed class PawnActiveAbilities : NetworkBehaviour
{
    public const int MaxActives = 5;

    [Header("Wiring")]
    [SerializeField] private Pawn pawn;
    [SerializeField] private PawnInput input;
    [Tooltip("Source of aim origin/direction for active abilities (e.g. the camera or fire point). Falls back to this transform.")]
    [SerializeField] private Transform aimSource;

    // ---- synced, server-authoritative ----

    /// <summary>The roster: references to the spawned ability instances, in slot order.</summary>
    public readonly SyncList<ActiveAbility> Actives = new();
    public readonly SyncVar<int> SelectedIndex = new();

    /// <summary>Raised on any peer whenever the roster (or an instance's uses) changes. HUD hook.</summary>
    public event System.Action InventoryChanged;

    private bool _prevCycleLeft, _prevCycleRight, _prevUse;
    private float _cooldownEndTime; // server-side gate

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Actives.OnChange += OnActivesChanged;
        NotifyInventoryChanged(); // surface any already-replicated roster (late join / pooled reuse)
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        Actives.OnChange -= OnActivesChanged;
    }

    private void OnActivesChanged(SyncListOperation op, int index, ActiveAbility oldItem, ActiveAbility newItem, bool asServer)
        => NotifyInventoryChanged();

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

    // ---- roster reads ----

    /// <summary>Fill <paramref name="results"/> with the roster's instances, in slot order (null entries skipped defensively).</summary>
    public void GetActives(List<ActiveAbility> results)
    {
        results.Clear();
        foreach (var a in Actives)
            if (a != null)
                results.Add(a);
    }

    public int ActivesCount => Actives.Count;

    // ---- server: acquisition (called by pickups) ----

    /// <summary>Add an active ability. Returns false if it's null, prefab-less, or the 5 slots are full.</summary>
    [Server]
    public bool AddActive(ActiveAbilityType ability)
    {
        if (Actives.Count >= MaxActives)
            return false;

        NetworkObject nob = AbilityGrant.Spawn(this, ability);
        if (nob == null || !nob.TryGetComponent<ActiveAbility>(out var instance))
            return false;

        Actives.Add(instance); // AFTER Spawn — see class doc
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
    /// caster's aim context and consume a use (removing the roster entry, then despawning the instance, when
    /// exhausted). Public + <c>[Server]</c> so the editor PlayMode test rig can invoke the real path directly.
    /// </summary>
    [Server]
    public void ServerUse()
    {
        int i = SelectedIndex.Value;
        if (i < 0 || i >= Actives.Count) return;
        if (Time.time < _cooldownEndTime) return;

        ActiveAbility active = Actives[i];
        if (active == null) return;

        bool exhausted = active.ServerUse(BuildContext());
        _cooldownEndTime = Time.time + Mathf.Max(0f, active.Cooldown);

        if (exhausted)
        {
            Actives.RemoveAt(i); // BEFORE Despawn — see class doc
            active.Despawn();
            SelectedIndex.Value = AbilityInventoryLogic.ClampSelection(SelectedIndex.Value, Actives.Count);
        }
    }

    private AbilityAim BuildContext()
    {
        Transform src = aimSource != null ? aimSource : transform;
        // Source = the caster's pawn; an aimed ability has only an origin + direction (no target/normal/systems),
        // which the AbilityAim typing enforces on the operations authored here. The caster's player id, when
        // an operation needs it, is read off Instigator.ObjectId.
        return new AbilityAim(
            source: pawn != null ? pawn.gameObject : gameObject,
            instigator: pawn != null ? pawn.ControllingPlayer.Value : null,
            point: src.position,
            direction: src.forward);
    }

    /// <summary>Raise <see cref="InventoryChanged"/>. Also called by instances when their uses change.</summary>
    public void NotifyInventoryChanged() => InventoryChanged?.Invoke();
}
