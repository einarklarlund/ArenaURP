using System;
using UnityEngine;

namespace Arena.LevelGen
{
    public enum PropKind : byte
    {
        Fence = 0,
        Car = 1,
        Tree = 2,
        Table = 3,
        Shelf = 4,
        Crate = 5,
        /// <summary>A light fixture under a room's ceiling. Fixtures have no collider (see <see cref="PropKinds"/>).</summary>
        CeilingLight = 6,
        /// <summary>A lamp on an outer wall: beside an entrance, or over a porch.</summary>
        WallLight = 7,
    }

    public static class PropKinds
    {
        /// <summary>Every kind, in catalogue order.</summary>
        public static readonly PropKind[] All =
        {
            PropKind.Fence, PropKind.Car, PropKind.Tree, PropKind.Table, PropKind.Shelf, PropKind.Crate,
            PropKind.CeilingLight, PropKind.WallLight,
        };

        /// <summary>Furniture standing on a room's slab.</summary>
        public static bool IsInterior(PropKind kind) => kind is PropKind.Table or PropKind.Shelf or PropKind.Crate;

        /// <summary>A light fixture: no collider and no primitives, so the span grid and the validator never see it. It
        /// is placed against a solid's face (stop short) or under a slab (stack), so it still overlaps nothing.</summary>
        public static bool IsFixture(PropKind kind) => kind is PropKind.CeilingLight or PropKind.WallLight;
    }

    /// <summary>
    /// What the generator places and validates a prop variant by: its footprint, in the prop's own axes (pivot at the
    /// bottom centre, length along +Z, width along X). It is geometry, so it is part of the config and its hash; the
    /// prefab that draws it is not.
    /// <list type="bullet">
    /// <item>A car: one box, <see cref="Length"/> × <see cref="Width"/> × <see cref="Height"/>.</item>
    /// <item>A fence: one piece, <see cref="Length"/> long, <see cref="Height"/> tall, <see cref="Width"/> thick.</item>
    /// <item>A tree: a trunk box (<see cref="Length"/> × <see cref="Width"/> × <see cref="Height"/>) and a canopy box
    /// from <see cref="CanopyBottom"/> up <see cref="CanopyHeight"/>.</item>
    /// <item>A table, shelf or crate: one box, its length along the wall it stands against, its front (+X) facing into
    /// the room.</item>
    /// <item>A light fixture: one box. A wall lamp's length runs along its wall and its width out from it (+X).</item>
    /// </list>
    /// </summary>
    [Serializable]
    public struct PropShape
    {
        [Tooltip("Relative odds of this variant among its kind's.")]
        public float Weight;
        [Tooltip("Along +Z: a car's length, a fence piece's length, a trunk's depth.")]
        public float Length;
        [Tooltip("Along X: a car's width, a fence's thickness, a trunk's width.")]
        public float Width;
        [Tooltip("A car's or fence's height, a trunk's height.")]
        public float Height;
        [Tooltip("Trees: the canopy box's length (Z) and width (X).")]
        public float CanopyLength;
        public float CanopyWidth;
        [Tooltip("Trees: height of the canopy's underside above the trunk's foot.")]
        public float CanopyBottom;
        public float CanopyHeight;

        public static PropShape DefaultCar => new() { Weight = 1f, Length = 4.5f, Width = 1.9f, Height = 1.5f };
        public static PropShape DefaultFence => new() { Weight = 1f, Length = 2f, Width = 0.1f, Height = 1.2f };

        public static PropShape DefaultTable => new() { Weight = 1f, Length = 1.6f, Width = 0.8f, Height = 0.75f };
        public static PropShape DefaultShelf => new() { Weight = 1f, Length = 2f, Width = 0.5f, Height = 2f };
        public static PropShape DefaultCrate => new() { Weight = 1f, Length = 1f, Width = 1f, Height = 1f };
        public static PropShape DefaultCeilingLight => new() { Weight = 1f, Length = 0.6f, Width = 0.6f, Height = 0.15f };
        public static PropShape DefaultWallLight => new() { Weight = 1f, Length = 0.3f, Width = 0.2f, Height = 0.3f };

        public static PropShape DefaultTree => new()
        {
            Weight = 1f, Length = 0.6f, Width = 0.6f, Height = 3f,
            CanopyLength = 3f, CanopyWidth = 3f, CanopyBottom = 3f, CanopyHeight = 2f,
        };

        public static PropShape Default(PropKind kind) => kind switch
        {
            PropKind.Car => DefaultCar,
            PropKind.Tree => DefaultTree,
            PropKind.Table => DefaultTable,
            PropKind.Shelf => DefaultShelf,
            PropKind.Crate => DefaultCrate,
            PropKind.CeilingLight => DefaultCeilingLight,
            PropKind.WallLight => DefaultWallLight,
            _ => DefaultFence,
        };

        /// <summary>The ground area a prop of this shape takes: the larger of trunk and canopy for a tree.</summary>
        public Vector2 Extent(PropKind kind) => kind == PropKind.Tree
            ? new Vector2(Mathf.Max(Length, CanopyLength), Mathf.Max(Width, CanopyWidth))
            : new Vector2(Length, Width);

        /// <summary>The top of the prop above its foot.</summary>
        public float Top(PropKind kind) => kind == PropKind.Tree ? Mathf.Max(Height, CanopyBottom + CanopyHeight) : Height;

        public PropShape Sanitized(PropKind kind)
        {
            var s = this;
            s.Weight = Mathf.Max(0f, s.Weight);
            s.Length = Mathf.Max(0.05f, s.Length);
            s.Width = Mathf.Max(0.05f, s.Width);
            s.Height = Mathf.Max(0.05f, s.Height);
            if (kind == PropKind.Fence) s.Length = Mathf.Max(0.5f, s.Length);
            if (kind == PropKind.Tree)
            {
                s.CanopyLength = Mathf.Max(0.05f, s.CanopyLength);
                s.CanopyWidth = Mathf.Max(0.05f, s.CanopyWidth);
                s.CanopyHeight = Mathf.Max(0.05f, s.CanopyHeight);
                s.CanopyBottom = Mathf.Max(0f, s.CanopyBottom);
            }
            else s.CanopyLength = s.CanopyWidth = s.CanopyBottom = s.CanopyHeight = 0f;
            return s;
        }

        /// <summary>Picks a variant by weight from <paramref name="pick"/> in [0, 1). Uniform when every weight is 0.</summary>
        public static int Pick(PropShape[] variants, float pick)
        {
            float total = 0f;
            foreach (var v in variants) total += v.Weight;
            if (total <= 0f) return Mathf.Min(variants.Length - 1, (int)(pick * variants.Length));
            float at = pick * total;
            for (int i = 0; i < variants.Length; i++)
                if ((at -= variants[i].Weight) < 0f) return i;
            return variants.Length - 1;
        }
    }
}
