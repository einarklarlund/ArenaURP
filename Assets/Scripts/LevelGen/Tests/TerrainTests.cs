using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    public class TerrainTests
    {
        private static (GenerationTrace trace, LevelGenSettings s) Generate(ulong seed,
            System.Func<LevelGenSettings, LevelGenSettings> tweak = null)
        {
            var s = LevelGenSettings.Default;
            if (tweak != null) s = tweak(s);
            var trace = new GenerationTrace();
            var result = LevelGenerator.Generate(s, seed, trace);
            Assert.IsTrue(result.Success, result.Reason);
            return (trace, s.Sanitized());
        }

        [Test]
        public void Terrain_IsContinuous_EdgesPinned_AndWalkable([Values(0, 1, 2, 3, 4)] int seed)
        {
            var (trace, s) = Generate((ulong)seed);
            float maxGrade = Mathf.Tan(s.Terrain.MaxGradeDegrees * Mathf.Deg2Rad) + 1e-3f;
            int changed = 0;
            foreach (var terrain in trace.Terrain)
            {
                var room = trace.Partition.Polygons[terrain.Plot];
                // Every corner has one height once a sinkhole's drop is added back: the ground is continuous.
                float depth = s.Terrain.SinkholeDepth;
                var heights = new Dictionary<Vector2, float>();
                for (int t = 0; t < terrain.Triangles.Count; t++)
                {
                    float drop = terrain.Cells[terrain.TriangleCells[t]].Sinkhole ? depth : 0f;
                    var (a, b, c) = terrain.Triangles[t];
                    foreach (var p in new[] { b, c })
                    {
                        var key = new Vector2(p.x, p.z);
                        if (heights.TryGetValue(key, out float other)) Assert.AreEqual(other, p.y + drop, 2e-3f, "continuous");
                        else heights[key] = p.y + drop;
                    }
                }
                foreach (var (a, b, c) in terrain.Triangles)
                {
                    Assert.LessOrEqual(TerrainStage.Gradient((a, b, c), out _).magnitude, maxGrade);
                    foreach (var p in new[] { a, b, c })
                    {
                        if (OnEdge(room, new Vector2(p.x, p.z)) && !IsSinkholeCorner(terrain, p))
                            Assert.AreEqual(terrain.Base.At(new Vector2(p.x, p.z)), p.y, 1e-3f, "room edges are pinned");
                        // Any corner: a tilt leaves its sub-room's site where it was and moves the corners round it.
                        if (Mathf.Abs(p.y - terrain.Base.At(new Vector2(p.x, p.z))) > 0.05f) changed++;
                    }
                }
            }
            Moved += changed;
        }

        private static int Moved;

        [Test]
        public void Terrain_ActuallyMoves()
        {
            // Over a few seeds: one level can have almost all its sub-rooms pinned under slopes, and its operations
            // then move nothing.
            Moved = 0;
            for (int seed = 0; seed < 5; seed++) Terrain_IsContinuous_EdgesPinned_AndWalkable(seed);
            Assert.Greater(Moved, 0, "the terrain actually moves");
        }

        [Test]
        public void Sinkholes_AreLedgedAtTheirDepth([Values(0.5f, 0.9f, 2.5f)] float sinkholeDepth)
        {
            int sinkholes = 0;
            for (ulong seed = 0; seed < 6; seed++)
            {
                var (trace, s) = Generate(seed, t => { t.Terrain.SinkholeWeight = 5f; t.Terrain.SinkholeDepth = sinkholeDepth; return t; });
                float depth = s.Terrain.SinkholeDepth;
                foreach (var terrain in trace.Terrain)
                {
                    // A corner's height in each triangle that has it: a sinkhole's is its depth below its neighbours'.
                    var byCorner = new Dictionary<Vector2, List<(float y, bool sinkhole)>>();
                    for (int t = 0; t < terrain.Triangles.Count; t++)
                    {
                        bool sinkhole = terrain.Cells[terrain.TriangleCells[t]].Sinkhole;
                        var (_, b, c) = terrain.Triangles[t];
                        foreach (var p in new[] { b, c })
                        {
                            var key = new Vector2(p.x, p.z);
                            if (!byCorner.TryGetValue(key, out var list)) byCorner[key] = list = new List<(float, bool)>();
                            list.Add((p.y, sinkhole));
                        }
                    }
                    foreach (var cell in terrain.Cells)
                    {
                        if (!cell.Sinkhole) continue;
                        sinkholes++;
                        Assert.IsFalse(cell.OnEdge, "sinkholes stay off the room's edges");
                    }
                    foreach (var list in byCorner.Values)
                        foreach (var (y, sinkhole) in list)
                            foreach (var (other, otherSinkhole) in list)
                                if (sinkhole && !otherSinkhole) Assert.AreEqual(depth, other - y, 1e-3f, "a ledge round the rim");
                }
            }
            Assert.Greater(sinkholes, 0);
        }

        [Test]
        public void SubRooms_TileTheirRoom_MoreForBiggerRooms([Values(0, 1, 2)] int seed)
        {
            var (trace, s) = Generate((ulong)seed);
            foreach (var terrain in trace.Terrain)
            {
                var room = trace.Partition.Polygons[terrain.Plot];
                float area = 0f;
                foreach (var cell in terrain.Cells)
                {
                    Assert.IsTrue(Polygon.IsConvex(cell.Polygon));
                    area += Polygon.Area(cell.Polygon);
                }
                Assert.AreEqual(Polygon.Area(room), area, 0.05f);
                Assert.AreEqual(Mathf.Max(1, Mathf.RoundToInt(Polygon.Area(room) / s.Terrain.SubRoomArea)), terrain.Cells.Count, 0.01f);
            }
        }

        [Test]
        public void NoOperations_LeaveThePlane()
        {
            var (trace, _) = Generate(1UL, t => { t.Terrain.MinOperations = 0; t.Terrain.MaxOperations = 0; return t; });
            foreach (var terrain in trace.Terrain)
                foreach (var (a, _, _) in terrain.Triangles)
                    Assert.AreEqual(terrain.Base.At(new Vector2(a.x, a.z)), a.y, 1e-3f);
        }

        private static bool IsSinkholeCorner(TerrainPlan terrain, Vector3 p)
        {
            foreach (var cell in terrain.Cells)
                if (cell.Sinkhole && cell.Polygon.Contains(new Vector2(p.x, p.z))) return true;
            return false;
        }

        private static bool OnEdge(List<Vector2> polygon, Vector2 p)
        {
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var ab = polygon[(k + 1) % polygon.Count] - a;
                if (ab.sqrMagnitude < 1e-12f) continue;
                float t = Vector2.Dot(p - a, ab) / ab.sqrMagnitude;
                if (t < -1e-4f || t > 1f + 1e-4f) continue;
                if (Mathf.Abs(ab.x * (p - a).y - ab.y * (p - a).x) / ab.magnitude < 1e-3f) return true;
            }
            return false;
        }
    }
}
