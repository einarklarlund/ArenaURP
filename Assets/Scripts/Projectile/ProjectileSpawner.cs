using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// The ONE place a projectile is spawned, shared by every spawner (PawnWeapon firing, the SpawnProjectiles
/// effect spawning children). It funnels the full sequence — the type builds + initializes its instance,
/// ownership is stamped on <see cref="NetPlayerOwnership"/>, the per-spawn motion payload is set, and the
/// object is network-spawned — so no spawner can forget a step (notably the SpawnTick that anchors
/// deterministic age, or the owner that drives owner-safety + attribution).
/// </summary>
public static class ProjectileSpawner
{
    /// <summary>
    /// Build, configure, and authoritatively spawn one projectile. <paramref name="ownerConnection"/> is the
    /// firing client's connection for the predicted-spawn path (PawnWeapon); pass null for server-only
    /// child spawns. Returns the spawned NetworkObject, or null.
    /// </summary>
    public static NetworkObject Spawn(ProjectileType type, NetworkManager manager, int ownerId, PreciseTick tick, Vector3 position, Vector3 direction, bool asServer, NetworkConnection ownerConnection = null)
    {
        if (type == null || manager == null)
            return null;

        Quaternion rot = direction.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        NetworkObject nob = type.Create(manager, position, rot, asServer);
        if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
            ownership.SetupOwner(ownerId);
        if (nob.TryGetComponent<NetworkedMotion>(out var motion))
            motion.SetupSpawn(tick, position, direction);
        manager.ServerManager.Spawn(nob, ownerConnection);
        return nob;
    }
}
