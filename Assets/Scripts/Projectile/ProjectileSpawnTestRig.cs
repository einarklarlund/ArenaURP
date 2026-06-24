#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Arena.Projectile;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the projectile-spawn PlayMode tests. Lives in Assembly-CSharp so it can
/// touch <see cref="ProjectileWorldProvider"/> / <see cref="PawnProjectile"/> directly ??? the
/// Arena.PlayTests asmdef cannot reference Assembly-CSharp, so the tests call these by reflection (one
/// contained seam, per the integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class ProjectileSpawnTestRig
{
    public static ProjectileWorldProvider FindProvider()
        => Object.FindAnyObjectByType<ProjectileWorldProvider>();

    /// <summary>Point the provider's (private serialized) catalog at a single type-&gt;prefab mapping.</summary>
    public static void ConfigureCatalog(ProjectileWorldProvider provider, ProjectileType type, NetworkObject prefab)
    {
        var entry = new ProjectileWorldProvider.TypePrefab { Type = type, Prefab = prefab };
        var field = typeof(ProjectileWorldProvider).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic);
        var list = (List<ProjectileWorldProvider.TypePrefab>)field.GetValue(provider);
        list.Clear();
        list.Add(entry);
    }

    /// <summary>Invoke the production child-spawn path. Returns the spawned child's ObjectId, or -1.</summary>
    public static int Spawn(ProjectileWorldProvider provider, ProjectileType type, Vector3 pos, Vector3 dir, int ownerId)
        => provider.Spawn(type, pos, dir, ownerId);

    public static GameObject FindSpawned(int objectId)
    {
        if (InstanceFinder.ServerManager == null) return null;
        return InstanceFinder.ServerManager.Objects.Spawned.TryGetValue(objectId, out var nob) && nob != null
            ? nob.gameObject
            : null;
    }

    public static uint CurrentTick() => InstanceFinder.TimeManager.Tick;

    // ---- PawnProjectile payload getters (typed; keeps reflection out of the tests) ----
    public static int SpawnOwnerId(GameObject go) => go.GetComponent<PawnProjectile>().SpawnOwnerId;
    public static uint SpawnTickValue(GameObject go) => go.GetComponent<PawnProjectile>().SpawnTick.Tick;
    public static Vector3 SpawnPosition(GameObject go) => go.GetComponent<PawnProjectile>().SpawnPosition;
    public static Vector3 SpawnForward(GameObject go) => go.GetComponent<PawnProjectile>().SpawnForward;

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

    /// <summary>The spawned object carrying a PawnProjectile (the bullet), or null. The shooter has Pawn, not PawnProjectile.</summary>
    public static GameObject FindSpawnedProjectile()
    {
        if (InstanceFinder.ServerManager == null) return null;
        foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
            if (nob != null && nob.GetComponent<PawnProjectile>() != null)
                return nob.gameObject;
        return null;
    }

    public static int CountSpawnedProjectiles()
    {
        int n = 0;
        if (InstanceFinder.ServerManager == null) return 0;
        foreach (var nob in InstanceFinder.ServerManager.Objects.Spawned.Values)
            if (nob != null && nob.GetComponent<PawnProjectile>() != null)
                n++;
        return n;
    }
}
#endif
