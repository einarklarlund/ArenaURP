# Level generation design

## Intent

Build a procedural level generator for the first-person free-for-all arena. A level is made of
**rooms** joined by **connectors**, and its feel is steered by measurable **metrics** for openness,
symmetry and elevation. Each metric has a level-wide target and a spread across rooms.

Requirements:

- **Shareable:** a level is fully determined by `(generatorVersion, seed, config)`, encoded as one share code.
- **Configurable size:** the level is W × H, with each side between 50 and 100 units (1 unit = 1 world unit).
- **Rooms:** 4–8 mostly self-contained sections. Rooms can be rectangles, L-shapes or irregular outlines.
- **Varied connectors:** doorways, open merges, straight halls, winding halls, stairs, ramps and drops.
- **Metrics:** openness, symmetry and elevation are measured on the generated output, at both room
  and level scale, with per-room values spread around a level target.
- **True 3D:** floors can stack, as with bridges, balconies and rooms above rooms.
- **No one gets stuck:** this is a hard rule (see [Hard constraints](#hard-constraints)).
- **Generate in the editor and during gameplay**, with tools that show how well the algorithm hits
  its targets (see [Tooling](#tooling)).

Out of scope for now: spawn point placement (later; see [Open items](#open-items-and-assumptions)),
pickups and chests, art and theming, AI navigation, and generation speed. Generation runs live at
match start, but nothing is optimized yet.

## Ground rules for the implementing session

- **Build the metrics before the generators.** Every generator stage is judged by the metric
  functions, so those come first and get tested on hand-built fixtures.
- **Keep the core free of Unity scene code.** Generation and measurement live in a plain C#
  assembly, and the scene-side code only turns the output into GameObjects (see
  [Code layout](#code-layout)).
- **Make every milestone visible in the editor window** (see [Milestones](#milestones)).
- **Stop after each milestone** for review. Don't commit unless asked.

## Player constants

These drive both measurement and the no-stuck rule. Keep them in config so they can follow movement
tuning.

| Constant | Value | Used for |
|---|---|---|
| `stepHeight` | 0.3 | Walking over a height change at or below this needs no jump. Geometry above it counts as an obstruction. |
| `jumpHeight` | 1.0 | Height changes above `stepHeight` and up to this are two-way: jump up, drop down. |
| `eyeHeight` | 1.877 | The height sightlines are cast at. |
| `playerHeight` | 2.0 *(assumed)* | The headroom a surface needs above it to count as walkable. |
| `maxSafeDrop` | 4.0 *(assumed)* | Drops higher than this are not allowed. |

## Terminology

- **Primitive:** one piece of geometry the generator places, such as a box, ramp, stair run or slab.
  Each one carries a **role** tag:
  - `Floor`, `Platform`, `Ramp`, `Stairs` are walkable.
  - `Wall`, `Cover`, `Pillar` are blockers.
  - `Ceiling` covers an area from above.
- **Span grid:** the measurement representation. It's a 2D grid of columns, and each column holds a
  sorted list of solid spans `(bottom, top, role)`. The core rasterizes primitives into it. This is
  the same idea as Recast's heightfield.
- **Walkable surface:** the top of a span that has at least `playerHeight` of clearance above it.
  It's identified by `(cell, surfaceIndex)`.
- **Walk region:** a maximal set of walkable surfaces connected through 4-neighbour steps of at most
  `stepHeight`. Ramps and stairs are built with risers of at most `stepHeight`, so they join walk
  regions without any special handling.
- **Orbit:** a set of rooms that map onto each other under the level's mirror axes. An orbit holds
  1 room (the centre), 2 rooms (a mirrored pair) or 4 rooms (one per quadrant).

## Configuration

`LevelGenConfig`, a ScriptableObject. Presets are assets.

```
generatorVersion        int     (bump whenever output for the same seed changes)
seed                    uint64
width, height           50..100 units
roomCount               4..8
maxHeight               vertical extent of the level (default 12)
cellSize                grid resolution in units (default 0.5)

per-room metrics, each MetricTarget { target 0..1, spread 0..1, skew -1..1, tolerance, weight }:
    openness
    symmetryX, symmetryY
    elevationRange, elevationFrequency, elevationOverlap

level-only metrics, each { target, tolerance, weight }:
    levelSymmetryX, levelSymmetryY
    levelElevationRange      how far apart room base heights are
    levelStacking            how much room footprints overlap vertically
    loops                    int, independent cycles in the room graph

connector weights          relative odds for door / open / straightHall / windingHall / stairs / ramp / drop
connector sizes            door width, hall width, ramp max slope, stair riser height (≤ stepHeight)
search budget              improve iterations per room, retries per room, retries per level
player constants           (table above)
```

The level-scale elevation metrics (`levelElevationRange`, `levelStacking`) separate the room-to-room
layout from room interiors. This follows the rule already set for symmetry: level metrics are
measured on the macro layout, and room metrics on interiors.

## Metrics

All metrics are pure functions of the span grid, the room assignment of each cell and the room
graph. Every value is in 0..1 except `loops`.

### Openness (room)

Openness is how much of a room's floor you can see from where you stand.

1. Sample points are every k-th walkable surface in the room, on a deterministic lattice.
2. From each sample point A, cast sightlines to every walkable surface B in the **same walk region**.
   - Use 2D grid traversal (supercover or DDA) between the two cells.
   - Along the line, `lineFloor` is the floor height linearly interpolated between `floor(A)` and
     `floor(B)`.
   - A cell **blocks** the line if any solid span in it overlaps the interval
     `(lineFloor + stepHeight, lineFloor + eyeHeight]`.
   - This makes waist-high cover, walls and raised ledges block sight, along with overhangs low
     enough to hit eye level. Ramps along the line, and pits below it, don't block.
3. The point's openness is visible surfaces ÷ surfaces in its walk region.
4. Average the points in each walk region, then take the area-weighted mean over the room's walk regions.

Level openness is the mean of the room values, weighted by room count. Connectors are excluded.

### Symmetry X / Y (room)

Symmetry looks only at the outline, so heights are ignored.

1. Take the room's bounding box on the grid.
2. A cell is a **feature** if it is outside the room's footprint or its column contains any blocker
   (`Wall`, `Cover`, `Pillar`) at any height. Platforms, ramps and stairs are not features.
3. Mirror the feature set F across the box's vertical centre line (for X) or its horizontal centre
   line (for Y).
4. The score is the Jaccard overlap: `|F ∩ mirror(F)| / |F ∪ mirror(F)|`. An empty F scores 1.

Counting outside-footprint cells as features means an L-shaped room scores as asymmetric. Doorways
don't have to be symmetric and are ignored.

### Level symmetry X / Y (level)

This is the same Jaccard score, computed over the whole level's macro mask with axes through the
level's centre. A cell is set if, seen from above, it lies under any room footprint or connector
footprint. Interiors are not part of it, which keeps level symmetry independent of room symmetry.

### Elevation range (room)

`(highest walkable surface − lowest walkable surface) / maxHeight` within the room.

### Elevation frequency (room)

1. Count the edges between 4-neighbouring walkable surfaces whose heights differ by more than
   `stepHeight`. These are ledges, stair tops and drop edges.
2. Multiply by `cellSize` to get a length, then divide by the walkable area.
3. Normalize with a calibration constant so that 1.0 means "very choppy", then clamp to 0..1.

Set the constant with the batch runner's calibration sweep (see [Tooling](#tooling)).

### Elevation overlap (room)

The fraction of the room's walkable columns that hold two or more walkable surfaces: under and on
bridges, balconies and mezzanines.

### Level elevation range (level)

`(max room base height − min room base height) / maxHeight`.

### Level stacking (level)

The fraction of total room footprint area that lies directly over or under another room's footprint.

### Loops (level)

`E − V + 1` on the room graph, where V is rooms and E is connectors. Parallel connectors count, and
drops count. The no-stuck rule guarantees a way back from any drop. Dead ends are allowed and are not
tracked.

### Error

The total error is the sum over metrics of `weight × |measured − target|`, where a measurement within
`tolerance` counts as 0. Loops error is `|measured − target|` scaled by its weight. This is the
single number that improvement and candidate selection minimize.

## Hard constraints

A level that breaks any of these is repaired or regenerated. It is never shipped.

1. **No one gets stuck.** Build the movement graph on walkable surfaces, including connectors. For
   each 4-neighbour pair with height difference Δ:
   - `|Δ| ≤ stepHeight`: walk, two-way.
   - `stepHeight < |Δ| ≤ jumpHeight`, with headroom for the jump: two-way.
   - `jumpHeight < Δ ≤ maxSafeDrop`: one-way, downward.
   - Larger: no edge.

   Let R be the set of surfaces reachable from a reference surface, the largest walk region of the
   first room. **R must be one strongly connected component**: from anywhere you can reach, you can
   reach everywhere else you can reach. Surfaces outside R, such as the top of a 1.5-unit pillar, are
   allowed because nobody can get there. R must also contain every room's largest walk region and
   every connector's floor.
2. **Every room has at least one connector that isn't a drop.**
3. Everything lies inside `width × height × maxHeight`.
4. Stacked floors keep `playerHeight` of headroom, and connectors meet their size limits.
5. The room count matches `roomCount`.

## Pipeline

```
Config + seed
  → 0 seed tree
  → 1 allocate per-room metric targets
  → 2 symmetric macro skeleton
  → 3 room footprints, base heights, room graph
  → 4 connectors
  → 5 room interiors (generate → measure → improve → retry)
  → 6 validate + repair
  → LevelDescription (primitives) → builder
```

### 0. Seed tree

Use your own PRNG (PCG32 or xoshiro256), never `UnityEngine.Random` or `System.Random`. Derive each
sub-seed by hashing with SplitMix64: `subSeed = hash(seed, stageId, index)`. Use stable integer stage
ids, never `string.GetHashCode`. Each stage and each room draws only from its own stream, so retrying
room 3 doesn't change room 4.

Avoid other sources of randomness: don't iterate over a `Dictionary` or `HashSet` where the order
affects output, don't run generation in parallel, and don't use fast-math. The server generates the
level, but a shared seed has to reproduce the same level on any machine and in the editor.

### 1. Allocate per-room targets

For each per-room metric, with level target `t`, spread `s`, skew `k` and N rooms:

1. Take quantiles `u_i = (i + 0.5) / N`.
2. Warp them to `v_i = u_i ^ (2 ^ (2k))`. Positive k gives a few high outliers, negative k a few low ones.
   (The first draft had `2 ^ (-2k)`, which does the opposite of this sentence; milestone 3 follows the sentence.)
3. Centre and scale: `x_i = t + s × (v_i − mean(v))`, scaled so that s = 1 stretches across as much
   of 0..1 as `t` allows.
4. Clamp to 0..1. Shift the rest to restore `mean = t`, and repeat until stable.
5. Shuffle the assignment with the allocation stream.

For example, target 0.3 with a high spread and negative skew gives a couple of very closed rooms
among more open ones. This formula is a starting point: keep it behind one function so it can be
swapped.

### 2. Symmetric macro skeleton

1. Decide the mirroring from the level symmetry targets. Above a threshold such as 0.5, mirror on
   that axis. Mirroring on both axes uses the quadrant as the base region, one axis uses a half, and
   neither uses the whole level.
2. Split `roomCount` into orbits that fit the mirroring.
   - Both axes: `N = c + 2p + 4q`, where c ∈ {0, 1} is the centre room, p is pairs on an axis and q
     is quadruples.
   - One axis: `N = a + 2p`, where a is rooms sitting on the axis.
   - Choose among the valid splits with the skeleton stream.
3. Place the orbit representatives in the base region as rough rectangles, then mirror them.
4. **Break the symmetry** in proportion to `1 − target` on each axis: jitter one copy's position and
   size, and change its outline shape.
5. Measure level symmetry on the macro mask. Retry with the next sub-seed until it is within
   tolerance, or the budget runs out and the best result is kept.

### 3. Room footprints, base heights and room graph

- **Footprints:** each room becomes a rectangle, an L-shape, or an irregular outline. Irregular
  outlines are a union of 2–3 rectangles, or seeded cell growth inside the rough rectangle.
- **Size:** rooms fill the level to roughly 40–60% coverage. The rest goes to connectors and voids,
  and the ratio is a config knob.
- **Base heights:** spread rooms across `0..maxHeight` to hit `levelElevationRange`.
- **Stacking:** to hit `levelStacking`, let some room footprints overlap rooms below them, as long as
  the vertical gap is at least the lower room's height plus a slab. A stacked room's floor is the
  lower room's ceiling.
- **Graph:**
  1. Find candidate edges between rooms that are near each other (footprints touching or within hall
     range).
  2. Take a spanning tree, chosen at random with the graph stream.
  3. Add exactly `loops` more edges from the candidates.
  4. If the macro layout is mirrored, mirror edges with their orbits so the graph keeps the same symmetry.

### 4. Connectors

Pick each edge's type by its weight, filtered by what fits:

| Type | Fits when | Geometry |
|---|---|---|
| Door | Rooms share a wall and are at nearly the same height | An opening of door width in the shared wall |
| Open | Rooms share a wall and are at nearly the same height | The wall is removed for at least half the shared length |
| Straight hall | Rooms are aligned within hall range | A straight corridor with floor, walls and an optional ceiling |
| Winding hall | Rooms are within hall range | A corridor routed by a noisy, turn-biased path search on the grid |
| Stairs / Ramp | Rooms have a height difference | A hall or in-room run that climbs, with risers ≤ `stepHeight` or slope ≤ the max |
| Drop | The height difference is above `jumpHeight` and at most `maxSafeDrop` | An opening at a ledge, one-way down |

Then enforce hard constraint 2: any room whose only connectors are drops gets one of them turned into
stairs or a ramp. Connectors are fixed before interiors, so each room knows its doorway cells and the
floor height at each doorway.

### 5. Room interiors

Each room has its own targets (from step 1), footprint mask and doorway cells with fixed heights. It
generates in this order:

1. **Tiers (elevation):**
   - Place `Platform` primitives with freeform heights.
   - The number and size of edges follow `elevationFrequency`.
   - The height spread follows `elevationRange`.
   - Bridges and balconies, which are platforms with clearance underneath, follow `elevationOverlap`.
   - Every platform gets an access route: stairs, a ramp or a jumpable step. Elevation doesn't follow
     the room's symmetry, so this works over the whole footprint.
2. **Blockers (openness and symmetry):**
   - Work in the room's base region: its quadrant or half if the room's X or Y target is above the
     threshold, otherwise the whole room.
   - Place `Wall`, `Cover` and `Pillar` primitives until the estimated openness nears the target.
     Cover is 0.8–1.2 tall, and walls and pillars reach the ceiling.
   - Mirror the base region onto the rest of the room.
   - Then re-randomize a fraction of blockers on one side, sized to `1 − target` on each axis.
   - Keep a clear zone in front of each doorway.
3. **Measure, improve, retry:**
   - Rasterize, measure, and compute the room's error.
   - Run greedy local search for a fixed number of iterations: add, remove, move or resize one
     blocker or platform, and keep the change if the error drops. When the room is symmetric, apply
     each change to the whole mirror orbit so symmetry holds.
   - If the error is still above tolerance, regenerate from the room's next sub-seed.
   - Keep the best of all attempts.

### 6. Validate and repair

Run the [hard constraints](#hard-constraints) on the full span grid. Repairs, in order of preference:

- **Sink component** (you can enter but not leave): add stairs or a ramp out of it, or reduce the
  drop height into it to `jumpHeight` or less.
- **Room with only drops in:** turn one drop into stairs or a ramp.
- **Headroom or bounds violation:** shrink or move the primitive that causes it.

After each repair, measure again. If repair fails, regenerate the level from the next level-retry
sub-seed. Record every repair and retry in the generation report.

A level that breaks a hard constraint is never handed out. If every level retry fails, generation
reports the failure and its reason instead of a level. Settings that no level can satisfy are refused
before any attempt.

## Output: `LevelDescription`

`LevelGenerator.Generate` returns a `GenerationResult`: on success, the level; on failure, no level,
the reason, the hard constraints the closest attempt broke, and the attempt count. It also carries the
settings check's warnings.

`LevelDescription` is plain data and the only thing that leaves the core:

- the `generatorVersion`, seed and config hash
- rooms: id, footprint polygon, base height, and the targets they were assigned
- connectors: id, type, endpoints and path
- primitives: role, shape (box / ramp / stairs / slab), position, size, rotation, and the owning room or connector

**Builder (runtime assembly):**

- Turns primitives into meshes and colliders, combined per room. Placeholder materials are tinted by role.
- Server and clients build the same colliders from the same description, which client-side
  prediction needs.
- The core never touches GameObjects.

**Generation report (built next to the description, not sent over the network):**

- Per-room and level targets, measured values and errors
- Constraint results
- Repairs, retries and per-stage timing
- Optional debug layers: span grid, walk regions, movement graph, per-sample openness, symmetry
  mismatch masks

## Sharing and networking

- **Share code:** base32 of `(generatorVersion, seed, compact config)`. The config is about 30
  numbers, which pack into a few dozen bytes. When the code's `generatorVersion` doesn't match, the
  game warns that the level may differ.
- **At match start:** the server (or host) generates from `(seed, config)`, then sends the
  `LevelDescription` to clients. Clients don't regenerate; they only build. Use a FishNet broadcast
  or a target RPC on a level NetworkBehaviour.
  - Late joiners get the description when they join.
  - A few hundred primitives is roughly 10–20 KB. Check FishNet and the transport's reliable message
    size limits, and split it into chunks if needed.
- Players spawn only after their client reports that the build finished.

## Tooling

You need to see how the algorithm performs both in the editor and while playing.

### Editor: Level Generator window

- A `LevelGenConfig` slot (presets), a seed field with a randomize button, and a share code field
  that can be copied and pasted.
- **Generate:** builds the level into a temporary preview root in the open scene. It's never saved,
  and it's replaced on each run. When generation fails, the window shows the reason as an error and
  builds nothing.
- The `LevelGenConfig` inspector shows the settings check: errors for settings no level can satisfy,
  warnings for values the generator changes.
- **Step through:** runs the pipeline one stage at a time, showing each stage's output (skeleton,
  footprints, graph, connectors, interiors, repairs).
- **Metrics report:** a table with a row per room and one for the level, showing target, measured
  value and error for every metric, coloured by whether it's within tolerance. It also shows
  constraint pass or fail, retry and repair counts, and per-stage timings.
- **Scene-view overlays**, each with its own toggle:
  - room footprints by id, and connector paths coloured by type
  - walkable surfaces coloured by height
  - the movement graph, with walk, jump and drop edges in different colours, and constraint
    violations highlighted
  - an openness heatmap of each sample point's visible fraction
  - symmetry mismatch cells and mirror axes, for rooms and for the level
- **Batch runner:** generates N seeds for a config, with no scene build. It reports:
  - success rate and the share of levels where every metric is within tolerance
  - mean and worst error per metric, with histograms
  - retries and repairs per level, and timing
  - the worst seeds, each clickable to load into the window
  - CSV export
- **Calibration sweep:** steps one metric's target from 0 to 1 and plots the achieved values against
  the target. This shows what each dial can actually reach, and it's how the elevation frequency
  constant and the symmetry-breaking fractions get tuned.

### Gameplay

- The match start flow takes the seed and config from the lobby, or a random seed.
- A development HUD panel shows the seed, the share code and the level's metrics report. It's
  toggled with a key and only appears in development builds.
- Debug commands, server or host only, in development builds:
  - `levelgen.regen [seed]` tears down the level, generates a new one, sends it, and respawns players.
  - `levelgen.load <shareCode>` loads a level from a share code.
- Runtime overlays for a subset of the editor's: walk regions, movement graph and openness heatmap.
  These are drawn with a simple runtime line and mesh drawer, since Gizmos don't show in builds.
- A `LevelGenSandbox` scene for walking levels solo as host.

### Tests (EditMode)

- **Determinism:** the same seed and config must give a byte-identical `LevelDescription`. Hash it and
  compare across repeated runs.
- **Metric fixtures:**
  - An empty room scores openness 1.
  - A room split by a wall scores below 1, with the exact value known.
  - Waist-high cover reduces openness, and a pit doesn't.
  - A perfectly mirrored room scores 1 on symmetry.
  - An L-shaped empty room scores below 1 on symmetry.
  - A single flat floor scores 0 on elevation frequency.
  - A bridge produces elevation overlap.
- **Validator fixtures:**
  - A pit with only drops in fails, and passes after repair.
  - An unreachable pillar top is allowed.
  - A room with only drops in fails.
- **Allocation:** the mean of the allocated values equals the target, and every value is within 0..1.
- **Guarantee:** batches of seeds for the default settings and harder presets (stacking, a small level
  with wide doors and halls) all pass every hard constraint. Infeasible settings are refused with a
  reason, and a stacked room is reached by a flight from its host.

## Code layout

Follow the precedent of `Arena.Movement.Core`, a pure logic assembly.

```
Assets/Scripts/LevelGen/
  Core/      Arena.LevelGen.Core.asmdef
             config data, PRNG and seed tree, span grid, rasterizer, metrics, validator,
             generator stages, LevelDescription, GenerationReport, share code.
             No GameObjects or MonoBehaviours; Unity.Mathematics is fine.
  Runtime/   Arena.LevelGen.asmdef (references Core, FishNet)
             LevelGenConfig ScriptableObject, builder, network sync, runtime debug HUD and overlays
  Editor/    Arena.LevelGen.Editor.asmdef
             generator window, overlays, batch runner, calibration sweep
  Tests/     Arena.LevelGen.Tests.asmdef (EditMode)
```

`LevelGenConfig` (the ScriptableObject) converts to a plain config struct that the core consumes.

## Milestones

Each milestone ends with something visible in the editor window.

1. **Skeleton:**
   - PRNG and seed tree, config, `LevelDescription`, span grid and rasterizer, builder.
   - The editor window generates placeholder boxes from a seed.
   - Determinism test.
2. **Measurement:**
   - All metric functions, the validator and the error function.
   - Fixture tests, the metrics report panel, and the overlays for walkable surfaces, movement graph,
     openness and symmetry.
3. **Macro layout:**
   - Target allocation, symmetric skeleton, footprints (rectangles, L-shapes, irregular), the room
     graph with loops, and flat connectors (door, open, straight and winding halls).
4. **Elevation:**
   - Base heights, stacking, stairs, ramps and drops.
   - Validator repairs.
5. **Interiors:**
   - Tiers, blockers, mirroring and symmetry breaking, and the measure–improve–retry loop.
6. **Evaluation:** batch runner, calibration sweep, CSV export, then tuning the constants.
7. **Gameplay:**
   - FishNet sync of the description, the match start flow, share codes.
   - The dev HUD, regen and load commands, runtime overlays, and the sandbox scene.

## Open items and assumptions

- **Checked against the movement code (milestone 1):** `playerHeight = 2.0`, `stepHeight = 0.3` and
  `eyeHeight = 1.877` match the pawn. `jumpHeight = 1.0` is kept on purpose: a tapped jump peaks at
  about 0.75 and a held jump at about 1.85. There is no fall damage, so `maxSafeDrop = 4.0` is a design
  choice. The pawn's capsule radius is 0.75.
- **Still to tune:** in milestone 6, the defaults for `maxHeight`,
  `cellSize`, hall and door widths, max ramp slope, and minimum room size.
- **Measurement choices made in milestone 2** where the spec left room:
  - Walk regions for openness stop at the room's edge, so a doorway doesn't pull another room's floor
    into the count.
  - Room metrics count only surfaces whose top has a walkable role (floor, platform, ramp, stairs). The
    movement graph and the no-stuck rule use every surface with headroom, so a cover top you can jump
    onto still counts there.
  - Merged spans keep only their top role, so the span grid also records every role per column.
    Symmetry features use that to find blockers at any height.
  - Sightlines walk the grid in integer steps. A line through an exact cell corner steps diagonally, so
    it can squeeze past a blocker's corner.
  - Elevation frequency pairs each surface with the closest-height walkable surface in the next column.
  - Loops is `E − V + C` (C = connected components): equal to `E − V + 1` for a connected graph, and
    never negative for a disconnected one.
  - Doorways are ignored in room symmetry by leaving out the cells under connectors (their primitives and
    paths), together with their mirror cells.
  - A jump needs the same headroom as a walk: across the shared edge, the open gap above the higher surface
    must fit `playerHeight`.
  - The bounds check lets the ground slab extend below y = 0.
- **Layout choices made in milestone 3:**
  - New config block `Layout`: room coverage (default 0.5), minimum room side (8), maximum hall length
    (14), wall height (4). Width and depth snap to whole cells so the grid mirrors exactly.
  - There is no level-wide ground. Each room and hall has its own floor slab, and the space between them
    is empty.
  - Rooms are walled on their footprint's boundary cells. Doors and open merges cut through both rooms'
    walls where they touch. Halls cut an opening as wide as the hall floor. The hall floor is 2h + 1 cells
    wide, so the default 3-unit hall comes out 3.5 units wide.
  - A connector's path is the hall centreline. For doors and open merges, it's the run of the opening
    along the wall. Connectors carry a width, which marks the doorway cells that room symmetry ignores.
  - Graph candidates are rooms that touch along a door's length, or whose rough rectangles are within
    the maximum hall length. If those don't connect every room or reach the loop target, a relaxed pass
    tries every other pair, nearest first, with hall limits stretched to the whole level. Loops can
    still fall short when nothing fits; that shows as loops error.
  - Mirrored edges reuse the mirrored geometry of the first built edge in their orbit when it fits.
  - Without mirroring, random layouts land around 0.35 to 0.45 level symmetry, so low symmetry targets
    are not reached yet. That's for tuning in milestone 6.
- **Elevation choices made in milestone 4:**
  - Base heights are multiples of 0.5. Ground orbits go on a few evenly spaced tiers from 0 to
    `levelElevationRange × maxHeight`, so some neighbours share a height and get doors or flat halls,
    and others get stairs, ramps or drops. Mirrored copies share a height. With fewer than three ground
    orbits, each room gets its own tier instead, since level symmetry ignores heights.
  - Stacking moves whole orbits so each copy overlaps a corner of a lower room. Each room keeps at least
    40% of its width and depth outside the overlap, so both still have walls halls can leave through.
    The host also keeps room for a flight up to the stacked room (see the generator v5 choices below).
    The stacked room's floor slab rests on top of the lower room's walls, so it becomes the lower room's
    ceiling where they overlap. Its base is `host base + wall height + 0.5`. Stacking makes the elevation
    range at least 4.5 / maxHeight, and with the overlap limits, stacking targets above about 0.2 are
    reached only partly.
  - Stairs and ramps come in three forms. Between touching rooms, an in-room flight runs inside the lower
    room up to a ledge in its wall. From a host up to the room stacked on it, a flight runs on the host's
    floor up to an opening in the stacked room's edge (added in generator v5). Otherwise a hall's floor
    climbs between flat landings: the first and last few cells, and around each corner. Ramps are capped at a per-cell rise below `stepHeight`,
    because they're sampled once per cell.
  - A drop is a door, or a flat hall at the higher room's height, ending at a ledge in the lower room's
    wall.
  - Repairs add a non-drop connector from each stuck or unreachable room to the nearest sound room (or
    any room if none fits). If four rounds of repairs don't satisfy the hard constraints, the level is
    regenerated from the next level-retry sub-seed. Since generator v5 the budget is 4 × `retries per
    level` attempts, the first passing attempt is the result, and if none passes there is no level (see
    below). "Reduce the drop height" isn't implemented: adding stairs always applied in testing.
  - The span grid merges spans within 1e-3 of each other, so float seams between stacked primitives
    don't read as zero-headroom floors.
- **Interior choices made in milestone 5:**
  - Order: validation and repair run on the empty rooms first, so every connector (repairs included) is final
    and each room knows all its doorways. Interiors then run, and the furnished level is validated again. A room
    whose interior still breaks a hard constraint is emptied (unsound rooms first, then all), and this is recorded
    as a repair. Interiors only run on a level that passed with empty rooms.
  - Interior primitives have their own owner kind, `Interior`, with the room's id. The builder makes a separate
    "Room N interior" object for them, and the window's Connectors stage hides it.
  - Each room is measured on a window around it: its bounding rectangle plus 3 cells, holding every primitive of
    the empty-roomed level that reaches in. Symmetry and the elevation metrics are the report's functions, so
    they match exactly. Openness is estimated with samples every 6th cell and sightlines to every 3rd surface.
    It lands within about 0.05 of the report, sometimes 0.1.
  - Every candidate interior is checked for soundness against the empty room. Every walkable surface of the room
    outside connector cells can be reached from a doorway and left again. No new one-way surface appears, nothing
    outside the room becomes reachable that wasn't before, and walkable surfaces keep their headroom.
  - Tiers are solid `Platform` boxes from the floor, with heights in steps of 0.25. The first tier of an attempt
    sits at the range target. A tier taller than `jumpHeight` gets a flight, which is stairs or (3 in 10) a ramp,
    door-width wide, with a 1-unit landing at its foot. A tier at or below `jumpHeight` is a jumpable step with no
    flight. Tiers are not mirrored.
  - Decks (bridges and balconies) are 0.5-thick `Platform` slabs 2.5–3.5 above the floor, with `playerHeight`
    clear underneath. They always get a flight, and nothing else goes under them. They are placed before tiers,
    while their long flights still fit.
  - A room's walls are raised to its highest tier plus `playerHeight` (up to `maxHeight`), so tiers can't reach
    the wall tops. Walls of a room that has a stacked room resting on them stay at the wall height. Such rooms top
    out at wall height − `playerHeight` (2.0) and fit no decks. Under a stacked room, tier tops keep
    `playerHeight` below its floor, and no blockers go there, so they don't count in the upper room's symmetry.
  - Blockers: walls are 1 cell thick and 2–7 units long. Cover is 0.5–1 by 1–2.5 units and 0.8–1.2 tall. Pillars
    are 1–1.5 units square. Walls and pillars reach the room's (possibly raised) wall top. The odds are 35 / 40 / 25.
  - The base region is the low half of the footprint's bounding box on each axis whose room symmetry target is
    above 0.5 (`SkeletonStage.MirrorThreshold`). A mirror image that falls wholly outside an irregular footprint is
    dropped. Images may not overlap each other unless they are identical (an item on the axis).
  - Symmetry breaking: for each mirrored blocker group and each mirrored axis, with probability 1 − target, the
    group loses its mirror on that axis, and the far copy is placed again at random in the far half.
  - Clear zones: interior cells within 2 units (Chebyshev) of any opening cell, and in-room flights with a 1-cell
    margin and a 2-unit run-out at their foot.
  - Local search: 65% of moves aim at a metric outside tolerance, chosen with odds by its error, and the rest are
    random. A move adds, removes, translates or resizes an item, changes a tier's height, moves its flight, or
    breaks or restores a blocker's mirroring. Every change applies to the whole mirror group. A move is kept when
    the search error drops. The search error is the room's error plus 0.05 × the raw distance to every target, so
    the search keeps pulling inside tolerance. The search stops at the iteration budget, at zero error, or after
    60 rejected moves in a row.
  - Attempts: `retries per room` attempts. The first draws from `Stream(seed, Interiors, room)`, and attempt k from
    `Stream(Derive(seed, Interiors, room), RoomRetry, k)` (new stage id 10). The best attempt is kept, and the
    empty room counts as a candidate.
  - What the dials reach (sweep of seeds 11–14 over 10 settings variants; every level passed every constraint).
    Mean room error per room drops from 1.0–1.8 empty to 0.15–0.6 furnished ("tall" goes from 2.3 to 0.9).
    Openness and elevation range land close. Symmetry can't go much below about 0.35, because a room's own walls are symmetric
    features. Overlap targets of 0.3 reach about half. Frequency targets of 0.6 fall short until the normalization
    is calibrated in milestone 6. An openness target of 0.85 ends a little further off than the empty room,
    because tiers cut sightlines and the search trades it for the elevation targets.
  - The report's room table has an "empty" row under each room: the same room before interiors, with openness
    estimated. The retry count doesn't include room attempts. They're in that row's tooltip.
  - Speed: about 3–4 s per 6-room level at the default budget (8 s for 8 rooms), nearly all of it the interior
    search.
- **Connector sizes and the no-stuck guarantee (generator v5):**
  - `LevelGenerator.Generate` returns a `GenerationResult`. A level is returned only if it passes every hard
    constraint. Whole-level attempts run from the seed and then from `Derive(seed, LevelRetry, k)`, up to
    4 × `retries per level` (16 by default). The first one that passes is the result, so a seed still gives
    one level. If none passes, the result has no level, only the reason: the constraints the closest attempt
    broke. Its trace keeps that attempt for the editor's stage views.
  - The layout derives its sizes in cells from the connector sizes (`LayoutSizes`). With h the hall floor's
    half-width, a hall exit needs 2h + 3 cells of straight wall. The minimum room side is the larger of
    `minRoomSide` and 2h + 5. Footprint arms are at least 2h + 3 wide (or door + 4, if that is wider). Rooms
    that don't touch keep at least 2h + 3 cells apart (or 2 units, if that is more), so a winding hall can pass
    between any two. With the earlier h + 3 gap, a 50 × 50 level with 6-unit halls needed up to 11 level attempts
    per seed; with 2h + 3 it needs at most 6 of the 16 (16 seeds). Touching rooms share at least door + 4 cells.
  - After footprints, a room with no hall exit, no shared wall long enough for a door, and (stacked rooms and
    their hosts) no room for the flight between them gets rectangles for its whole orbit. If it still has
    none, the attempt stops and the next level retry runs. This is recorded as a repair.
  - Stacked rooms: the rise to a stacked room is wall height + slab (4.5 by default), above `maxSafeDrop`, so a
    drop can't connect it, and `DoorRuns` needs both rooms on one layer. A new connector form joins a stacked room
    to its host: a door-wide opening in the stacked room's edge where it crosses the host, a landing at the
    stacked floor's height on the host floor in front of it, and stairs or a ramp running down from the landing
    across the host floor, clear of the overlap. Stacking places a room only where its host keeps that flight's
    length (plus landing, foot row and wall) clear in front of one of the stacked room's edges, along a stretch a
    door wide. A stacked room that only this flight can reach gets its host edge before the rest of the graph.
  - The settings check (`SettingsCheck`) runs before generating and in the config inspector. Errors are
    refused: rooms of the minimum side and gap that don't fit the level as a plain grid (the skeleton's
    fallback); door, open and hall weights all 0; stairs and ramp weights both 0 while the elevation range or
    stacking target is above 0. Warnings: values sanitizing changes, a minimum room side raised to fit a hall
    exit, both hall weights 0, and stacking that can't happen (max height too low, or a flight longer than any
    room).
- **Calibration constants** (the elevation frequency normalization, the symmetry-break fractions, the
  mirroring threshold) are set from calibration sweeps, not by guessing.
- **Spawn points** aren't covered yet. When they're added, they must lie in R (the reachable,
  strongly connected component), and they're a natural place for later fairness metrics.
- **Generation speed** isn't a goal yet. If live generation is too slow, the first levers are the
  sample density for openness, the improve-iteration budget, and incremental re-measurement inside
  local search.
