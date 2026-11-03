using System.Collections.Generic;
using Arena.LevelGen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="SitePicker"/> on a hand-built level: four 10 × 10 rooms in a row along X, two sites each. Picks are
/// random, so each test tries many seeds. Which rooms weigh what is <see cref="SiteScorerTests"/>' job.
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
        rooms = new RoomGraph(level);
    }

    // -------------------- TESTS --------------------

    [Test]
    public void PicksOnlyRoomsWithWeight()
    {
        var picked = PickRooms(new[] { 1f, 0f, 1f, 0f }, null, _ => true);
        CollectionAssert.AreEquivalent(new[] { 0, 2 }, picked, "both weighted rooms come up over the seeds, nothing else");
    }

    [Test]
    public void HeavierRooms_ComeUpMoreOften()
    {
        int heavy = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            Assert.IsTrue(SitePicker.TryPick(rooms, new[] { 0.2f, 0f, 1f, 0f }, null, _ => true, new System.Random(seed), out ChestSite site));
            if (site.Room == 2) heavy++;
        }
        Assert.That(heavy, Is.InRange(300, 370), "about 5 in 6");
    }

    [Test]
    public void AvoidedRooms_AreSkipped()
    {
        CollectionAssert.AreEquivalent(new[] { 2 }, PickRooms(Even, new[] { 0, 1, 3 }, _ => true));
    }

    [Test]
    public void TakenSites_AreSkipped()
    {
        CollectionAssert.AreEquivalent(new[] { 2 }, PickRooms(Even, null, site => site.Room == 2));
    }

    [Test]
    public void NothingFree_OrNoWeight_PicksNothing()
    {
        Assert.IsFalse(SitePicker.TryPick(rooms, new[] { 0f, 1f, 0f, 0f }, null, site => site.Room != 1, new System.Random(0), out _));
        Assert.IsFalse(SitePicker.TryPick(rooms, new float[4], null, _ => true, new System.Random(0), out _));
    }

    [Test]
    public void UpperFloorsAndBalconies_ComeFirstInARoom()
    {
        var level = rooms.Level;
        level.ChestSites.Add(Site(0, ChestSiteKind.Balcony, 5f));
        rooms = new RoomGraph(level);

        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.IsTrue(SitePicker.TryPick(rooms, new[] { 1f, 0f, 0f, 0f }, null, _ => true, new System.Random(seed), out ChestSite site));
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
    private static readonly float[] Even = { 1f, 1f, 1f, 1f };

    private List<int> PickRooms(float[] weights, ICollection<int> avoid, System.Func<ChestSite, bool> isFree)
    {
        var picked = new List<int>();
        for (int seed = 0; seed < Seeds; seed++)
        {
            Assert.IsTrue(SitePicker.TryPick(rooms, weights, avoid, isFree, new System.Random(seed), out ChestSite site), $"seed {seed}");
            if (!picked.Contains(site.Room)) picked.Add(site.Room);
        }
        return picked;
    }
}
