using Arena.LevelGen;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// <see cref="SiteScorer"/> on a hand-built level: six 10 × 10 rooms in a row along X, each linked both ways to the
/// next, with the default <see cref="SiteWeights"/>. A gap of 5 or more is full strength.
/// </summary>
public class SiteScorerTests
{
    private RoomGraph rooms;
    private readonly SiteWeights weights = new();

    [SetUp]
    public void SetUp() => rooms = Row(6, oneWayFrom: -1);

    // -------------------- TESTS --------------------

    [Test]
    public void NobodyLeading_NearerRoomsWeighMore_OwnAndOutOfReachAreZero()
    {
        float[] w = Scorer((2, 0), (5, 0)).WeightsFor(0);
        Assert.AreEqual(0f, w[2], "own room");
        Assert.AreEqual(w[1], w[3], "one step either way");
        Assert.That(w[1], Is.GreaterThan(w[0]));
        Assert.That(w[0], Is.GreaterThan(w[5]));
        Assert.That(w[5], Is.GreaterThan(0f), "three steps is still in reach");

        Assert.AreEqual(0f, Scorer((0, 0), (5, 0)).WeightsFor(0)[4], "four steps is out of reach");
    }

    [Test]
    public void Chaser_PrefersRoomsTheyReachBeforeTheLeader()
    {
        float[] w = Scorer((2, 0), (4, 5)).WeightsFor(0);
        Assert.That(w[1], Is.GreaterThan(w[3]), "room 3 is as close to the leader");
        Assert.That(w[3], Is.GreaterThan(0f));
    }

    [Test]
    public void Bias_GrowsWithTheGapToTheLeader()
    {
        float close = Scorer((2, 4), (4, 5)).WeightsFor(0)[3];
        float far = Scorer((2, 0), (4, 5)).WeightsFor(0)[3];
        Assert.That(far, Is.LessThan(close));
    }

    [Test]
    public void SoleLeader_PrefersRoomsFarFromEveryone()
    {
        var scorer = Scorer((2, 5), (5, 0));
        Assert.IsTrue(scorer.IsLeader(0));
        float[] w = scorer.WeightsFor(0);
        Assert.That(w[0], Is.GreaterThan(w[1]), "two steps from the leader beats one");
        Assert.That(w[0], Is.GreaterThan(w[4]), "room 4 is next to the other player");
        Assert.AreEqual(0f, w[2], "own room");
    }

    [Test]
    public void TiedLeaders_RaceEachOther_Gently()
    {
        var scorer = Scorer((2, 3), (4, 3), (0, 0));
        Assert.IsTrue(scorer.IsLeader(0) && scorer.IsLeader(1));
        float[] w = scorer.WeightsFor(0);
        Assert.That(w[1], Is.GreaterThan(w[3]), "room 3 is as close to the other leader");
        Assert.That(w[3], Is.GreaterThan(Scorer((2, 0), (4, 5)).WeightsFor(0)[3]), "weaker than a full-strength chase");
    }

    [Test]
    public void EveryoneOnTheSameScore_NobodyLeads()
    {
        var scorer = Scorer((1, 2), (3, 2), (5, 2));
        for (int i = 0; i < 3; i++)
            Assert.IsFalse(scorer.IsLeader(i));
    }

    [Test]
    public void DeadLeader_IsIgnored_DeadPlayer_GetsNothing()
    {
        float[] w = Scorer((2, 0), (-1, 5)).WeightsFor(0);
        Assert.AreEqual(w[1], w[3]);
        CollectionAssert.AreEqual(new float[6], Scorer((-1, 0), (2, 5)).WeightsFor(0));
    }

    [Test]
    public void OneWayCliff_StepsFollowTheDrop()
    {
        rooms = Row(4, oneWayFrom: 2);
        float[] w = Scorer((3, 0), (0, 0)).WeightsFor(0);
        CollectionAssert.AreEqual(new float[4], w, "nothing is reachable from the cliff's foot");
        Assert.That(Scorer((2, 0), (0, 0)).WeightsFor(0)[3], Is.GreaterThan(0f), "dropping down is");
    }

    [Test]
    public void Lobby_NearChasers_AwayFromTheLeader_NeverInAPlayersRoom()
    {
        float[] w = Scorer((0, 0), (5, 5)).LobbyWeights();
        Assert.AreEqual(0f, w[0]);
        Assert.AreEqual(0f, w[5]);
        Assert.That(w[1], Is.GreaterThan(w[2]));
        Assert.That(w[2], Is.GreaterThan(w[3]));
        Assert.That(w[3], Is.GreaterThan(0f));
        Assert.AreEqual(0f, w[4], "only the leader is near, and the chaser is out of reach");
    }

    [Test]
    public void Lobby_OnlyLeadersAlive_OrNobody()
    {
        float[] w = Scorer((-1, 0), (2, 5)).LobbyWeights();
        Assert.AreEqual(0f, w[2]);
        Assert.That(w[1], Is.GreaterThan(0f), "pickups still go near the leader when nobody else is alive");
        CollectionAssert.AreEqual(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, Scorer((-1, 0), (-1, 5)).LobbyWeights());
    }

    // -------------------- HELPERS --------------------

    private SiteScorer Scorer(params (int room, int score)[] players)
    {
        var contenders = new Contender[players.Length];
        for (int i = 0; i < players.Length; i++)
            contenders[i] = new Contender(players[i].room, players[i].score);
        return new SiteScorer(rooms, contenders, weights);
    }

    /// <summary><paramref name="count"/> rooms in a row; the link from <paramref name="oneWayFrom"/> to the next only goes forward.</summary>
    private static RoomGraph Row(int count, int oneWayFrom)
    {
        var level = new LevelDescription();
        for (int i = 0; i < count; i++)
        {
            float x = i * 10f;
            int polygon = level.AddPolygon(new[]
            {
                new Vector2(x, 0f), new Vector2(x + 10f, 0f), new Vector2(x + 10f, 10f), new Vector2(x, 10f),
            });
            level.Plots.Add(new PlotDescription { Id = i, Polygon = polygon });
            if (i > 0)
                level.RoomLinks.Add(new RoomLink { RoomA = i - 1, RoomB = i, AToB = true, BToA = i - 1 != oneWayFrom });
        }
        return new RoomGraph(level);
    }
}
