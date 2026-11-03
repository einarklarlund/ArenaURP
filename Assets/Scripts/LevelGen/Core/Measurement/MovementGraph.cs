using System.Collections.Generic;

namespace Arena.LevelGen
{
    public enum MoveKind : byte
    {
        Walk = 0,
        Jump = 1,
        Drop = 2,
    }

    public struct MoveEdge
    {
        public int From;
        public int To;
        public MoveKind Kind;
    }

    /// <summary>
    /// How a player can move between neighbouring surfaces, over every surface in the level (not just
    /// walkable roles: you can jump onto cover). For a height difference Δ across a shared edge with headroom:
    /// |Δ| ≤ step walks both ways, |Δ| ≤ jump jumps both ways, Δ ≤ maxSafeDrop drops one way down.
    /// <para>
    /// The edges are 4-neighbour. <see cref="Distances"/> adds diagonal steps for path lengths: a diagonal step
    /// a → b is allowed when both of its orthogonal neighbours can be passed through, a → c → b, so it never cuts a
    /// corner the 4-neighbour moves couldn't round.
    /// </para>
    /// </summary>
    public sealed class MovementGraph
    {
        public readonly List<MoveEdge> Edges = new();

        private readonly SurfaceSet _surfaces;
        private readonly List<int>[] _out;
        private readonly List<int>[] _in;
        private List<int>[] _diagonal;

        public MovementGraph(SurfaceSet surfaces, in PlayerConstants player)
        {
            _surfaces = surfaces;
            var grid = surfaces.Grid;
            _out = new List<int>[surfaces.Count];
            _in = new List<int>[surfaces.Count];
            for (int i = 0; i < surfaces.Count; i++)
            {
                _out[i] = new List<int>(4);
                _in[i] = new List<int>(4);
            }

            for (int s = 0; s < surfaces.Count; s++)
            {
                var a = surfaces[s];
                foreach (var (dx, dz) in SurfaceSet.Neighbours4)
                {
                    int nx = a.X + dx, nz = a.Z + dz;
                    if (!grid.InBounds(nx, nz)) continue;
                    surfaces.ColumnRange(nx, nz, out int start, out int end);
                    for (int t = start; t < end; t++)
                    {
                        var b = surfaces[t];
                        if (!SurfaceSet.HasHeadroom(a, b, player.PlayerHeight)) continue;
                        float rise = b.Height - a.Height;
                        float magnitude = System.Math.Abs(rise);
                        // Each unordered pair is seen from both sides; add the a -> b direction only.
                        if (magnitude <= player.StepHeight) Add(s, t, MoveKind.Walk);
                        else if (magnitude <= player.JumpHeight) Add(s, t, MoveKind.Jump);
                        else if (rise < 0f && -rise <= player.MaxSafeDrop) Add(s, t, MoveKind.Drop);
                    }
                }
            }
        }

        public IReadOnlyList<int> Successors(int surface) => _out[surface];
        public IReadOnlyList<int> Predecessors(int surface) => _in[surface];

        /// <summary>Surfaces reachable from <paramref name="start"/> along edges (or against them when
        /// <paramref name="reverse"/>), optionally only through surfaces where <paramref name="within"/> is true.</summary>
        public bool[] Reach(int start, bool reverse = false, bool[] within = null)
        {
            var seen = new bool[_out.Length];
            if (start < 0) return seen;
            var queue = new Queue<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                foreach (int t in reverse ? _in[s] : _out[s])
                {
                    if (seen[t] || (within != null && !within[t])) continue;
                    seen[t] = true;
                    queue.Enqueue(t);
                }
            }
            return seen;
        }

