using System;
using System.Collections.Generic;
using Arena.LevelGen;
using UnityEngine;

/// <summary>A player as site weighting sees them: the room their living pawn is in (-1 when dead or outside every room) and their score.</summary>
public readonly struct Contender
{
    public readonly int Room;
    public readonly int Score;

    public Contender(int room, int score)
    {
        Room = room;
        Score = score;
    }
}

/// <summary>How <see cref="SiteScorer"/> weighs rooms. Distances are room steps along <see cref="RoomGraph.Adjacent"/>.</summary>
[Serializable]
public sealed class SiteWeights
{
    [Tooltip("A room's weight by steps from the player it's for: element 0 is 1 step out. Rooms further out than the last element aren't used.")]
    public float[] near = { 1f, 0.5f, 0.2f };
    [Tooltip("Race factor for a room the leader reaches in as many steps as the player.")]
    public float raceTied = 0.4f;
    [Tooltip("Race factor for a room the leader reaches in fewer steps than the player.")]
    public float raceBehind = 0.15f;
    [Tooltip("The leader's weight for a room by steps from the nearest other player or the leader, whichever is fewer: element 0 is someone in the room. Steps past the last element use the last.")]
    public float[] isolation = { 0.05f, 0.3f, 1f };
    [Tooltip("The score gap (to the leader, or the leader's to second place) at which the bias is at full strength.")]
    public int fullStrengthGap = 5;
    [Tooltip("Bias strength for a gap of 0, such as between players tied for the lead.")]
    [Range(0f, 1f)] public float minStrength = 0.25f;
}

/// <summary>
/// Weighs every room for a spawn, so things are more likely near a player and away from whoever leads; for the leader,
/// away from everyone. A weight of 0 means the room isn't used: a player's own room never is. The leaders are everyone
/// on the top score, and nobody leads while everyone's score is the same. Plain C#, so the directors and the tests use
/// the same rules.
/// </summary>
public sealed class SiteScorer
{
    private const int Unreachable = int.MaxValue;

    private readonly RoomGraph rooms;
    private readonly IReadOnlyList<Contender> contenders;
    private readonly SiteWeights weights;
    private readonly int[][] steps;
    private readonly bool[] leading;
    private readonly int topScore;
    private readonly int secondScore;
    private readonly int leaderCount;

