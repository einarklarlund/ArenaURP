using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A grid cell a primitive covers, with the height of the primitive's top surface above that cell.</summary>
    public struct CoveredCell
    {
        public int X;
        public int Z;
        public float Top;
    }

    /// <summary>
    /// Turns primitives into spans. A cell is covered when its centre lies inside the primitive's
    /// yawed footprint (a Prism's polygon; a thin blocker counts as wide enough to block, see below); the span runs from the primitive's bottom to its top surface above that
    /// centre, which for a Prism is its top plane evaluated there.
    /// </summary>
    public static class Rasterizer
    {
        public static SpanGrid Rasterize(LevelDescription description, float cellSize)
        {
            var grid = new SpanGrid(description.Size.x, description.Size.z, cellSize);
            var cells = new List<CoveredCell>();
            foreach (var p in description.Primitives)
            {
                CoveredCells(p, description, grid, cells);
                foreach (var c in cells) grid.AddSpan(c.X, c.Z, p.Position.y, c.Top, p.Role);
            }
            return grid;
        }

        /// <summary>
        /// Fills <paramref name="into"/> (cleared first) with the cells of <paramref name="grid"/> that the primitive
        /// covers. <paramref name="level"/> supplies a Prism's polygon.
        /// </summary>
        public static void CoveredCells(in Primitive p, LevelDescription level, SpanGrid grid, List<CoveredCell> into)
        {
            into.Clear();
            if (p.Shape == PrimitiveShape.Prism)
            {
                var polygon = level.Polygons[p.Polygon];
                PrismCells(p, PrimitiveRoles.IsBlocker(p.Role) ? Thickened(polygon, grid.CellSize) : polygon, grid, into);
                return;
            }
            double yaw = p.YawDegrees * (Math.PI / 180.0);
            float cos = (float)Math.Cos(yaw);
            float sin = (float)Math.Sin(yaw);
            float halfX = p.Size.x * 0.5f;
            float halfZ = p.Size.z * 0.5f;
            if (PrimitiveRoles.IsBlocker(p.Role))
            {
                // A blocker thinner than this could fall between cell centres and leave a gap a player walks through
                // in the grid. Across a band whose normal is n, two neighbouring centres are cellSize·max(|nx|, |nz|)
                // apart, so a band at least that wide always holds one of them.
                float minHalf = 0.5f * grid.CellSize * Mathf.Max(Mathf.Abs(cos), Mathf.Abs(sin));
                halfX = Mathf.Max(halfX, minHalf);
                halfZ = Mathf.Max(halfZ, minHalf);
            }

            // World-space bounds of the yawed footprint.
            float extentX = Mathf.Abs(halfX * cos) + Mathf.Abs(halfZ * sin);
            float extentZ = Mathf.Abs(halfX * sin) + Mathf.Abs(halfZ * cos);
            int minX = Mathf.Max(0, Mathf.FloorToInt((p.Position.x - extentX) / grid.CellSize));
            int maxX = Mathf.Min(grid.CellsX - 1, Mathf.FloorToInt((p.Position.x + extentX) / grid.CellSize));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((p.Position.z - extentZ) / grid.CellSize));
            int maxZ = Mathf.Min(grid.CellsZ - 1, Mathf.FloorToInt((p.Position.z + extentZ) / grid.CellSize));

            for (int z = minZ; z <= maxZ; z++)
            {
                float dz = grid.CellCenterZ(z) - p.Position.z;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = grid.CellCenterX(x) - p.Position.x;
                    // Inverse of the yaw rotation: world offset -> local axes.
                    float localX = dx * cos - dz * sin;
                    float localZ = dx * sin + dz * cos;
                    if (Mathf.Abs(localX) > halfX || Mathf.Abs(localZ) > halfZ) continue;

                    into.Add(new CoveredCell { X = x, Z = z, Top = p.Position.y + LocalTopHeight(p, localZ) });
                }
            }
        }

        /// <summary>
        /// A blocker prism as wide as a box blocker is made (see <see cref="CoveredCells"/>): across its narrowest
        /// direction n, and then along it, a band narrower than cellSize·max(|nx|, |nz|) is widened to that on both sides
        /// (the polygon swept along the direction), so a wall made a prism can't fall between cell centres either.
        /// </summary>
        private static List<Vector2> Thickened(List<Vector2> polygon, float cellSize)
        {
            if (polygon.Count < 3) return polygon;
            int thin = -1;
            float width = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var edge = polygon[(i + 1) % polygon.Count] - a;
                if (edge.sqrMagnitude < 1e-12f) continue;
                var n = new Vector2(-edge.y, edge.x).normalized;
                Polygon.Extent(polygon, n, out float min, out float max);
                if (max - min < width)
                {
                    width = max - min;
                    thin = i;
                }
            }
            if (thin < 0) return polygon;
            var along = (polygon[(thin + 1) % polygon.Count] - polygon[thin]).normalized;
            var across = new Vector2(-along.y, along.x);
            var result = polygon;
            foreach (var n in new[] { across, along })
            {
                Polygon.Extent(result, n, out float min, out float max);
                // A hair over, as a box's test keeps a centre exactly at its widened face.
                float wide = cellSize * Mathf.Max(Mathf.Abs(n.x), Mathf.Abs(n.y)) + 1e-4f;
                if (max - min >= wide) continue;
                var shift = n * ((wide - (max - min)) * 0.5f);
                var points = new List<Vector2>(2 * result.Count);
                foreach (var v in result)
                {
                    points.Add(v - shift);
                    points.Add(v + shift);
                }
                result = Polygon.Hull(points);
            }
            return result;
        }

        private static void PrismCells(in Primitive p, List<Vector2> polygon, SpanGrid grid, List<CoveredCell> into)
        {
            if (polygon.Count < 3) return;
            float minX = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            foreach (var v in polygon)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.y);
                maxZ = Mathf.Max(maxZ, v.y);
            }
            int x0 = Mathf.Max(0, Mathf.FloorToInt(minX / grid.CellSize));
            int x1 = Mathf.Min(grid.CellsX - 1, Mathf.FloorToInt(maxX / grid.CellSize));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(minZ / grid.CellSize));
            int z1 = Mathf.Min(grid.CellsZ - 1, Mathf.FloorToInt(maxZ / grid.CellSize));

            for (int z = z0; z <= z1; z++)
            {
                float cz = grid.CellCenterZ(z);
                for (int x = x0; x <= x1; x++)
                {
                    float cx = grid.CellCenterX(x);
                    if (!Polygon.Contains(polygon, cx, cz)) continue;
                    into.Add(new CoveredCell { X = x, Z = z, Top = p.PrismTop(cx, cz) });
                }
            }
        }

        /// <summary>Height of the primitive's top surface above its bottom, at local z in [-Size.z/2, Size.z/2].</summary>
        public static float LocalTopHeight(in Primitive p, float localZ)
        {
            float t = p.Size.z > 0f ? Mathf.Clamp01(localZ / p.Size.z + 0.5f) : 1f;
            switch (p.Shape)
            {
                case PrimitiveShape.Ramp:
                    return p.Size.y * t;
                case PrimitiveShape.Stairs:
                    int steps = Mathf.Max(1, p.Steps);
                    int step = Mathf.Min(steps - 1, (int)(t * steps));
                    return p.Size.y * (step + 1) / steps;
                default:
                    return p.Size.y;
            }
        }
    }
}
