using FishNet.Object;
using UnityEngine;

/// <summary>
/// The shared catalogue of passive KINDS: a root <see cref="NetworkObject"/> spawned once per match (wired in
/// <c>MatchMediator</c>) and observed on every peer. Each direct child is one passive KIND — a nested
/// <see cref="NetworkObject"/> carrying a <see cref="PassiveInfo"/> (identity) plus one or more <see cref="Passive"/>
/// applicators (and any <see cref="IOwnedObjectContribution"/> create-passive controllers). Because the kinds are
/// authored ONCE here and every owner holds a <see cref="FishNet.Object.Synchronizing.SyncList{T}"/> REFERENCE to a
/// child (in <see cref="PlayerPassiveLoadout"/>), there is no per-player copy and no id→asset indirection — grant,
/// steal and foreign-HUD read all resolve the same shared child on every peer.
///
/// A single global instance is exposed statically (the <see cref="NetPlayerResolver"/> idiom) so the authored
/// grant sites — an <c>AbilityPickup</c> in a scene/prefab that cannot hold a runtime child ref — resolve a KIND by
/// its child GameObject NAME. A missing name is a QUIET warning, not an exception: authoring typos should be visible
/// but must not throw inside server tick / hit resolution.
/// </summary>
public sealed class PassiveRegistry : NetworkBehaviour
{
    /// <summary>The live registry for this match, or null before it spawns / after it despawns. Set on every peer
    /// at <see cref="OnStartNetwork"/>; a match has exactly one.</summary>
    public static PassiveRegistry Instance { get; private set; }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        if (Instance != null && Instance != this)
            Debug.LogWarning("PassiveRegistry: a second registry spawned; kinds should be authored under one root.", this);
        Instance = this;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Resolve the passive KIND whose child GameObject is named <paramref name="kindName"/> to its
    /// <see cref="NetworkObject"/> (the reference a loadout stores). Returns null and warns when no child matches —
    /// an authoring typo, not a fatal error.</summary>
    public NetworkObject ResolveByName(string kindName)
    {
        if (string.IsNullOrEmpty(kindName))
            return null;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name == kindName && child.TryGetComponent<NetworkObject>(out var nob))
                return nob;
        }

        Debug.LogWarning($"PassiveRegistry: no passive kind named '{kindName}' under the registry.", this);
        return null;
    }

    /// <summary>Static convenience: resolve a KIND by name against the live <see cref="Instance"/>, or null (with a
    /// warning) when no registry is spawned yet.</summary>
    public static NetworkObject Resolve(string kindName)
    {
        if (Instance == null)
        {
            Debug.LogWarning($"PassiveRegistry.Resolve('{kindName}'): no registry spawned.");
            return null;
        }
        return Instance.ResolveByName(kindName);
    }
}
