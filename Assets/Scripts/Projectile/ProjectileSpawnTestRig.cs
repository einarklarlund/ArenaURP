#if UNITY_EDITOR
using FishNet;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the projectile-spawn PlayMode tests. Lives in Assembly-CSharp so it can
/// touch <see cref="ProjectileType"/> / <see cref="NetworkedMotion"/> directly — the Arena.PlayTests
/// asmdef cannot reference Assembly-CSharp, so the tests call these by reflection (one contained seam, per
/// the integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class ProjectileSpawnTestRig
{
    /// <summary>Build + initialize + authoritatively spawn a child projectile via its type. Returns the ObjectId, or -1.</summary>
    public static int SpawnViaType(ProjectileType type, Vector3 pos, Vector3 dir, int ownerId)
    {
        var manager = InstanceFinder.NetworkManager;
        if (type == null || manager == null)
            return -1;

        Quaternion rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : Quaternion.identity;
        var nob = manager.GetPooledInstantiated(type.InstancePrefab, pos, rot, asServer: true);
        if (nob.TryGetComponent<NetPlayerOwnership>(out var ownership))
            ownership.SpawnOwnerId = ownerId;
        manager.ServerManager.Spawn(nob);
        return nob != null ? nob.ObjectId : -1;
    }

    public static GameObject FindSpawned(int objectId)
    {
        if (InstanceFinder.ServerManager == null) return null;
        return InstanceFinder.ServerManager.Objects.Spawned.TryGetValue(objectId, out var nob) && nob != null
            ? nob.gameObject
            : null;
    }

    public static uint CurrentTick() => InstanceFinder.TimeManager.Tick;

    // ---- NetworkedMotion payload getters (typed; keeps reflection out of the tests) ----
    public static int SpawnOwnerId(GameObject go) => go.GetComponent<NetPlayerOwnership>().SpawnOwnerId;
    public static uint SpawnTickValue(GameObject go) => go.GetComponent<NetworkedMotion>().SpawnTick.Tick;
    public static Vector3 SpawnPosition(GameObject go) => go.GetComponent<NetworkedMotion>().SpawnPosition;
    public static Vector3 SpawnForward(GameObject go) => go.GetComponent<NetworkedMotion>().SpawnForward;

    // ---- weapon-case helpers ----

    /// <summary>Drive the shooter's FakePawnInput fire button (so PawnWeapon fires).</summary>
    public static void SetFire(GameObject shooter, bool fire)
        => shooter.GetComponent<FakePawnInput>().Fire = fire;

    /// <summary>Wire a shooter's Pawn to a NetworkPlayer so its shots are attributed to that player.</summary>
    public static void WireOwner(GameObject shooter, GameObject player)
    {
        var pawn = shooter.GetComponent<Pawn>();
        var np = player.GetComponent<NetworkPlayer>();
        pawn.ControllingPlayer.Value = np;
        np.ControlledPawn.Value = pawn;
    }

    public static int PlayerObjectId(GameObject player) => player.GetComponent<NetworkPlayer>().ObjectId;

    /// <summary>The spawned object carrying a NetworkedMotion (the bullet), or null. The shooter has Pawn, not NetworkedMotion.</summary>
    public static GameObject FindSpawnedProjectile()
    {
        if (InstanceFinder.ServerManager == null) return null;
        foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
            if (nob != null && nob.GetComponent<NetworkedMotion>() != null)
                return nob.gameObject;
        return null;
    }

    public static int CountSpawnedProjectiles()
    {
        int n = 0;
        if (InstanceFinder.ServerManager == null) return 0;
        foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
            if (nob != null && nob.GetComponent<NetworkedMotion>() != null)
                n++;
        return n;
    }

    /// <summary>Count live GameObjects whose name starts with <paramref name="namePrefix"/> (e.g. "Foo(Clone)").</summary>
    public static int CountClones(string namePrefix)
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith(namePrefix)) n++;
        return n;
    }
}
#endif