        /// <summary>
        /// Shortest horizontal walking distance from <paramref name="source"/> to every surface (8-neighbour Dijkstra):
        /// an orthogonal step costs <c>cellSize</c>, a diagonal one <c>√2·cellSize</c>. Walk, jump and drop edges all
        /// count, so distances are directed. Unreachable surfaces get +infinity. When <paramref name="parent"/> is
        /// given it receives each surface's predecessor on a shortest path (-1 for the source and unreached ones).
        /// </summary>
        public void Distances(int source, float[] distance, int[] parent = null)
        {
            EnsureDiagonals();
            float orthogonal = _surfaces.Grid.CellSize;
            float diagonal = orthogonal * 1.41421356f;
            for (int i = 0; i < distance.Length; i++) distance[i] = float.PositiveInfinity;
            if (parent != null)
                for (int i = 0; i < parent.Length; i++) parent[i] = -1;

            var heap = new MinHeap(256);
            distance[source] = 0f;
            heap.Push(0f, source);
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int s);
                if (d > distance[s]) continue;
                Relax(_out[s], orthogonal);
                Relax(_diagonal[s], diagonal);

                void Relax(List<int> successors, float step)
                {
                    float next = d + step;
                    foreach (int t in successors)
                    {
                        if (next >= distance[t]) continue;
                        distance[t] = next;
                        if (parent != null) parent[t] = s;
                        heap.Push(next, t);
                    }
                }
            }
        }

        /// <summary>Diagonal successors of a surface, as <see cref="Distances"/> uses them.</summary>
        public IReadOnlyList<int> DiagonalSuccessors(int surface)
        {
            EnsureDiagonals();
            return _diagonal[surface];
        }

        private void EnsureDiagonals()
        {
            if (_diagonal != null) return;
            var surfaces = _surfaces;
            _diagonal = new List<int>[_out.Length];
            for (int a = 0; a < _out.Length; a++)
            {
                var list = _diagonal[a] = new List<int>(0);
                int ax = surfaces[a].X, az = surfaces[a].Z;
                // Through the x-side neighbour c1 to b, then confirm b is also reached through the z-side one.
                foreach (int c1 in _out[a])
                {
                    if (surfaces[c1].Z != az) continue;
                    foreach (int b in _out[c1])
                    {
                        if (surfaces[b].Z == az || surfaces[b].X == ax || list.Contains(b)) continue;
                        if (ThroughZSide(a, b, surfaces[b].Z)) list.Add(b);
                    }
                }
            }

            bool ThroughZSide(int a, int b, int z)
            {
                foreach (int c2 in _out[a])
                    if (surfaces[c2].Z == z && surfaces[c2].X == surfaces[a].X && _out[c2].Contains(b)) return true;
                return false;
            }
        }

        /// <summary>A binary min-heap of (key, value) for Dijkstra; stale entries are skipped by the caller.</summary>
        private sealed class MinHeap
        {
            private float[] _keys;
            private int[] _values;
            public int Count;

            public MinHeap(int capacity)
            {
                _keys = new float[capacity];
                _values = new int[capacity];
            }

            public void Push(float key, int value)
            {
                if (Count == _keys.Length)
                {
                    System.Array.Resize(ref _keys, Count * 2);
                    System.Array.Resize(ref _values, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (_keys[up] <= key) break;
                    _keys[i] = _keys[up];
                    _values[i] = _values[up];
                    i = up;
                }
                _keys[i] = key;
                _values[i] = value;
            }

            public void Pop(out float key, out int value)
            {
                key = _keys[0];
                value = _values[0];
                float lastKey = _keys[--Count];
                int lastValue = _values[Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && _keys[child + 1] < _keys[child]) child++;
                    if (_keys[child] >= lastKey) break;
                    _keys[i] = _keys[child];
                    _values[i] = _values[child];
                    i = child;
                }
                _keys[i] = lastKey;
                _values[i] = lastValue;
            }
        }

        private void Add(int from, int to, MoveKind kind)
        {
            Edges.Add(new MoveEdge { From = from, To = to, Kind = kind });
            _out[from].Add(to);
            _in[to].Add(from);
        }
    }
}
