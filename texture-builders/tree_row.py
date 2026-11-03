"""Builds a horizontally tiling tree-row texture and its normal map from rubberduck's CC0 pine sheet.

Source: https://opengameart.org/content/high-res-tree-textures (trees_2K.zip: tree_collection_2K.png and
tree_collection_2K_n.png). The sheet is cut into single trees by its transparent columns, then three rows of
them are layered over a dark backing: a small, dark back row, a middle row and a full-size front row. The
lower part is opaque (the forest floor behind the trunks) and everything above the crowns is transparent.
Alpha is binary: a texel of a tree is fully opaque and anything else is fully clear. Trees that cross the right
edge wrap round to the left, so the result tiles along U.

Usage: python tree_row.py <sheet.png> <sheet_n.png> <out.png> <out_normal.png> [--seed N]
"""
import argparse

import numpy as np
from PIL import Image

WIDTH, HEIGHT = 4096, 2048
GROUND = 0.04          # Tree bases sit this far up from the bottom (fraction of height).
BACKING_TOP = 0.30     # The opaque backing reaches about this high, with a ragged top.
CLEAR_TOP = 128        # Rows at the top that are always transparent, enough for the smaller mip levels too.
SKIP = {2}             # Sheet trees left out: the snowy one.
SNOW = 0.45            # A texel brighter than this in every channel is snow.
GRADE = np.array([0.95, 1.08, 0.90], np.float32)
COVERAGE = 0.5         # A sheet texel at least this opaque is part of a tree; anything less is left out.

# name, count, height range (fraction of the texture's height), brightness
ROWS = [
    ("back", 15, (0.50, 0.62), 0.45),
    ("middle", 11, (0.64, 0.78), 0.70),
    ("front", 8, (0.76, 0.86), 1.00),
]


def cut_trees(colour, normal):
    """The sheet's trees, each cropped to its own box, as (rgba, normal) float arrays."""
    alpha = colour[..., 3]
    filled = (alpha > 0.02).sum(axis=0) > 4
    trees, start = [], None
    for x, on in enumerate(list(filled) + [False]):
        if on and start is None:
            start = x
        elif not on and start is not None:
            if x - start > 40:
                rows = np.where((alpha[:, start:x] > 0.02).any(axis=1))[0]
                box = (slice(rows[0], rows[-1] + 1), slice(start, x))
                trees.append((colour[box], normal[box]))
            start = None
    return trees


def resize(array, height):
    width = max(1, round(array.shape[1] * height / array.shape[0]))
    channels = [Image.fromarray(array[..., c].astype(np.float32)).resize((width, height), Image.LANCZOS)
                for c in range(array.shape[2])]
    return np.clip(np.stack([np.asarray(c) for c in channels], axis=-1), 0.0, 1.0)


def paste(colour, normal, tree, tree_normal, left, base):
    """Pastes one tree with its base at row `base`, wrapping round the U edge. Every texel the tree covers is
    replaced outright, so nothing behind shows through it."""
    h, w = tree.shape[:2]
    top = base - h
    rows = slice(max(top, 0), base)
    tree, tree_normal = tree[rows.start - top:], tree_normal[rows.start - top:]
    columns = (np.arange(w) + left) % WIDTH
    covered = tree[..., 3] >= COVERAGE
    # The cut-out's soft edge texels carry the photo's pale background; scaling by coverage darkens that halo.
    shaded = tree.copy()
    shaded[..., :3] *= tree[..., 3:4]
    for target, source in ((colour, shaded), (normal, tree_normal)):
        region = target[rows, columns]
        region[covered, :3] = source[covered, :3]
        if target is colour:
            region[covered, 3] = 1
        target[rows, columns] = region


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("sheet")
    parser.add_argument("sheet_normal")
    parser.add_argument("out")
    parser.add_argument("out_normal")
    parser.add_argument("--seed", type=int, default=7)
    args = parser.parse_args()
    rng = np.random.default_rng(args.seed)

    sheet = np.asarray(Image.open(args.sheet).convert("RGBA"), dtype=np.float32) / 255
    sheet_normal = np.asarray(Image.open(args.sheet_normal).convert("RGB"), dtype=np.float32) / 255
    trees = [t for i, t in enumerate(cut_trees(sheet, sheet_normal)) if i not in SKIP]
    print(f"{len(trees)} trees used")

    opaque = np.concatenate([t[0][t[0][..., 3] > 0.9][:, :3] for t in trees])
    foliage = opaque.mean(axis=0)
    for tree, _ in trees:
        # Some trees have a dusting of snow: paint it over with dark needles, then grade towards green.
        rgb = tree[..., :3]
        snow = rgb.min(axis=-1) > SNOW
        rgb[snow] = foliage * rng.uniform(0.6, 1.0, (snow.sum(), 1))
        rgb *= GRADE
    colour = np.zeros((HEIGHT, WIDTH, 4), np.float32)
    normal = np.zeros((HEIGHT, WIDTH, 3), np.float32)
    normal[:] = (0.5, 0.5, 1.0)

    # Backing: the dark forest behind the trunks, with a ragged top that tiles.
    phase = rng.uniform(0, 2 * np.pi, 3)
    x = np.arange(WIDTH) / WIDTH * 2 * np.pi
    ragged = (np.sin(x * 7 + phase[0]) * 0.03 + np.sin(x * 19 + phase[1]) * 0.015 + np.sin(x * 43 + phase[2]) * 0.01)
    edge = HEIGHT - (BACKING_TOP + ragged) * HEIGHT
    rows = np.arange(HEIGHT)[:, None]
    backing = (rows >= edge[None, :]).astype(np.float32)
    depth = np.clip((rows - edge[None, :]) / (HEIGHT * BACKING_TOP), 0, 1)
    shade = foliage * 0.35 * (1 - depth[..., None] * 0.5)
    colour[..., :3] = shade
    colour[..., 3] = backing

    base = HEIGHT - int(GROUND * HEIGHT)
    for name, count, (low, high), brightness in ROWS:
        slots = (np.arange(count) + rng.uniform(-0.3, 0.3, count)) / count * WIDTH
        for left in slots:
            index = rng.integers(len(trees))
            tree, tree_normal = trees[index]
            height = int(rng.uniform(low, high) * HEIGHT)
            scaled = resize(tree, height)
            scaled_normal = resize(tree_normal, height)
            if rng.random() < 0.5:
                scaled, scaled_normal = scaled[:, ::-1].copy(), scaled_normal[:, ::-1].copy()
                scaled_normal[..., 0] = 1 - scaled_normal[..., 0]
            scaled[..., :3] *= brightness
            row_base = base - int(rng.uniform(0, 0.02) * HEIGHT)
            paste(colour, normal, scaled, scaled_normal, int(left - scaled.shape[1] / 2), row_base)
        print(f"{name} row: {count} trees")

    # Under the ground line everything is opaque, so the clamped bottom edge stays solid.
    colour[base:, 3] = 1
    # The top rows stay clear: V is clamped, so whatever is there is stretched up the rest of the wall.
    colour[:CLEAR_TOP, 3] = 0
    # Fill transparent texels with the foliage colour, so mip-mapping doesn't pull in black fringes.
    colour[colour[..., 3] == 0, :3] = foliage * 0.4

    n = normal * 2 - 1
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-6
    normal = (n + 1) / 2

    Image.fromarray((np.clip(colour, 0, 1) * 255).astype(np.uint8)).save(args.out)
    Image.fromarray((np.clip(normal, 0, 1) * 255).astype(np.uint8)).save(args.out_normal)
    print(f"wrote {args.out} and {args.out_normal}")


if __name__ == "__main__":
    main()