    public SiteScorer(RoomGraph rooms, IReadOnlyList<Contender> contenders, SiteWeights weights)
    {
        this.rooms = rooms;
        this.contenders = contenders;
        this.weights = weights;

        int count = contenders.Count;
        steps = new int[count][];
        leading = new bool[count];

        topScore = int.MinValue;
        int lowest = int.MaxValue;
        foreach (Contender contender in contenders)
        {
            topScore = Math.Max(topScore, contender.Score);
            lowest = Math.Min(lowest, contender.Score);
        }

        secondScore = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            leading[i] = count > 0 && lowest != topScore && contenders[i].Score == topScore;
            if (leading[i])
                leaderCount++;
            else
                secondScore = Math.Max(secondScore, contenders[i].Score);
        }
    }

    public bool IsLeader(int contender) => leading[contender];

    /// <summary>Every room's weight for a spawn that belongs to <paramref name="contender"/>; all 0 when they have no room.</summary>
    public float[] WeightsFor(int contender)
    {
        var result = new float[rooms.RoomCount];
        int own = contenders[contender].Room;
        if (own < 0) return result;

        int[] fromYou = StepsFrom(contender);
        bool soleLeader = leading[contender] && leaderCount == 1;

        for (int room = 0; room < result.Length; room++)
        {
            int you = fromYou[room];
            if (room == own || you < 1 || you > weights.near.Length) continue;

            float near = weights.near[you - 1];
            if (soleLeader)
            {
                int isolation = Math.Min(you, NearestOther(contender, room));
                result[room] = Mathf.Lerp(near, Isolation(isolation), Strength(topScore - secondScore));
            }
            else
            {
                // Tied leaders race the others they're tied with, at the gap-0 strength.
                result[room] = near * Mathf.Lerp(1f, Race(NearestLeader(contender, room) - (long)you),
                    Strength(topScore - contenders[contender].Score));
            }
        }

        return result;
    }

    /// <summary>
    /// Every room's weight for a spawn that belongs to nobody, such as pickups: near the nearest living player who isn't
    /// leading, and a race against the leaders, at the strength of the leaders' margin. Rooms with a living player in
    /// them are 0. With nobody alive, every room is 1.
    /// </summary>
    public float[] LobbyWeights()
    {
        var result = new float[rooms.RoomCount];

        bool anyAlive = false, anyChaser = false;
        for (int i = 0; i < contenders.Count; i++)
        {
            if (contenders[i].Room < 0) continue;
            anyAlive = true;
            anyChaser |= !leading[i];
        }

        if (!anyAlive)
        {
            for (int room = 0; room < result.Length; room++)
                result[room] = 1f;
            return result;
        }

        float strength = Strength(topScore - secondScore);
        for (int room = 0; room < result.Length; room++)
        {
            int nearest = Unreachable;
            bool occupied = false;
            for (int i = 0; i < contenders.Count; i++)
            {
                if (contenders[i].Room < 0) continue;
                occupied |= contenders[i].Room == room;
                // With only leaders alive, they count as the players to put pickups near.
                if (anyChaser && leading[i]) continue;
                nearest = Math.Min(nearest, StepsFrom(i)[room]);
            }

            if (occupied || nearest < 1 || nearest > weights.near.Length) continue;

            float race = anyChaser ? Race(NearestLeader(-1, room) - (long)nearest) : 1f;
            result[room] = weights.near[nearest - 1] * Mathf.Lerp(1f, race, strength);
        }

        return result;
    }

    /// <summary>Steps from <paramref name="contender"/>'s room to every room, by breadth-first search; worked out once.</summary>
    private int[] StepsFrom(int contender)
    {
        if (steps[contender] != null) return steps[contender];

        var result = new int[rooms.RoomCount];
        for (int i = 0; i < result.Length; i++)
            result[i] = Unreachable;

        int start = contenders[contender].Room;
        if (start >= 0 && start < result.Length)
        {
            var queue = new Queue<int>();
            result[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                foreach (int next in rooms.Adjacent(room))
                {
                    if (result[next] != Unreachable) continue;
                    result[next] = result[room] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        steps[contender] = result;
        return result;
    }

    /// <summary>The fewest steps any living leader other than <paramref name="except"/> takes to <paramref name="room"/>.</summary>
    private int NearestLeader(int except, int room)
    {
        int nearest = Unreachable;
        for (int i = 0; i < contenders.Count; i++)
        {
            if (i == except || !leading[i] || contenders[i].Room < 0) continue;
            nearest = Math.Min(nearest, StepsFrom(i)[room]);
        }
        return nearest;
    }

    /// <summary>The fewest steps any other living player takes to <paramref name="room"/>.</summary>
    private int NearestOther(int except, int room)
    {
        int nearest = Unreachable;
        for (int i = 0; i < contenders.Count; i++)
        {
            if (i == except || contenders[i].Room < 0) continue;
            nearest = Math.Min(nearest, StepsFrom(i)[room]);
        }
        return nearest;
    }

    /// <summary>1 when the player gets there first (or no leader can), less when a leader ties or beats them.</summary>
    private float Race(long margin) => margin >= 1 ? 1f : margin == 0 ? weights.raceTied : weights.raceBehind;

    private float Isolation(int steps) => weights.isolation[Math.Min(steps, weights.isolation.Length - 1)];

    private float Strength(int gap) =>
        Mathf.Lerp(weights.minStrength, 1f, Mathf.Clamp01(gap / (float)Math.Max(1, weights.fullStrengthGap)));
}
