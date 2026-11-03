using System.Collections.Generic;

namespace Arena.LevelGen
{
    /// <summary>The top of a span with at least <c>playerHeight</c> of clearance above it.</summary>
    public struct Surface
    {
        public int X;
        public int Z;
        public float Height;
        /// <summary>Bottom of the next span up, or +infinity under open sky.</summary>
        public float Ceiling;
        /// <summary>Role of the primitive that forms the top. Metrics only count walkable roles.</summary>
        public PrimitiveRole Role;

        public bool HasWalkableRole => PrimitiveRoles.IsWalkable(Role);
    }

    /// <summary>
    /// Every walkable surface in a span grid, indexed column by column (bottom first). A surface's index is
    /// its id everywhere in measurement. Surfaces are geometric: a cover top you can stand on is one, whatever
    /// its role. The movement graph uses all of them; the room metrics filter to walkable roles.
    /// </summary>
    public sealed class SurfaceSet
    {
        public readonly SpanGrid Grid;
        public readonly List<Surface> Surfaces = new();

        private readonly int[] _columnStart;

        public SurfaceSet(SpanGrid grid, float playerHeight)
        {
            Grid = grid;
            _columnStart = new int[grid.CellCount + 1];
            for (int z = 0; z < grid.CellsZ; z++)
            for (int x = 0; x < grid.CellsX; x++)
            {
                _columnStart[x + z * grid.CellsX] = Surfaces.Count;
                var column = grid.GetColumn(x, z);
                for (int i = 0; i < column.Count; i++)
                {
                    float ceiling = i + 1 < column.Count ? column[i + 1].Bottom : float.PositiveInfinity;
                    if (ceiling - column[i].Top < playerHeight) continue;
                    Surfaces.Add(new Surface
                    {
                        X = x, Z = z, Height = column[i].Top, Ceiling = ceiling, Role = column[i].Role,
                    });
                }
            }
            _columnStart[grid.CellCount] = Surfaces.Count;
        }

        public int Count => Surfaces.Count;

        public Surface this[int index] => Surfaces[index];

        /// <summary>Surfaces of column (x, z) are the ids in [start, end).</summary>
        public void ColumnRange(int x, int z, out int start, out int end)
        {
            int c = x + z * Grid.CellsX;
            start = _columnStart[c];
            end = _columnStart[c + 1];
        }

        /// <summary>The surface in column (x, z) whose height is within <paramref name="tolerance"/> of
        /// <paramref name="height"/>, or -1.</summary>
        public int FindAt(int x, int z, float height, float tolerance = 1e-3f)
        {
            ColumnRange(x, z, out int start, out int end);
            for (int i = start; i < end; i++)
                if (System.Math.Abs(Surfaces[i].Height - height) <= tolerance) return i;
            return -1;
        }

        /// <summary>
        /// Whether a player can stand at both heights across the shared edge of two neighbouring columns:
        /// the gap that both columns leave open has to fit the player above the higher surface.
        /// </summary>
        public static bool HasHeadroom(in Surface a, in Surface b, float playerHeight)
        {
            float floor = a.Height > b.Height ? a.Height : b.Height;
            float ceiling = a.Ceiling < b.Ceiling ? a.Ceiling : b.Ceiling;
            return ceiling - floor >= playerHeight;
        }

        public static readonly (int dx, int dz)[] Neighbours4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    }
}
