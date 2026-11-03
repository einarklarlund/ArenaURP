using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>Turns decal strips into quads: lifted <see cref="Lift"/> along each strip's normal, with texture
    /// coordinates in world units like the level's own (<see cref="PrimitiveMesher.WorldUv"/>): u is the distance along
    /// the strip, v the distance across it, so a square texture tiles once per 1 × 1 unit of decal.</summary>
    public static class DecalMesher
    {
        public const float Lift = 0.01f;

        public static void Append(DecalStrip strip, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
            List<int> triangles)
        {
            int n = Mathf.Min(strip.Left.Count, strip.Right.Count);
            if (n < 2 || strip.Width <= 0f) return;
            var lift = strip.Normal * Lift;
            float u = 0f;
            int start = vertices.Count;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) u += (strip.Points[i] - strip.Points[i - 1]).magnitude;
                vertices.Add(strip.Left[i] + lift);
                vertices.Add(strip.Right[i] + lift);
                normals.Add(strip.Normal);
                normals.Add(strip.Normal);
                uvs.Add(new Vector2(u, strip.V0 * strip.Width));
                uvs.Add(new Vector2(u, strip.V1 * strip.Width));
            }
            for (int i = 0; i + 1 < n; i++)
            {
                int l0 = start + 2 * i, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                // Wind each triangle to face along the normal.
                bool flip = Vector3.Dot(Vector3.Cross(vertices[r0] - vertices[l0], vertices[l1] - vertices[l0]), strip.Normal) < 0f;
                if (flip)
                {
                    triangles.Add(l0); triangles.Add(l1); triangles.Add(r0);
                    triangles.Add(r0); triangles.Add(l1); triangles.Add(r1);
                }
                else
                {
                    triangles.Add(l0); triangles.Add(r0); triangles.Add(l1);
                    triangles.Add(r0); triangles.Add(r1); triangles.Add(l1);
                }
            }
        }
    }
}
