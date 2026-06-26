using FishNet.Object;
using UnityEngine;

/// <summary>
/// Broadcasts a cosmetic impact VFX to every peer. The VFX prefab is baked on the instance prefab (a
/// non-networked prefab can't be sent over the wire, so each peer spawns it locally); only the point/normal
/// travel over the ObserversRpc. The <c>SpawnImpactVfx</c> world-effect triggers it server-side on a hit.
/// Kept off NetworkedMotion so motion stays a pure generic concern.
/// </summary>
public sealed class ImpactVfxBroadcaster : NetworkBehaviour
{
    [Tooltip("Non-networked cosmetic VFX prefab spawned at the impact point on every peer.")]
    [SerializeField] private GameObject vfxPrefab;

    [ObserversRpc(RunLocally = true)]
    public void Broadcast(Vector3 point, Vector3 normal)
    {
        if (vfxPrefab == null)
            return;
        Quaternion rot = normal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(normal) : Quaternion.identity;
        Instantiate(vfxPrefab, point, rot);
    }
}
