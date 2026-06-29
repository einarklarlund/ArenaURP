using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The ONE place a projectile is spawned, shared by every spawner (PawnWeapon firing, the SpawnProjectiles
/// effect spawning children). It funnels the PER-SPAWN sequence — pooled-instantiate the type's prefab,
/// stamp ownership on <see cref="NetPlayerOwnership"/>, set the per-spawn motion anchor, and network-spawn —
/// so no spawner can forget a step (notably the SpawnTick that anchors deterministic age, or the owner that
/// drives owner-safety + attribution). It does NOT initialize the instance's components from the type: the
/// prefab's <see cref="TypeObjectRoot"/> does that itself at Awake, on every peer (so predicted/observer
/// spawns are configured without the spawner running).
/// </summary>
public static class ProjectileSpawner
{
    /// <summary>
    /// Pooled-instantiate, stamp the per-spawn data, and authoritatively spawn one projectile (its components
    /// configure themselves from the baked type at Awake). <paramref name="ownerConnection"/> is the firing
    /// client's connection for the predicted-spawn path (PawnWeapon); pass null for server-only child spawns.
    /// Returns the spawned NetworkObject, or null.
    /// </summary>
    public static NetworkObject Spawn(ProjectileType type, NetworkManager manager, int ownerId, PreciseTick tick, Vector3 position, Vector3 direction, bool asServer, NetworkConnection ownerConnection = null)
    {
        if (type == null || manager == null)
            return null;

        Quaternion rot = direction.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        NetworkObject nob = manager.GetPooledInstantiated(type.InstancePrefab, position, rot, asServer);
        if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
            ownership.SetupOwner(ownerId);
        if (nob.TryGetComponent<NetworkedMotion>(out var motion))
            motion.SetupSpawn(tick, position, direction);
        manager.ServerManager.Spawn(nob, ownerConnection);
        return nob;
    }
}
