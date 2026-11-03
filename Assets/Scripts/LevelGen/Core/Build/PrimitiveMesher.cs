using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Appends flat-shaded triangles for one primitive to shared vertex and index lists, in level space.
    /// The shapes match what <see cref="Rasterizer"/> measures.
    /// </summary>
    public static class PrimitiveMesher
    {
        /// <summary>A face is mapped from above (or below) when its normal is at least this far from horizontal.</summary>
        public const float FlatFaceY = 0.5f;

        /// <summary>
        /// Texture coordinates in world units, so a square texture tiles once per 1 × 1 unit on every surface. A face
        /// that faces up or down (|n.y| ≥ <see cref="FlatFaceY"/>, slopes to 60°) is projected from above: u = x,
        /// v = z (mirrored on undersides so they read the right way round from below). The tiling then runs on unbroken
        /// across terrain triangles, plots and ramps, at exactly 1:1 on flat ground and stretched by 1/cos(slope)
        /// along a slope. Any other face is mapped on its own plane: u is the distance along it horizontally (left to
        /// right, seen from outside) and v the height, so walls are exactly 1:1 and the tiling lines up along a wall
        /// and from floor to floor.
        /// </summary>
        public static Vector2 WorldUv(Vector3 position, Vector3 normal)
        {
            if (normal.y >= FlatFaceY) return new Vector2(position.x, position.z);
            if (normal.y <= -FlatFaceY) return new Vector2(position.x, -position.z);
            var right = new Vector2(-normal.z, normal.x).normalized;
            return new Vector2(right.x * position.x + right.y * position.z, position.y);
        }

        /// <summary><paramref name="level"/> supplies a Prism's polygon.</summary>
        public static void Append(in Primitive p, LevelDescription level, List<Vector3> vertices, List<Vector3> normals,
            List<int> triangles) =>
            Append(p, level, vertices, normals, _ => triangles);

        /// <summary>As above, with each face's triangles going to the index list <paramref name="trianglesFor"/> gives
        /// for its <see cref="SurfaceTag"/> (see <see cref="Surfaces"/> for which face is which), or left out where it
        /// gives null. It is called once per triangle, in order.</summary>
        public static void Append(in Primitive p, LevelDescription level, List<Vector3> vertices, List<Vector3> normals,
            Func<SurfaceTag, List<int>> trianglesFor)
        {
            if (p.Shape == PrimitiveShape.Prism)
            {
                new Writer(Vector3.zero, Quaternion.identity, vertices, normals, trianglesFor, p.Surfaces)
                    .Prism(p, level.Polygons[p.Polygon]);
                return;
            }

            var mesher = new Writer(p.Position, Quaternion.Euler(0f, p.YawDegrees, 0f), vertices, normals, trianglesFor,
                p.Surfaces);
            float hx = p.Size.x * 0.5f;
            float hz = p.Size.z * 0.5f;
            float h = p.Size.y;

            switch (p.Shape)
            {
                case PrimitiveShape.Ramp:
                    mesher.Wedge(hx, hz, h);
                    break;
                case PrimitiveShape.Stairs:
                    int steps = Mathf.Max(1, p.Steps);
                    float run = p.Size.z / steps;
                    for (int i = 0; i < steps; i++)
                    {
                        float z0 = -hz + i * run;
                        mesher.Box(-hx, hx, 0f, h * (i + 1) / steps, z0, z0 + run);
                    }
                    break;
                default:
                    mesher.Box(-hx, hx, 0f, h, -hz, hz);
                    break;
            }
        }

        private readonly struct Writer
        {
            private readonly Vector3 _origin;
            private readonly Quaternion _rotation;
            private readonly List<Vector3> _vertices;
            private readonly List<Vector3> _normals;
            private readonly Func<SurfaceTag, List<int>> _trianglesFor;
            private readonly Surfaces _surfaces;

            public Writer(Vector3 origin, Quaternion rotation, List<Vector3> vertices, List<Vector3> normals,
                Func<SurfaceTag, List<int>> trianglesFor, Surfaces surfaces)
            {
                _origin = origin;
                _rotation = rotation;
                _vertices = vertices;
                _normals = normals;
                _trianglesFor = trianglesFor;
                _surfaces = surfaces;
            }

            public void Box(float x0, float x1, float y0, float y1, float z0, float z1)
            {
                var s = _surfaces;
                Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), Vector3.down, s.Bottom);
                Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.up, s.Top);
                Quad(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0), Vector3.back, s.Ends);
                Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), Vector3.forward, s.Ends);
                Quad(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0), Vector3.left, s.Back);
                Quad(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), Vector3.right, s.Front);
            }

            /// <summary>Rises from height 0 at -hz to height h at +hz.</summary>
            public void Wedge(float hx, float hz, float h)
            {
                var b0 = new Vector3(-hx, 0f, -hz);
                var b1 = new Vector3(hx, 0f, -hz);
                var b2 = new Vector3(hx, 0f, hz);
                var b3 = new Vector3(-hx, 0f, hz);
                var t2 = new Vector3(hx, h, hz);
                var t3 = new Vector3(-hx, h, hz);
                var s = _surfaces;
                Quad(b0, b1, b2, b3, Vector3.down, s.Bottom);
                Quad(b3, b2, t2, t3, Vector3.forward, s.Ends);
                Quad(b0, t3, t2, b1, new Vector3(0f, 2f * hz, -h), s.Top);
                Triangle(b0, b3, t3, Vector3.left, s.Back);
                Triangle(b1, t2, b2, Vector3.right, s.Front);
            }

            /// <summary>
            /// A convex counter-clockwise polygon (level space) from the prism's bottom up to its top plane: fans for
            /// the bottom and top, and one quad per edge for the sides (front, or back for those in BackSides).
            /// </summary>
            public void Prism(in Primitive p, List<Vector2> polygon)
            {
                int n = polygon.Count;
                if (n < 3) return;
                float bottom = p.Position.y;
                var tops = new Vector3[n];
                var bottoms = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    var v = polygon[i];
                    tops[i] = new Vector3(v.x, p.PrismTop(v.x, v.y), v.y);
                    bottoms[i] = new Vector3(v.x, bottom, v.y);
                }

                var up = new Vector3(-p.Slope.x, 1f, -p.Slope.y);
                for (int i = 1; i + 1 < n; i++)
                {
                    Triangle(tops[0], tops[i], tops[i + 1], up, _surfaces.Top);
                    Triangle(bottoms[0], bottoms[i], bottoms[i + 1], Vector3.down, _surfaces.Bottom);
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    var edge = polygon[j] - polygon[i];
                    var tag = (p.BackSides >> i & 1u) != 0 ? _surfaces.Back : _surfaces.Front;
                    Quad(bottoms[i], bottoms[j], tops[j], tops[i], new Vector3(edge.y, 0f, -edge.x), tag);
                }
            }

            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, SurfaceTag tag)
            {
                Triangle(a, b, c, outward, tag);
                Triangle(a, c, d, outward, tag);
            }

            /// <summary>Adds one triangle facing <paramref name="outward"/> (local), fixing the winding if needed.</summary>
            private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, SurfaceTag tag)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) return;
                if (Vector3.Dot(normal, outward) < 0f) (b, c) = (c, b);

                var triangles = _trianglesFor(tag);
                if (triangles == null) return;
                var worldNormal = _rotation * outward.normalized;
                int start = _vertices.Count;
                _vertices.Add(_origin + _rotation * a);
                _vertices.Add(_origin + _rotation * b);
                _vertices.Add(_origin + _rotation * c);
                _normals.Add(worldNormal);
                _normals.Add(worldNormal);
                _normals.Add(worldNormal);
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }
        }
    }
}
