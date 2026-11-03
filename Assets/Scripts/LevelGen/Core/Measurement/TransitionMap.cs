using System.Collections.Generic;

namespace Arena.LevelGen
{
    /// <summary>
    /// The surfaces each transition's walkable primitives form: a bank's slope, a ramp's run. Flat, ledge and cliff
    /// transitions have no geometry of their own, so their lists are empty.
    /// </summary>
    public sealed class TransitionMap
    {
        /// <summary>Per transition (by position in <see cref="LevelDescription.Transitions"/>).</summary>
        public readonly List<int>[] FloorSurfaces;

        public TransitionMap(LevelDescription description, SurfaceSet surfaces)
        {
            int count = description.Transitions.Count;
            FloorSurfaces = new List<int>[count];
            var indexById = new Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                FloorSurfaces[i] = new List<int>();
                indexById[description.Transitions[i].Id] = i;
            }

            var cells = new List<CoveredCell>();
            foreach (var p in description.Primitives)
            {
                if (p.OwnerKind != OwnerKind.Transition || !PrimitiveRoles.IsWalkable(p.Role)) continue;
                if (!indexById.TryGetValue(p.OwnerId, out int transition)) continue;
                Rasterizer.CoveredCells(p, description, surfaces.Grid, cells);
                foreach (var c in cells)
                {
                    int surface = surfaces.FindAt(c.X, c.Z, c.Top);
                    if (surface >= 0 && !FloorSurfaces[transition].Contains(surface)) FloorSurfaces[transition].Add(surface);
                }
            }
        }
    }
}
