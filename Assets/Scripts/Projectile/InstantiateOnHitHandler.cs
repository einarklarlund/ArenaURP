using UnityEngine;

/// <summary>
/// Hit handler (component-handler shape, ADR 0003): instantiate the non-networked prefab(s) configured on a sibling
/// <see cref="InstantiableObjects"/> at the hit point, oriented to the surface normal (e.g. impact VFX, a hit
/// marker). Discovered by presence — typically contributed by an owner's equipped passive (authored beside the
/// prefab's own <see cref="InstantiableObjects"/>) — and harvested by <see cref="HitResolver"/>.
///
/// A LOCAL instantiate (runs in the server-authoritative collision context; the host/editor sees it). To guarantee
/// it on remote clients, make the prefab networked and use <see cref="SpawnNetworkObjects"/> instead. The prefab
/// list is data on the <see cref="InstantiableObjects"/> component, resolved by GetComponent on THIS handler's own
/// GameObject; set <see cref="source"/> only to disambiguate when the object carries several.
/// </summary>
public sealed class InstantiateOnHitHandler : MonoBehaviour, IHitHandler
{
    [SerializeField, Tooltip("Optional explicit InstantiableObjects. Leave null to resolve via GetComponent on this " +
             "handler's own GameObject; set it only to disambiguate when several are present.")]
    private InstantiableObjects source;

    [Tooltip("Which hit channels this KIND instantiates on — per-prefab-copy authoring (the LayerMask idiom).")]
    [SerializeField] private HitChannels enabledChannels = HitChannels.Player;

    // Reads only point + normal, present on every channel; the KIND picks where via enabledChannels.
    public HitChannels SupportedChannels => HitChannels.Surface | HitChannels.Player | HitChannels.Projectile;
    public HitChannels EnabledChannels => enabledChannels;

    public void OnHit(in HitContext context)
    {
        InstantiableObjects resolved = source;
        if (resolved == null && !TryGetComponent(out resolved))
            return;

        Quaternion rot = context.Normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(context.Normal) : Quaternion.identity;
        var prefabs = resolved.Prefabs;
        for (int i = 0; i < prefabs.Count; i++)
            if (prefabs[i] != null)
                Object.Instantiate(prefabs[i], context.Point, rot);
    }
}
