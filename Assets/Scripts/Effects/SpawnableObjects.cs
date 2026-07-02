using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Authoring data for a <see cref="SpawnNetworkObjects"/> effect: the networked prefab(s) to spawn and how
/// many / how wide to fan them. Lives on the same GameObject as the effect's applier (a projectile prefab, an
/// ability instance) so the effect can hold a direct reference to THIS specific list — one applier may carry
/// several <see cref="SpawnableObjects"/>, each feeding a different <see cref="SpawnNetworkObjects"/>, which a
/// bare GetComponent could not disambiguate. Keeping the config on this component (not on the stateless effect)
/// is what lets it be tuned per-instance at runtime.
/// </summary>
public sealed class SpawnableObjects : MonoBehaviour
{
    [SerializeField, Tooltip("The networked prefab(s) to spawn. When Count exceeds the list size, the list is cycled.")]
    private List<NetworkObject> prefabs = new();
    [SerializeField, Min(1), Tooltip("How many to spawn in total.")]
    private int count = 1;
    [SerializeField, Tooltip("0 = all along the aim direction; 360 = even ring; in between = an arc.")]
    private float spreadDegrees = 0f;

    public IReadOnlyList<NetworkObject> Prefabs => prefabs;
    public int Count => count;
    public float SpreadDegrees => spreadDegrees;
}
