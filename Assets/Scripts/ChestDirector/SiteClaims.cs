using System.Collections.Generic;
using Arena.LevelGen;
using FishNet.Object;

/// <summary>
/// Which chest sites hold something: a chest or an experience capsule, any player's. A site is free again once its
/// object is no longer spawned (opened, broken, expired). Shared by <see cref="ChestDirector"/> and
/// <see cref="ExperienceCapsuleDirector"/>. Server only.
/// </summary>
public static class SiteClaims
{
    // Keyed by position: sites are plain structs, and no two share a position (SiteStage keeps them 1.5 apart).
    private static readonly Dictionary<UnityEngine.Vector3, NetworkObject> claims = new();

    public static bool IsFree(ChestSite site) =>
        !claims.TryGetValue(site.Position, out NetworkObject holder) || holder == null || !holder.IsSpawned;

    public static void Claim(ChestSite site, NetworkObject holder) => claims[site.Position] = holder;

    /// <summary>A new level, or the server stopped: every site is free.</summary>
    public static void Clear() => claims.Clear();
}
