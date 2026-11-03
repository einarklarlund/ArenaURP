using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Reads a prop prefab's colliders into its footprint, and checks a footprint against them. Everything is in the
    /// prefab root's space: pivot at the bottom centre, +Z forward.
    /// </summary>
    public static class PropFitter
    {
        /// <summary>How far a collider may reach past the declared boxes before the check warns: past that, the
        /// validator would not see it.</summary>
        public const float Tolerance = 0.1f;

        /// <summary>
        /// The smallest footprint centred on the pivot that holds every collider: one box for a car or fence; for a tree,
        /// a trunk box round the colliders that reach the ground and a canopy box round the rest. A light fixture has no
        /// colliders (the builder takes them off), so its box is fitted round its meshes. Keeps the weight.
        /// </summary>
        public static bool TryFit(PropKind kind, GameObject prefab, PropShape current, out PropShape shape, out string error)
        {
            shape = current;
            error = null;
            bool fixture = PropKinds.IsFixture(kind);
            var boxes = fixture ? MeshBounds(prefab) : ColliderBounds(prefab);
            if (boxes.Count == 0)
            {
                error = fixture ? $"{prefab.name} has no meshes." : $"{prefab.name} has no colliders.";
                return false;
            }
            if (kind != PropKind.Tree)
            {
                var all = Union(boxes);
                shape.Length = 2f * Mathf.Max(-all.min.z, all.max.z);
                shape.Width = 2f * Mathf.Max(-all.min.x, all.max.x);
                shape.Height = all.max.y;
                return true;
            }

            var trunk = boxes.FindAll(b => b.bounds.min.y < Tolerance);
            var canopy = boxes.FindAll(b => b.bounds.min.y >= Tolerance);
            if (trunk.Count == 0 || canopy.Count == 0)
            {
                error = $"{prefab.name} needs a trunk collider reaching the ground and a canopy collider above it.";
                return false;
            }
            var t = Union(trunk);
            var c = Union(canopy);
            shape.Length = 2f * Mathf.Max(-t.min.z, t.max.z);
            shape.Width = 2f * Mathf.Max(-t.min.x, t.max.x);
            shape.Height = t.max.y;
            shape.CanopyLength = 2f * Mathf.Max(-c.min.z, c.max.z);
            shape.CanopyWidth = 2f * Mathf.Max(-c.min.x, c.max.x);
            shape.CanopyBottom = c.min.y;
            shape.CanopyHeight = c.max.y - c.min.y;
            return true;
        }

        /// <summary>A line per collider that reaches more than <see cref="Tolerance"/> past the footprint's boxes.</summary>
        public static List<string> Check(PropKind kind, GameObject prefab, PropShape shape)
        {
            var problems = new List<string>();
            // A fixture's colliders are taken off when it is built.
            if (PropKinds.IsFixture(kind)) return problems;
            var declared = new List<Bounds> { BoxOf(shape.Length, shape.Width, 0f, shape.Height) };
            if (kind == PropKind.Tree)
                declared.Add(BoxOf(shape.CanopyLength, shape.CanopyWidth, shape.CanopyBottom, shape.CanopyHeight));
            foreach (var (name, bounds) in ColliderBounds(prefab))
            {
                float best = float.PositiveInfinity;
                foreach (var box in declared) best = Mathf.Min(best, Overshoot(bounds, box));
                if (best > Tolerance) problems.Add($"Collider '{name}' reaches {best:0.##} past the footprint.");
            }
            return problems;
        }

        private static Bounds BoxOf(float length, float width, float bottom, float height) =>
            new(new Vector3(0f, bottom + height * 0.5f, 0f), new Vector3(width, height, length));

        private static float Overshoot(Bounds inner, Bounds outer)
        {
            var below = outer.min - inner.min;
            var above = inner.max - outer.max;
            return Mathf.Max(0f, Mathf.Max(Mathf.Max(below.x, below.y), Mathf.Max(below.z, Mathf.Max(above.x,
                Mathf.Max(above.y, above.z)))));
        }

        private static Bounds Union(List<(string name, Bounds bounds)> boxes)
        {
            var all = boxes[0].bounds;
            foreach (var (_, b) in boxes) all.Encapsulate(b);
            return all;
        }

        /// <summary>Each mesh's axis-aligned bounds in the prefab root's space.</summary>
        private static List<(string name, Bounds bounds)> MeshBounds(GameObject prefab)
        {
            var result = new List<(string, Bounds)>();
            var toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    result.Add((filter.name, Transformed(toRoot * filter.transform.localToWorldMatrix, filter.sharedMesh.bounds)));
            return result;
        }

        private static Bounds Transformed(Matrix4x4 m, Bounds local)
        {
            var bounds = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                bounds.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return bounds;
        }

        /// <summary>Each collider's axis-aligned bounds in the prefab root's space.</summary>
        private static List<(string name, Bounds bounds)> ColliderBounds(GameObject prefab)
        {
            var result = new List<(string, Bounds)>();
            var toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                Bounds local;
                switch (collider)
                {
                    case BoxCollider box:
                        local = new Bounds(box.center, box.size);
                        break;
                    case SphereCollider sphere:
                        local = new Bounds(sphere.center, Vector3.one * (2f * sphere.radius));
                        break;
                    case CapsuleCollider capsule:
                        var size = Vector3.one * (2f * capsule.radius);
                        size[capsule.direction] = Mathf.Max(capsule.height, 2f * capsule.radius);
                        local = new Bounds(capsule.center, size);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        local = mesh.sharedMesh.bounds;
                        break;
                    default:
                        continue;
                }
                var m = toRoot * collider.transform.localToWorldMatrix;
                var bounds = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    bounds.Encapsulate(m.MultiplyPoint3x4(corner));
                }
                result.Add((collider.name, bounds));
            }
            return result;
        }
    }
}
