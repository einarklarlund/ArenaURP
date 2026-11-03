using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// The level's rooms for gameplay: a room is a plot (its id is the plot's index), a building and its floors
    /// included. Answers which room a position is in, which rooms neighbour a room, and the chest sites and pickup
    /// lines in each. Built from a description; plain C#, so the server and the tests use the same lookup.
    /// </summary>
    public sealed class RoomGraph
    {
        /// <summary>A position this close outside every plot (a seam, a wall's foot) counts as the nearest plot.</summary>
        public const float SeamTolerance = 1f;

        private static readonly List<ChestSite> NoSites = new();
        private static readonly List<PickupLine> NoLines = new();
        private static readonly List<int> NoRooms = new();

        private readonly LevelDescription _level;
        private readonly List<int>[] _adjacent;
        private readonly List<int>[] _linked;
        private readonly List<ChestSite>[] _sites;
        private readonly List<PickupLine>[] _lines;
        private readonly Vector2[] _centres;

        public RoomGraph(LevelDescription level)
        {
            _level = level;
            int n = level.Plots.Count;
            _adjacent = new List<int>[n];
            _linked = new List<int>[n];
            _sites = new List<ChestSite>[n];
            _lines = new List<PickupLine>[n];
            _centres = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                _adjacent[i] = new List<int>();
                _linked[i] = new List<int>();
                _sites[i] = new List<ChestSite>();
                _lines[i] = new List<PickupLine>();
                _centres[i] = Polygon.Centroid(level.PolygonOf(level.Plots[i]));
            }
            foreach (var link in level.RoomLinks)
            {
                if (!Valid(link.RoomA) || !Valid(link.RoomB)) continue;
                _linked[link.RoomA].Add(link.RoomB);
                _linked[link.RoomB].Add(link.RoomA);
                if (link.AToB) _adjacent[link.RoomA].Add(link.RoomB);
                if (link.BToA) _adjacent[link.RoomB].Add(link.RoomA);
            }
            foreach (var site in level.ChestSites)
                if (Valid(site.Room)) _sites[site.Room].Add(site);
            foreach (var line in level.PickupLines)
                if (Valid(line.Room)) _lines[line.Room].Add(line);
        }

        public LevelDescription Level => _level;
        public int RoomCount => _sites.Length;

        /// <summary>The room under <paramref name="position"/> (its height is ignored), or -1 outside the level.</summary>
        public int RoomAt(Vector3 position) => Locate(_level, new Vector2(position.x, position.z));

        /// <summary>The rooms a player can walk, jump or drop into from <paramref name="room"/>.</summary>
        public IReadOnlyList<int> Adjacent(int room) => Valid(room) ? _adjacent[room] : NoRooms;

        /// <summary>Every room sharing a transition with <paramref name="room"/>, one-way and blocked ones included.</summary>
        public IReadOnlyList<int> Linked(int room) => Valid(room) ? _linked[room] : NoRooms;

        public IReadOnlyList<ChestSite> ChestSitesIn(int room) => Valid(room) ? _sites[room] : NoSites;

        public IReadOnlyList<PickupLine> PickupLinesIn(int room) => Valid(room) ? _lines[room] : NoLines;

        /// <summary>The room's plot centroid, on the XZ plane.</summary>
        public Vector2 Centre(int room) => _centres[room];

        private bool Valid(int room) => room >= 0 && room < _sites.Length;

        /// <summary>The plot whose outline holds <paramref name="p"/>; else the nearest one within
        /// <see cref="SeamTolerance"/>; else -1.</summary>
        public static int Locate(LevelDescription level, Vector2 p)
        {
            for (int i = 0; i < level.Plots.Count; i++)
                if (Polygon.Contains(level.PolygonOf(level.Plots[i]), p)) return i;

            int best = -1;
            float bestDistance = SeamTolerance * SeamTolerance;
            for (int i = 0; i < level.Plots.Count; i++)
            {
                var polygon = level.PolygonOf(level.Plots[i]);
                for (int k = 0, j = polygon.Count - 1; k < polygon.Count; j = k++)
                {
                    float d = SquaredDistanceToSegment(p, polygon[j], polygon[k]);
                    if (d <= bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }
            }
            return best;
        }

        private static float SquaredDistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }
    }
}
