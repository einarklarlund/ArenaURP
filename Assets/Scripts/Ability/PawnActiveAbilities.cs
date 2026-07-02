using System.Collections.Generic;
using Arena.Ability;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// Server-authoritative ACTIVE-ability inventory facade for a pawn: up to <see cref="MaxActives"/> triggered
/// abilities, cycled with Q/E and fired with F. The inventory is not a synced list — each granted ability is
/// a spawned <see cref="ActiveAbility"/> instance (its type's prefab) parented under this pawn's
/// NetworkObject; FishNet replicates the instances and their parenting, so every peer enumerates the same
/// children. Live state (uses remaining) is a SyncVar ON the instance; config (default uses, cooldown, the
/// authored operations) is that prefab's serialized data.
///
/// This component owns the pawn-side concerns: input edges, the selection SyncVar, the server-side use path
/// (cooldown gate, building the <see cref="AbilityAim"/> context, despawning exhausted instances), and the
/// <see cref="InventoryChanged"/> HUD hook. Instances call <see cref="NotifyInventoryChanged"/> from their
/// network start/stop and uses changes. Passive abilities live on the sibling <see cref="PawnPassiveAbilities"/>.
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
    public readonly SyncVar<int> SelectedIndex = new();

    /// <summary>Raised on any peer whenever the child active instances (or their uses) change. HUD hook.</summary>
    public event System.Action InventoryChanged;

    // Scratch buffer for child enumeration (avoid per-call allocation).
    private readonly List<ActiveAbility> _activesBuf = new();

    private bool _prevCycleLeft, _prevCycleRight, _prevUse;
    private float _cooldownEndTime; // server-side gate

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        NotifyInventoryChanged(); // surface any already-replicated children (late join / pooled reuse)
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

    // ---- inventory enumeration (children of this pawn's NetworkObject) ----

    /// <summary>Fill <paramref name="results"/> with the active-ability instances under this pawn, in slot order.</summary>
    public void GetActives(List<ActiveAbility> results)
    {
        results.Clear();
        GetComponentsInChildren(includeInactive: true, results);
    }

    public int ActivesCount { get { GetActives(_activesBuf); return _activesBuf.Count; } }

    // ---- server: acquisition (called by pickups) ----

    /// <summary>Add an active ability. Returns false if it's null, prefab-less, or the 5 slots are full.</summary>
    [Server]
    public bool AddActive(ActiveAbilityType ability)
    {
        if (ActivesCount >= MaxActives)
            return false;
        return AbilityGrant.Spawn(this, ability);
    }

    // ---- client intent -> server ----

    [ServerRpc]
    private void ServerCycleSelected(int dir)
    {
        SelectedIndex.Value = AbilityInventoryLogic.CycleIndex(SelectedIndex.Value, ActivesCount, dir);
    }

    [ServerRpc]
    private void ServerUseSelected() => ServerUse();

    /// <summary>
    /// Server-side use of the currently selected active ability: run its authored operations against the
    /// caster's aim context and consume a use (despawning the instance when exhausted). Public +
    /// <c>[Server]</c> so the editor PlayMode test rig can invoke the real path directly.
    /// </summary>
    [Server]
    public void ServerUse()
    {
        GetActives(_activesBuf);
        int i = SelectedIndex.Value;
        if (i < 0 || i >= _activesBuf.Count) return;
        if (Time.time < _cooldownEndTime) return;

        ActiveAbility active = _activesBuf[i];
        bool exhausted = active.ServerUse(BuildContext());
        _cooldownEndTime = Time.time + Mathf.Max(0f, active.Cooldown);

        if (exhausted)
        {
            active.Despawn();
            SelectedIndex.Value = AbilityInventoryLogic.ClampSelection(SelectedIndex.Value, _activesBuf.Count - 1);
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

    /// <summary>Raise <see cref="InventoryChanged"/>. Called by active instances on start/stop/uses changes.</summary>
    public void NotifyInventoryChanged() => InventoryChanged?.Invoke();
}
