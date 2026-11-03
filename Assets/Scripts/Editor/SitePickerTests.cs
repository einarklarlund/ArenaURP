using System.Collections.Generic;
using Arena.LevelGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="SitePicker"/> on a hand-built level: four 10 × 10 rooms in a row along X. Rooms 0–1–2 link both ways,
/// and 2 drops into 3 down a cliff (2 → 3 only). Picks are random, so each test tries many seeds.
/// </summary>
public class SitePickerTests
{
    private const int Seeds = 50;

    private RoomGraph rooms;

    [SetUp]
    public void SetUp()
    {
        var level = new LevelDescription();
        for (int i = 0; i < 4; i++)
        {
            float x = i * 10f;
            int polygon = level.AddPolygon(new[]
            {
                new Vector2(x, 0f), new Vector2(x + 10f, 0f), new Vector2(x + 10f, 10f), new Vector2(x, 10f),
            });
            level.Plots.Add(new PlotDescription { Id = i, Polygon = polygon });
            level.ChestSites.Add(Site(i, ChestSiteKind.OpenGround, 3f));
            level.ChestSites.Add(Site(i, ChestSiteKind.Hilltop, 7f));
        }
        level.RoomLinks.Add(new RoomLink { RoomA = 0, RoomB = 1, AToB = true, BToA = true });
        level.RoomLinks.Add(new RoomLink { RoomA = 1, RoomB = 2, AToB = true, BToA = true });
        level.RoomLinks.Add(new RoomLink { RoomA = 2, RoomB = 3, AToB = true, BToA = false });
        rooms = new RoomGraph(level);
    }

    // -------------------- TESTS --------------------

    [Test]
    public void PicksOnlyAdjacentRooms_NeverThePlayersOwn()
    {
        var picked = PickRooms(1, null, _ => true);
        CollectionAssert.IsSubsetOf(picked, new[] { 0, 2 });
        CollectionAssert.AreEquivalent(new[] { 0, 2 }, picked, "both neighbours come up over the seeds");
    }

    [Test]
    public void OneWayCliff_OnlyTheDownhillSideCounts()
    {
        CollectionAssert.IsSubsetOf(PickRooms(2, null, _ => true), new[] { 1, 3 });
        // From the cliff's foot, room 2 isn't reachable, and room 3 has no other neighbour.
        Assert.IsFalse(SitePicker.TryPick(rooms, 3, null, _ => true, new System.Random(0), out _));
    }

    [Test]
    public void AvoidedRooms_AreSkipped()
    {
        CollectionAssert.AreEquivalent(new[] { 2 }, PickRooms(1, new[] { 0 }, _ => true));
    }

    [Test]
    public void TakenSites_AreSkipped()
    {
        CollectionAssert.AreEquivalent(new[] { 2 }, PickRooms(1, null, site => site.Room != 0));
    }

    [Test]
    public void NoFreeNeighbour_FallsBackToTwoRoomsOut()
    {
        // From room 0, its only neighbour (1) is full: room 2 is next, never 0 itself.
        CollectionAssert.AreEquivalent(new[] { 2 }, PickRooms(0, null, site => site.Room != 1));
    }

    [Test]
    public void AdjacentOnly_DoesNotFallBack()
    {
        Assert.IsFalse(SitePicker.TryPick(rooms, 0, null, site => site.Room != 1, new System.Random(0), out _, twoStepsOut: false));
    }

    [Test]
    public void NothingFree_OrOutsideTheLevel_PicksNothing()
    {
        Assert.IsFalse(SitePicker.TryPick(rooms, 1, null, site => site.Room == 1, new System.Random(0), out _));
        Assert.IsFalse(SitePicker.TryPick(rooms, -1, null, _ => true, new System.Random(0), out _));
    }

    [Test]
    public void UpperFloorsAndBalconies_ComeFirstInARoom()
    {
        var level = rooms.Level;
        level.ChestSites.Add(Site(0, ChestSiteKind.Balcony, 5f));
        rooms = new RoomGraph(level);

        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.IsTrue(SitePicker.TryPick(rooms, 1, new[] { 2 }, _ => true, new System.Random(seed), out ChestSite site));
            Assert.AreEqual(ChestSiteKind.Balcony, site.Kind, $"seed {seed}");
        }
    }

    [Test]
    public void CornerFor_PutsTheFootprintsBottomCentreOnTheSite()
    {
        var footprint = new Bounds(new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
        foreach (float yaw in new[] { 0f, 37f, 90f, 225f })
        {
            var site = new ChestSite { Position = new Vector3(4f, 2f, -3f), YawDegrees = yaw };
            SitePicker.CornerFor(site, footprint, out Vector3 position, out Quaternion rotation);

            Vector3 bottomCentre = position + rotation * new Vector3(footprint.center.x, 0f, footprint.center.z);
            Assert.That(Vector3.Distance(bottomCentre, site.Position), Is.LessThan(0.001f), $"yaw {yaw}");
            Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(0f, yaw, 0f)), Is.LessThan(0.01f), $"yaw {yaw}");
        }
    }

    [Test]
    public void Footprint_OfEachChestPrefab_RunsFromItsCorner()
    {
        foreach (string path in new[] { "Assets/Chests/Supply Chest.prefab", "Assets/Chests/Weapon Chest.prefab" })
        {
            Bounds footprint = SitePicker.Footprint(AssetDatabase.LoadAssetAtPath<ChestTimer>(path));
            Assert.That(Vector3.Distance(footprint.min, Vector3.zero), Is.LessThan(0.001f), path + " min");
            Assert.That(Vector3.Distance(footprint.size, Vector3.one), Is.LessThan(0.001f), path + " size");
        }
    }

    // -------------------- HELPERS --------------------

    private static ChestSite Site(int room, ChestSiteKind kind, float z) =>
        new() { Kind = kind, Room = room, Position = new Vector3(room * 10f + 5f, 0f, z) };

    /// <summary>The distinct rooms picked over every seed; fails if any seed picks nothing.</summary>
    private List<int> PickRooms(int playerRoom, ICollection<int> avoid, System.Func<ChestSite, bool> isFree)
    {
        var picked = new List<int>();
        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.IsTrue(SitePicker.TryPick(rooms, playerRoom, avoid, isFree, new System.Random(seed), out ChestSite site), $"seed {seed}");
            Assert.AreNotEqual(playerRoom, site.Room, $"seed {seed}");
            if (!picked.Contains(site.Room)) picked.Add(site.Room);
        }
        return picked;
    }
}
