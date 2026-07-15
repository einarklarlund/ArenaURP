using Arena.NetworkedMotion;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The ONE generic networked-spawn behaviour (component-handler shape, ADR 0003): spawn the networked prefab(s)
/// configured on a sibling <see cref="SpawnableObjects"/>, fanned across its spread around a direction and
/// originating at a point. Run at ACTIVE-ABILITY use time via <c>GetComponents</c> on the ability instance
/// (<see cref="ActiveAbility.ServerUse"/>) — there is no IUseHandler interface until a SECOND use-site behaviour
/// earns one (rule of two). It reads only an origin + direction + firer, so nothing about the caster's systems is
/// coupled here.
///
/// The prefab list, count, and spread are DATA on a <see cref="SpawnableObjects"/> component (a plain data-holder),
/// resolved by GetComponent on this GameObject; set <see cref="source"/> only to disambiguate when several are
/// present. Spawning is instantiate-at-pose + stamp the firer + network-spawn: the instance's components
/// self-configure (TypeObjectRoot at Awake, NetworkedMotion's anchor at OnStartNetwork from the pose + tick). The
/// firer id is the one per-spawn value a component cannot self-derive (bots have no owning connection), so it is
/// stamped here from the caller.
/// </summary>
public sealed class SpawnNetworkObjects : MonoBehaviour
{
    [SerializeField, Tooltip("Optional explicit SpawnableObjects. Leave null to resolve via GetComponent on this " +
             "GameObject; set it only to disambiguate when several are present.")]
    private SpawnableObjects source;

    /// <summary>Server: spawn the fan from <paramref name="point"/> along <paramref name="direction"/>, attributing
    /// each spawned object to <paramref name="instigator"/> (stamped as its owner id before the network spawn).</summary>
    public void ServerSpawn(Vector3 point, Vector3 direction, NetworkPlayer instigator)
    {
        SpawnableObjects resolved = source;
        if (resolved == null && !TryGetComponent(out resolved))
            return;
        if (resolved.Prefabs.Count == 0)
            return;

        var manager = InstanceFinder.NetworkManager;
        if (manager == null)
            return;

        int ownerId = instigator != null ? instigator.ObjectId : -1;
        int count = resolved.Count;
        for (int i = 0; i < count; i++)
        {
            var prefab = resolved.Prefabs[i % resolved.Prefabs.Count];
            if (prefab == null)
                continue;

            Vector3 dir = SpawnFanMath.Direction(direction, i, count, resolved.SpreadDegrees, Vector3.up);
            Quaternion rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            NetworkObject nob = manager.GetPooledInstantiated(prefab, point, rot, asServer: true);
            if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
                ownership.SpawnOwnerId = ownerId;
            manager.ServerManager.Spawn(nob);
        }
    }
}
