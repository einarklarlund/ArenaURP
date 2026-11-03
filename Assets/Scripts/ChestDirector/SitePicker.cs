using System;
using System.Collections.Generic;
using Arena.LevelGen;
using UnityEngine;

/// <summary>
/// Picks one of level generation's chest sites for a spawn: a room drawn at random by <see cref="SiteScorer"/>'s
/// weights, then a free site in it. It doesn't find sites: SiteStage does that from the level's features. Plain C#,
/// so the directors and the tests use the same rules. See chest-director-design.md.
/// </summary>
public static class SitePicker
{
    /// <summary>
    /// A free site in a room drawn by <paramref name="roomWeights"/> (rooms weighing 0 never come up), skipping
    /// <paramref name="avoidRooms"/> and rooms with no free site. Upper floors and balconies come first within a room.
    /// False when nothing is left to draw.
    /// </summary>
    public static bool TryPick(RoomGraph rooms, IReadOnlyList<float> roomWeights, ICollection<int> avoidRooms,
        Func<ChestSite, bool> isFree, System.Random random, out ChestSite site)
    {
        site = default;
        if (rooms == null) return false;

        var candidates = new List<int>();
        var candidateWeights = new List<float>();
        for (int room = 0; room < roomWeights.Count; room++)
        {
            if (roomWeights[room] <= 0f || (avoidRooms != null && avoidRooms.Contains(room))) continue;
            if (!HasFreeSite(rooms.ChestSitesIn(room), isFree)) continue;
            candidates.Add(room);
            candidateWeights.Add(roomWeights[room]);
        }

        int drawn = Draw(candidateWeights, random);
        return drawn >= 0 && TryPickSite(rooms.ChestSitesIn(candidates[drawn]), isFree, random, out site);
    }

    /// <summary>An index drawn at random in proportion to <paramref name="weights"/>; -1 when they're all 0 or empty.</summary>
    public static int Draw(IReadOnlyList<float> weights, System.Random random)
    {
        float total = 0f;
        foreach (float weight in weights)
            total += Mathf.Max(0f, weight);
        if (total <= 0f) return -1;

        float pick = (float)random.NextDouble() * total;
        int last = -1;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0f) continue;
            last = i;
            pick -= weights[i];
            if (pick < 0f) return i;
        }
        return last;
    }

    /// <summary>
    /// Where a chest prefab's origin goes so the chest's bottom centre is on <paramref name="site"/>, facing its yaw.
    /// The prefab's origin is a bottom corner, so the footprint's centre is offset from it.
    /// </summary>
    public static void CornerFor(ChestSite site, Bounds footprint, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.Euler(0f, site.YawDegrees, 0f);
        position = site.Position - rotation * new Vector3(footprint.center.x, 0f, footprint.center.z);
    }

    /// <summary>The chest's colliders' combined bounds, in the chest's own space.</summary>
    public static Bounds Footprint(Component chest)
    {
        Transform root = chest.transform;
        Bounds footprint = default;
        bool any = false;

        foreach (BoxCollider box in chest.GetComponentsInChildren<BoxCollider>(true))
        {
            Vector3 half = box.size * 0.5f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = box.center + Vector3.Scale(half, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 point = root.InverseTransformPoint(box.transform.TransformPoint(local));

                if (any)
                    footprint.Encapsulate(point);
                else
                    footprint = new Bounds(point, Vector3.zero);
                any = true;
            }
        }

        return footprint;
    }

    private static bool HasFreeSite(IReadOnlyList<ChestSite> sites, Func<ChestSite, bool> isFree)
    {
        foreach (ChestSite candidate in sites)
            if (isFree(candidate)) return true;
        return false;
    }

    /// <summary>A random free upper-floor or balcony site if there is one, else any random free site.</summary>
    private static bool TryPickSite(IReadOnlyList<ChestSite> sites, Func<ChestSite, bool> isFree, System.Random random,
        out ChestSite site)
    {
        var preferred = new List<ChestSite>();
        var other = new List<ChestSite>();
        foreach (ChestSite candidate in sites)
        {
            if (!isFree(candidate)) continue;
            if (candidate.Kind is ChestSiteKind.BuildingUpper or ChestSiteKind.Balcony)
                preferred.Add(candidate);
            else
                other.Add(candidate);
        }

        List<ChestSite> pool = preferred.Count > 0 ? preferred : other;
        site = pool.Count > 0 ? pool[random.Next(pool.Count)] : default;
        return pool.Count > 0;
    }
}
