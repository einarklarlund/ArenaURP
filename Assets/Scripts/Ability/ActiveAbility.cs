using System.Collections.Generic;
using Arena.Ability;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The stateful half of a triggered ability, living on the ability's INSTANCE prefab — a logical (no
/// renderer/collider) NetworkObject spawned by <see cref="PawnActiveAbilities"/> and parented under the
/// owning pawn. Config (default uses, cooldown, the authored <see cref="OnUse"/> operations) is serialized prefab
/// data, so it is runtime-tunable per instance; live state (<see cref="UsesRemaining"/>) is a SyncVar every
/// peer sees. Effects needing prefab lists reference a sibling <see cref="SpawnableObjects"/> /
/// <see cref="InstantiableObjects"/> on this same prefab via their <c>source</c> field.
///
/// Identity (display name / icon) resolves through the sibling <see cref="TypeObjectRoot"/>'s baked
/// <see cref="AbilityType"/> — nothing about WHICH ability this is rides the wire; the spawned prefab IS the
/// identity.
/// </summary>
public sealed class ActiveAbility : NetworkBehaviour
{
    [Header("Config (authored on the ability prefab)")]
    [Min(1), Tooltip("Uses granted when this ability is acquired.")]
    public int DefaultUses = 1;
    [Min(0f), Tooltip("Seconds between uses.")]
    public float Cooldown = 0f;

    [Header("Operations run when the ability is used (aimed from the caster)")]
    [SerializeReference, EffectSelector]
    public List<IEffect<AbilityAim>> OnUse = new();

    /// <summary>Server-authoritative remaining uses; the HUD reads it on every peer.</summary>
    public readonly SyncVar<int> UsesRemaining = new();

    // The owning inventory facade, for the uses-change HUD notification. Membership itself is tracked by
    // the facade's SyncList roster (its OnChange is the add/remove hook), so this instance only notifies
    // for MID-LIFE state changes — never from its own start/stop, whose FishNet callback timing is awkward
    // (stop fires while still parented).
    private PawnActiveAbilities _inventory;

    /// <summary>This ability's identity SO, via the sibling TypeObjectRoot (null if none baked).</summary>
    public AbilityType Type => TryGetComponent<TypeObjectRoot>(out var root) ? root.Type as AbilityType : null;

    public override void OnStartServer()
    {
        base.OnStartServer();
        UsesRemaining.Value = Mathf.Max(1, DefaultUses);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        UsesRemaining.OnChange += OnUsesChanged;
        _inventory = GetComponentInParent<PawnActiveAbilities>();
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        UsesRemaining.OnChange -= OnUsesChanged;
        _inventory = null;
    }

    private void OnUsesChanged(int prev, int next, bool asServer)
    {
        if (_inventory != null)
            _inventory.NotifyInventoryChanged();
    }

    /// <summary>
    /// Server: run the authored operations against the caster's aim and consume one use. Returns true when
    /// the ability is exhausted (the caller despawns this instance).
    /// </summary>
    [Server]
    public bool ServerUse(AbilityAim ctx)
    {
        if (OnUse != null)
            for (int i = 0; i < OnUse.Count; i++)
                OnUse[i]?.Apply(ctx);

        var result = AbilityInventoryLogic.UseSlot(UsesRemaining.Value);
        UsesRemaining.Value = result.UsesRemaining;
        return result.Removed;
    }
}
