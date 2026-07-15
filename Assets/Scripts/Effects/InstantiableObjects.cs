using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoring data for an <see cref="InstantiateOnHitHandler"/>: the non-networked prefab(s) to instantiate
/// locally at a hit point (e.g. impact VFX, a hit marker). Lives on the same GameObject as the handler that reads
/// it — one object may carry several <see cref="InstantiableObjects"/>, which a bare GetComponent could not
/// disambiguate. Keeping the config on this component keeps the handler config-light and lets the list be tuned
/// per-instance.
/// </summary>
public sealed class InstantiableObjects : MonoBehaviour
{
    [SerializeField, Tooltip("The non-networked prefab(s) to instantiate at the hit point.")]
    private List<GameObject> prefabs = new();

    public IReadOnlyList<GameObject> Prefabs => prefabs;
}
