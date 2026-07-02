using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authoring data for an <see cref="InstantiateGameObjects"/> effect: the non-networked prefab(s) to
/// instantiate locally (e.g. impact VFX). Lives on the same GameObject as the effect's applier so the effect
/// can reference THIS specific list — one applier may carry several <see cref="InstantiableObjects"/>, which a
/// bare GetComponent could not disambiguate. Keeping the config on this component keeps the effect stateless
/// and lets the list be tuned per-instance.
/// </summary>
public sealed class InstantiableObjects : MonoBehaviour
{
    [SerializeField, Tooltip("The non-networked prefab(s) to instantiate at the hit point.")]
    private List<GameObject> prefabs = new();

    public IReadOnlyList<GameObject> Prefabs => prefabs;
}
