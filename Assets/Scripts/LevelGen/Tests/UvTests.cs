using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Arena.LevelGen.Tests
{
    /// <summary>Texture coordinates are in world units: one tile of a square texture per 1 × 1 unit.</summary>
    public class UvTests
    {
        private static LevelDescription Generate(ulong seed)
        {
            var result = LevelGenerator.Generate(LevelGenSettings.Default, seed);
            Assert.IsTrue(result.Success, result.Reason);
            return result.Level;
        }

        private static float Area(Vector3 a, Vector3 b, Vector3 c) => Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        private static float Area(Vector2 a, Vector2 b, Vector2 c) => Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;

        /// <summary>
        /// Every triangle of the terrain and buildings: a wall's UVs cover exactly its area, a flat floor's exactly its
        /// area, and a slope's its area foreshortened by cos(slope) (it is projected from above).
        /// </summary>
        [Test]
        public void LevelFaces_TileOncePerUnit([Values(0, 1, 2)] int seed)
        {
            var level = Generate((ulong)seed);
            int walls = 0, floors = 0, slopes = 0;
            foreach (var p in level.Primitives)
            {
                if (p.OwnerKind == OwnerKind.Prop) continue;
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var triangles = new List<int>();
                PrimitiveMesher.Append(p, level, vertices, normals, triangles);
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                    var n = normals[triangles[t]];
                    float world = Area(a, b, c);
                    if (world < 1e-4f) continue;
                    float uv = Area(PrimitiveMesher.WorldUv(a, n), PrimitiveMesher.WorldUv(b, n), PrimitiveMesher.WorldUv(c, n));
                    float expected = Mathf.Abs(n.y) >= PrimitiveMesher.FlatFaceY ? world * Mathf.Abs(n.y) : world * Mathf.Sqrt(1f - n.y * n.y);
                    Assert.AreEqual(expected, uv, 1e-3f * Mathf.Max(1f, world), $"{p.OwnerKind} {p.Role} face {n}");
                    if (Mathf.Abs(n.y) < 1e-4f) walls++;
                    else if (Mathf.Abs(n.y) > 0.9999f) floors++;
                    else slopes++;
                }
            }
            Assert.Greater(walls, 0);
            Assert.Greater(floors, 0);
            Assert.Greater(slopes, 0);
        }

        [Test]
        public void UnitSquares_GetOneTile()
        {
            // A flat 1 × 1 square anywhere on the ground, and one on a wall facing any way.
            var ground = new[] { new Vector3(10.3f, 2f, 4.6f), new Vector3(11.3f, 2f, 4.6f), new Vector3(10.3f, 2f, 5.6f) };
            var g0 = PrimitiveMesher.WorldUv(ground[0], Vector3.up);
            Assert.AreEqual(1f, (PrimitiveMesher.WorldUv(ground[1], Vector3.up) - g0).magnitude, 1e-5f);
            Assert.AreEqual(1f, (PrimitiveMesher.WorldUv(ground[2], Vector3.up) - g0).magnitude, 1e-5f);

            for (float yaw = 0f; yaw < 360f; yaw += 37f)
            {
                var normal = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var along = Vector3.Cross(Vector3.up, normal);
                var corner = new Vector3(7f, 1f, 3f);
                var w0 = PrimitiveMesher.WorldUv(corner, normal);
                var w1 = PrimitiveMesher.WorldUv(corner + along, normal);
                var w2 = PrimitiveMesher.WorldUv(corner + Vector3.up, normal);
                Assert.AreEqual(1f, Mathf.Abs(w1.x - w0.x), 1e-5f, $"along a wall facing {yaw}°");
                Assert.AreEqual(0f, w1.y - w0.y, 1e-5f);
                Assert.AreEqual(1f, w2.y - w0.y, 1e-5f, "up a wall");
                Assert.AreEqual(0f, w2.x - w0.x, 1e-5f);
            }
        }

        [Test]
        public void Ground_IsSeamless_AcrossTriangles([Values(0, 1)] int seed)
        {
            // Wherever two upward faces share a corner, they give it the same UV: the texture runs on unbroken.
            var level = Generate((ulong)seed);
            var seen = new Dictionary<Vector3, Vector2>();
            int shared = 0;
            foreach (var p in level.Primitives)
            {
                if (p.OwnerKind is not (OwnerKind.Plot or OwnerKind.Transition)) continue;
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                PrimitiveMesher.Append(p, level, vertices, normals, new List<int>());
                for (int i = 0; i < vertices.Count; i++)
                {
                    if (normals[i].y < PrimitiveMesher.FlatFaceY) continue;
                    var uv = PrimitiveMesher.WorldUv(vertices[i], normals[i]);
                    if (seen.TryGetValue(vertices[i], out var other))
                    {
                        Assert.AreEqual(other.x, uv.x, 1e-5f);
                        Assert.AreEqual(other.y, uv.y, 1e-5f);
                        shared++;
                    }
                    else seen[vertices[i]] = uv;
                }
            }
            Assert.Greater(shared, 0);
        }

        [Test]
        public void Decals_TileOncePerUnit()
        {
            var level = Generate(2);
            var strips = DecalStage.Build(level, DecalSettings.Default);
            int checkedStrips = 0;
            foreach (var strip in strips)
            {
                var vertices = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                DecalMesher.Append(strip, vertices, normals, uvs, new List<int>());
                if (vertices.Count < 4) continue;
                // Across: the edges' v differ by the strip's width, measured square to it (a mitred end runs on the slant);
                // along: u grows by the distance travelled.
                var direction = (vertices[2] - vertices[0]).normalized;
                var span = vertices[1] - vertices[0];
                float across = (span - Vector3.Dot(span, direction) * direction).magnitude;
                Assert.AreEqual(across, Mathf.Abs(uvs[1].y - uvs[0].y), 0.02f * Mathf.Max(1f, across), $"{strip.Type} across");
                float along = (strip.Points[1] - strip.Points[0]).magnitude;
                Assert.AreEqual(along, uvs[2].x - uvs[0].x, 1e-4f, $"{strip.Type} along");
                checkedStrips++;
            }
            Assert.Greater(checkedStrips, 0);
        }
    }
}
