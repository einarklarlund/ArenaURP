using System.Collections.Generic;
using Arena.Ability;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// The stateful half of a triggered ability, living on the ability's INSTANCE prefab — a logical (no
/// renderer/collider) NetworkObject spawned by <see cref="PawnActiveAbilities"/> and parented under the
/// owning pawn. Config (default uses, cooldown) is serialized prefab data, so it is runtime-tunable per instance;
/// live state (<see cref="UsesRemaining"/>) is a SyncVar every peer sees. Use-time behaviours are
/// <see cref="SpawnNetworkObjects"/> components on this same prefab, run by GetComponents at use time; they read
/// their prefab list off a sibling <see cref="SpawnableObjects"/>.
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

    // Use-time behaviours (spawn a fan, etc.) are SpawnNetworkObjects components authored on this prefab (ADR 0003),
    // run by GetComponents in ServerUse — no serialized operation list, and no IUseHandler until a second use-site
    // behaviour earns one (rule of two).

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

    private readonly List<SpawnNetworkObjects> _spawners = new();

    /// <summary>
    /// Server: run this ability's use-time behaviours (its <see cref="SpawnNetworkObjects"/> components) against the
    /// caster's aim — <paramref name="origin"/> + <paramref name="direction"/>, attributed to
    /// <paramref name="instigator"/> — and consume one use. Returns true when the ability is exhausted (the caller
    /// despawns this instance).
    /// </summary>
    [Server]
    public bool ServerUse(Vector3 origin, Vector3 direction, NetworkPlayer instigator)
    {
        GetComponents(_spawners);
        for (int i = 0; i < _spawners.Count; i++)
            _spawners[i].ServerSpawn(origin, direction, instigator);

        var result = AbilityInventoryLogic.UseSlot(UsesRemaining.Value);
        UsesRemaining.Value = result.UsesRemaining;
        return result.Removed;
    }
}
