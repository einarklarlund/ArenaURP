using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The instantiation controller of an entity (ADR 0005): on a hit it instantiates its own configured non-networked
/// prefab(s) at the hit point, oriented to the surface normal (impact VFX, a hit marker). A controller OWNS STATE
/// (its <see cref="prefabs"/> list and the <see cref="enabledChannels"/> it reacts on) — self-contained, so a
/// cloned copy on a spawned object needs no sibling to read from. This replaces the old
/// InstantiateOnHitHandler + InstantiableObjects pair: holding its own list is what lets it self-clone.
///
/// Discovered by presence at the hit site and dispatched the one <see cref="HitMessage"/>; also an
/// <see cref="IOwnedObjectContribution"/>, so an owner passive can add "your shots leave a mark" onto the objects it
/// spawns (the passive instance carries this controller; <see cref="SpawnContributionReceiver"/> self-clones it onto each
/// spawned object). A LOCAL instantiate — it runs in the server-authoritative collision context, so the host/editor
/// sees it; to guarantee it on remote clients, make the prefab networked and use <see cref="SpawnNetworkObjects"/>.
/// </summary>
public sealed class InstantiationController : MonoBehaviour, IHitMessageHandler, IOwnedObjectContribution
{
    [Tooltip("The non-networked prefab(s) to instantiate at the hit point, oriented to the surface normal.")]
    [SerializeField] private List<GameObject> prefabs = new();

    [Tooltip("Which hit channels this KIND instantiates on — per-prefab-copy authoring (the LayerMask idiom).")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    // Reads only point + normal, present on every channel; the KIND picks where via enabledChannels.
    public HitChannels EnabledHitChannels => enabledChannels;

    public void OnHit(HitMessage hit)
    {
        Quaternion rot = hit.Normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(hit.Normal) : Quaternion.identity;
        for (int i = 0; i < prefabs.Count; i++)
            if (prefabs[i] != null)
                Object.Instantiate(prefabs[i], hit.Point, rot);
    }

    /// <summary>Owner contribution (self-cloning): add an instantiation reaction onto <paramref name="target"/>
    /// unless it already has one, copying this instance's channels and prefab list. Returns the added component, or
    /// null when one was already present (so pooled-reuse teardown never strips a handler the target was authored
    /// with).</summary>
    public Component Apply(GameObject target)
    {
        if (target.TryGetComponent<InstantiationController>(out _))
            return null;
        var clone = target.AddComponent<InstantiationController>();
        clone.enabledChannels = enabledChannels;
        clone.prefabs = new List<GameObject>(prefabs);
        return clone;
    }
}
