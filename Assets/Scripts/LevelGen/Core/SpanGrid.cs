using System.Collections.Generic;
using System.IO;

namespace Arena.LevelGen
{
    /// <summary>A solid vertical interval in one column. Role is the role of the span's top surface.</summary>
    public struct Span
    {
        public float Bottom;
        public float Top;
        public PrimitiveRole Role;
    }

    /// <summary>
    /// The measurement representation: a 2D grid of columns over the level's XZ extent, each holding a
    /// bottom-to-top list of disjoint solid spans (the same idea as Recast's heightfield). Cell (x, z)
    /// covers level x in [x·CellSize, (x+1)·CellSize), and likewise for z.
    /// </summary>
    public sealed class SpanGrid
    {
        public readonly int CellsX;
        public readonly int CellsZ;
        public readonly float CellSize;

        /// <summary>Spans closer than this count as touching, so float seams between stacked primitives merge.</summary>
        public const float MergeTolerance = 1e-3f;

        private readonly List<Span>[] _columns;
        private readonly uint[] _roleMasks;

        public SpanGrid(float width, float depth, float cellSize)
        {
            CellSize = cellSize;
            CellsX = (int)System.Math.Ceiling(width / cellSize);
            CellsZ = (int)System.Math.Ceiling(depth / cellSize);
            _columns = new List<Span>[CellsX * CellsZ];
            _roleMasks = new uint[CellsX * CellsZ];
        }

        public int CellCount => CellsX * CellsZ;

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < CellsX && z < CellsZ;

        /// <summary>The column's spans, bottom first. Empty when nothing was rasterized into it.</summary>
        public IReadOnlyList<Span> GetColumn(int x, int z) =>
            (IReadOnlyList<Span>)_columns[x + z * CellsX] ?? System.Array.Empty<Span>();

        /// <summary>
        /// Every role rasterized into the column, as bits <c>1 &lt;&lt; (int)role</c>. Merging spans keeps only
        /// the top role, so this is how to ask whether a column holds, say, a blocker at any height.
        /// </summary>
        public uint GetRoleMask(int x, int z) => _roleMasks[x + z * CellsX];

        public bool HasBlocker(int x, int z) => (GetRoleMask(x, z) & PrimitiveRoles.BlockerMask) != 0;

        public float CellCenterX(int x) => (x + 0.5f) * CellSize;
        public float CellCenterZ(int z) => (z + 0.5f) * CellSize;

        /// <summary>
        /// Adds a solid interval, merging it with every span it overlaps or touches (within
        /// <see cref="MergeTolerance"/>). The merged span takes the
        /// role of whichever part reaches highest, since its top is the surface that matters; on a tie the
        /// span already there keeps its role.
        /// </summary>
        public void AddSpan(int x, int z, float bottom, float top, PrimitiveRole role)
        {
            if (top <= bottom || !InBounds(x, z)) return;
            int index = x + z * CellsX;
            var column = _columns[index] ??= new List<Span>(2);
            _roleMasks[index] |= 1u << (int)role;

            var merged = new Span { Bottom = bottom, Top = top, Role = role };
            int insertAt = 0;
            for (int i = 0; i < column.Count;)
            {
                var s = column[i];
                if (s.Top < merged.Bottom - MergeTolerance)
                {
                    insertAt = ++i;
                    continue;
                }
                if (s.Bottom > merged.Top + MergeTolerance) break;

                if (s.Bottom < merged.Bottom) merged.Bottom = s.Bottom;
                if (s.Top >= merged.Top)
                {
                    merged.Top = s.Top;
                    merged.Role = s.Role;
                }
                column.RemoveAt(i);
            }
            column.Insert(insertAt, merged);
        }

        public int CountNonEmptyColumns()
        {
            int n = 0;
            foreach (var c in _columns) if (c is { Count: > 0 }) n++;
            return n;
        }

        public int CountSpans()
        {
            int n = 0;
            foreach (var c in _columns) if (c != null) n += c.Count;
            return n;
        }

        public ulong ComputeHash()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream))
            {
                w.Write(CellsX); w.Write(CellsZ); w.Write(CellSize);
                foreach (var c in _columns)
                {
                    int count = c?.Count ?? 0;
                    w.Write(count);
                    for (int i = 0; i < count; i++)
                    {
                        w.Write(c[i].Bottom); w.Write(c[i].Top); w.Write((byte)c[i].Role);
                    }
                }
            }
            return StableHash.Fnv1a64(stream.ToArray());
        }
    }
}
