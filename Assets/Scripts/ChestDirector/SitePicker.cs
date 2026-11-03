using System;
using System.Collections.Generic;
using Arena.LevelGen;
using UnityEngine;

/// <summary>
/// Picks one of level generation's chest sites for a player's next chest or capsule: a free site in a room next to
/// the player's room, never in it. It doesn't find sites: SiteStage does that from the level's features. Plain C#,
/// so the directors and the tests use the same rules. See chest-director-design.md.
/// </summary>
public static class SitePicker
{
    /// <summary>
    /// A free site in a random room adjacent to <paramref name="playerRoom"/>, skipping <paramref name="avoidRooms"/>.
    /// If no adjacent room has one and <paramref name="twoStepsOut"/>, a room two steps out. Upper floors and
    /// balconies come first within a room. False when the player is outside every room or nothing is free.
    /// </summary>
    public static bool TryPick(RoomGraph rooms, int playerRoom, ICollection<int> avoidRooms, Func<ChestSite, bool> isFree,
        System.Random random, out ChestSite site, bool twoStepsOut = true)
    {
        site = default;
        if (rooms == null || playerRoom < 0) return false;

        var near = new List<int>();
        foreach (int room in rooms.Adjacent(playerRoom))
            if (room != playerRoom && !near.Contains(room)) near.Add(room);

        if (TryPickIn(rooms, Allowed(near, avoidRooms), isFree, random, out site))
            return true;
        if (!twoStepsOut)
            return false;

        var far = new List<int>();
        foreach (int room in near)
            foreach (int next in rooms.Adjacent(room))
                if (next != playerRoom && !near.Contains(next) && !far.Contains(next)) far.Add(next);

        return TryPickIn(rooms, Allowed(far, avoidRooms), isFree, random, out site);
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

    private static List<int> Allowed(List<int> rooms, ICollection<int> avoidRooms)
    {
        if (avoidRooms == null || avoidRooms.Count == 0) return rooms;
        return rooms.FindAll(room => !avoidRooms.Contains(room));
    }

    private static bool TryPickIn(RoomGraph rooms, List<int> candidates, Func<ChestSite, bool> isFree,
        System.Random random, out ChestSite site)
    {
        Shuffle(candidates, random);
        foreach (int room in candidates)
            if (TryPickSite(rooms.ChestSitesIn(room), isFree, random, out site))
                return true;

        site = default;
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

    private static void Shuffle(List<int> list, System.Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
