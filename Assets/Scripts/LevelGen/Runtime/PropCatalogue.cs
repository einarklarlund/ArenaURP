using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// One look a prop kind can take: a prefab and the footprint the generator places and validates it by. The prefab's
    /// pivot is its bottom centre and it faces +Z. With no prefab, the builder draws the footprint as a placeholder.
    /// </summary>
    [Serializable]
    public class PropVariant
    {
        public GameObject prefab;
        public PropShape shape = PropShape.DefaultCar;
    }

    /// <summary>
    /// The prefab variants per prop kind. Their shapes are geometry: they go into the settings, and so into the config
    /// hash and the share code. The prefabs are only looked up by the builder.
    /// </summary>
    [Serializable]
    public class PropCatalogue
    {
        public List<PropVariant> fences = new();
        public List<PropVariant> cars = new();
        public List<PropVariant> trees = new();
        public List<PropVariant> tables = new();
        public List<PropVariant> shelves = new();
        public List<PropVariant> crates = new();
        [Tooltip("Under each room's ceiling. Fixtures are built without colliders, even from a prefab that has some.")]
        public List<PropVariant> ceilingLights = new();
        [Tooltip("Beside entrances and over porches, on the outer wall.")]
        public List<PropVariant> wallLights = new();

        public List<PropVariant> Variants(PropKind kind) => kind switch
        {
            PropKind.Car => cars,
            PropKind.Tree => trees,
            PropKind.Table => tables,
            PropKind.Shelf => shelves,
            PropKind.Crate => crates,
            PropKind.CeilingLight => ceilingLights,
            PropKind.WallLight => wallLights,
            _ => fences,
        };

        /// <summary>The prefab for a prop, or null for a placeholder.</summary>
        public GameObject PrefabFor(PropKind kind, int variant)
        {
            var variants = Variants(kind);
            return variants != null && variant >= 0 && variant < variants.Count ? variants[variant].prefab : null;
        }

        /// <summary>The catalogue as a build plan reads it: every kind's shapes, and which variants have a prefab.</summary>
        public PropArt Art()
        {
            var art = new PropArt();
            foreach (var kind in PropKinds.All)
            {
                var variants = Variants(kind);
                if (variants == null) continue;
                var prefabs = new bool[variants.Count];
                for (int i = 0; i < prefabs.Length; i++) prefabs[i] = variants[i].prefab != null;
                art.Set(kind, Shapes(kind), prefabs);
            }
            return art;
        }

        public PropShape[] Shapes(PropKind kind)
        {
            var variants = Variants(kind);
            if (variants == null) return null;
            var shapes = new PropShape[variants.Count];
            for (int i = 0; i < shapes.Length; i++) shapes[i] = variants[i].shape;
            return shapes;
        }
    }
}
