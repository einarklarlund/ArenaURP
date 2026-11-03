using System.Collections.Generic;
using Arena.LevelGen;

/// <summary>The match's players as <see cref="SiteScorer"/> sees them, for the directors. Server only.</summary>
public static class MatchStandings
{
    /// <summary>
    /// A scorer over every active player, in the order of <paramref name="players"/>, which this fills: a player's
    /// contender index is their index there.
    /// </summary>
    public static SiteScorer Scorer(MatchManager match, RoomGraph rooms, SiteWeights weights, List<NetworkPlayer> players)
    {
        players.Clear();
        var contenders = new List<Contender>();
        foreach (NetworkPlayer player in match.ActivePlayers)
        {
            if (player == null) continue;

            Pawn pawn = player.ControlledPawn.Value;
            int room = pawn != null && pawn.IsSpawned ? rooms.RoomAt(pawn.transform.position) : -1;
            players.Add(player);
            contenders.Add(new Contender(room, player.Score.Value));
        }

        return new SiteScorer(rooms, contenders, weights);
    }
}
