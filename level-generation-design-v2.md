# Level generation design, v2: outdoor plots and buildings

This replaces the generator in [level-generation-design.md](level-generation-design.md) (v1). The v1 groundwork
(PRNG, span grid, movement graph, validator, report, builder, editor window) stays. The v1 stages that make
walled rooms joined by halls go. See [What carries over](#what-carries-over-from-v1). This doc was approved on
2026-10-22, defaults included. v1 is kept only for its recorded measurement choices, which are copied into
[Open items](#open-items-and-assumptions) here where they still apply.

## Intent

Build a procedural generator for the first-person free-for-all arena that makes **open, mostly outdoor levels**.

Players have to find each other and chests easily, so:

- Spaces are open, and most of the level is outdoors.
- Getting from any point to any other is quick and unobstructed. The exception is going into or out of a
  building, and buildings must still be easy to walk through.

Every part of the level should look distinct, so:

- Layouts are asymmetric.
- Some plots carry buildings.
- Drop-offs and slopes run between plots and within them.
- Plots and buildings have varied, arbitrary-angle outlines.
- The level's own outline isn't a rectangle: it follows plot edges, and nothing shows the box the diagram was
  drawn in.

Requirements kept from v1:

- **Shareable:** a level is fully determined by `(generatorVersion, seed, config)`, encoded as one share code.
- **Configurable size:** W × H, with each side between 50 and 100 units.
- **No one gets stuck:** this is a hard rule (see [Hard constraints](#hard-constraints)).
- **Generate in the editor and during gameplay**, with tools that show how well the algorithm hits its targets.

Art hookup is in scope: materials, building material profiles, prop prefabs and decals (see
[Art](#art-materials-prop-prefabs-and-decals)). So is seed-based multiplayer: the host picks a seed and every peer
generates the level itself (see [Sharing and networking](#sharing-and-networking)).

Out of scope for now: chests, AI navigation, art authoring itself (the generator only places what a theme supplies),
and generation speed.

## Ground rules for the implementing session

- **Build the metrics before the generators.** Path stretch comes first and gets tested on hand-built fixtures.
- **Keep the core free of Unity scene code.** This is the same split as v1 (see [Code layout](#code-layout)).
- **Make every milestone visible in the editor window.**
- **Stop after each milestone** for review. Don't commit unless asked.

## Player constants

These are unchanged from v1. They are checked against the pawn there.

| Constant | Value | Used for |
|---|---|---|
| `stepHeight` | 0.3 | Height changes at or below this are walked. It also caps the rise per grid cell on slopes and stairs. |
| `jumpHeight` | 1.0 | Height changes above `stepHeight` and up to this are two-way: jump up, drop down. |
| `eyeHeight` | 1.877 | Kept for later sightline work. No v2 metric uses it. |
| `playerHeight` | 2.0 | The headroom a walkable surface needs. Door and window openings are at least this tall. |
| `maxSafeDrop` | 4.0 | Drops up to this are one-way edges. Larger drops are no edge in the movement graph. |
| capsule radius | 0.75 | Openings and walkways are at least 1.5 wide. Doors default to 2. |

The movement code has no max walkable slope (`MovementConfig` only snaps to the ground), so walkable slope is
limited by the span grid instead. A slope is walkable when its rise per cell is at most `stepHeight`. With
`cellSize` 0.5 that is about 31°, so ramps and banks are capped at `rampMaxSlope` = 30°. Anything steeper is a
vertical face.

## Terminology

- **Plot:** a convex polygon of ground at one height, or with a planar slope. Plots tile the level. This is
  v1's "room", renamed because it is mostly outdoors.
- **Sub-plot:** a piece of a plot after carving (stage 6). Sub-plots are plots with a parent.
- **Edge:** the shared boundary between two neighbouring plots. Each edge is split into **segments**, and every
  segment has one **transition** type.
- **Transition:** what a segment is to walk across: flat, ledge, cliff, ramp, bank, or blocked.
- **Building:** a structure on one plot, with floors, outer walls, a floor plan, balconies and stairs.
- **Spine:** the walk-through route on a building's ground floor, connecting its entrances.
- **Prop:** a placeholder obstacle near plot edges: fence, car, tree.
- **Primitive, span grid, walkable surface, walk region, movement graph:** these mean the same as in v1.

## Configuration

`LevelGenConfig`, a ScriptableObject. Presets are assets. The numbers are v1's where v1 had them.

```
generatorVersion        int     (v2 starts at 6; v1 share codes are refused)
seed                    uint64
width, depth            50..100 units
plotCount               4..8                (v1 roomCount)
maxHeight               12                  (vertical extent for bounds; boundary walls are exempt)
cellSize                0.5

plots
  sizeSpread            0..1    how unequal plot areas are (power-diagram weights)
  regularity            0..1    Lloyd relaxation iterations, 0..2
  minPlotSide           8       (v1 minRoomSide) smallest inscribed width
  minSharedEdge         door width + 2: shorter shared edges don't count as adjacent
  heightStep            0.5     plot base heights are multiples of this

metrics
  pathStretch           level   { target ≥ 1 (default 1.3), tolerance, weight }
  levelElevationRange   level   { target 0..1, tolerance, weight }
  elevationRange        per plot MetricTarget { target, spread, skew, tolerance, weight }
  elevationFrequency    per plot MetricTarget

transition weights      relative odds of flat / ledge / cliff / ramp / bank where each fits
transition sizes        ramp width (hall width, 3), rampMaxSlope 30°, stair riser 0.25,
                        curveFraction 0.25 (sag and crest curves), curveSegments 4 (straight pieces per curve),
                        flareSlopeDegrees 40 (graded sides)

buildings
  buildingCount         0..plotCount (default 2)
  floors                1..3
  floorHeight           4               (v1 wall height): 0.5 slab + 3.5 clear; at least 2 × playerHeight
  raisedGroundOdds      0..1            odds the first floor is lifted off the ground
  fullPlotOdds          0..1            odds the footprint fills the plot
  roofAccessOdds        0..1
  doorWidth 2, doorHeight 2.5
  windowSill 0.9, windowHeight 2.2, windowWidth 1.5..2.5
  balconyDepth 2, railingHeight 1.1, spineWidth = hall width (3)
  interiorWalls per floor 0..3
  floorResizeMin, Max   0.1, 0.5        how much each floor differs in size from the one below (10–50%)
  floorGrowOdds         0.25            odds a floor is bigger than the one below; otherwise it is smaller

terrain (stage 5)
  subRoomArea           35              floor area per sub-room
  min, maxOperations    2..5            operations per room
  min, maxHeightChange  0.25..1         how far a raise moves a sub-room, up or down
  min, maxTiltDegrees   3..10           slope a tilt adds
  maxGradeDegrees       20              no terrain triangle gets steeper (at most rampMaxSlope)
  sinkholeDepth         0.9             how far a sinkhole sits below its rim (over jumpHeight traps: warned, allowed)
  weights               raise 1, tilt 1, raise and tilt 1, sinkhole 0.5, nothing 0.5

props
  density               cars and trees per 100 units of plot edge
  weights               car / tree
  fencedRoomOdds        0.5             odds a room has any fences
  min, maxFencedSides   1..3            sides a fenced room lines
  fenceMargin           1.5             from the room's edge to its fence
  oldFenceOdds          0.35            odds a fenced room's fences are old (a few small gaps)
  min, maxOldGaps       1..3            small gaps per run of an old fence
  min, maxOldGapWidth   0.6..1.2        always narrower than a gap to a neighbouring room

boundary                wall height 50, thickness 1
search budget           improve iterations, retries per plot, retries per level
player constants        (table above)
```

## Metrics

Symmetry, openness, overlap, stacking and loops from v1 are dropped. Every metric is a pure function of the span
grid, the plot map and the movement graph.

### Path stretch (level)

Path stretch measures how much detour the level forces, compared with walking in a straight line.

1. **Samples:** walkable outdoor surfaces in R (the reachable strongly connected set). Outdoor means the top of a
   plot (its terrain), bank or ramp primitive, not a building, balcony or prop. Take every k-th cell on a
   deterministic lattice (default k = 8, so every 4 units).
2. **Distance:** run 8-neighbour Dijkstra over the movement graph. A step costs its horizontal length (cellSize, or
   √2·cellSize diagonally). A diagonal step needs both of its orthogonal neighbours to be traversable from the
   source. Walk, jump and drop edges all count, and drops stay one-way, so `d(A→B)` and `d(B→A)` can differ.
   Building interiors and spines are routable, but they are never samples. That is "entering and leaving a
   building" is exempt as a destination but not as a route.
3. **Stretch of a pair:** `d(A→B) / octile(A, B)`. Dividing by the octile distance instead of the Euclidean one
   cancels the grid's diagonal bias, so open flat ground scores exactly 1.0.
4. **Pairs:** ordered pairs whose samples lie in different top-level plots and are at least 8 units apart.
5. **Level value:** the mean over pairs. The report also shows the 95th percentile and the worst pair, and the
   overlay draws the worst pair's path.

Error is `weight × max(0, |measured − target| − tolerance)` for every metric. Values above the target are the usual
failure. Values below it mean the level is flatter and emptier than asked.

### Elevation range (plot), elevation frequency (plot), level elevation range

These are the v1 functions applied to plots instead of rooms, including the per-plot target allocation (v1 stage 1).
They are measured and scored; since the terrain stage has its own knobs (stage 5), they no longer drive generation
directly. Level elevation range is `(max plot base − min plot base) / maxHeight` over plots.

## Hard constraints

1. **No one gets stuck.** This is v1's rule on the full span grid. R is one strongly connected component and
   contains every plot's largest walk region, every transition's surface, every building floor and spine, and
   every balcony. A reachable roof is in R. An unreachable roof is allowed. A sinkhole's floor is exempt: it may
   be a dead end when `sinkholeDepth` is over `jumpHeight`.
2. **Every plot has at least one two-way transition** (flat, ledge, ramp or bank) to a neighbour.
3. **Every building is walk-through.** All of a building's entrances are joined to each other by two-way walking
   on its ground floor (see [Walk-through buildings](#walk-through-buildings)).
4. Everything except the boundary walls lies inside `width × depth × maxHeight` (the bounding rectangle; cut cells
   inside it are void).
5. Walkable surfaces keep `playerHeight` of headroom. Doors, windows, stairs and walkways meet their size limits.
6. The top-level plot count matches `plotCount`.
7. **No overlapping geometry.** No two solids share volume, and no two faces of different solids lie on each other
   facing the same way (see [No overlapping geometry](#no-overlapping-geometry)). Solids may touch only face to face,
   back to back.
8. **Building mix.** The built buildings match the drawn mix (see [Round 2](#round-2-buildings-fences-and-cars-milestones-1113)).

## No overlapping geometry

Overlapping primitives are what cause z-fighting. They are prevented where geometry is made, not hidden when it is
meshed. Every stage follows the rules below. The generator doesn't run the overlap check (see
[Performance](#performance)): the tests and the editor's report do.

**Rules.** Solids may touch, but never interpenetrate. Two solids that meet share a boundary plane, and their faces
there point opposite ways. Wherever a stage puts one solid on, in or against another, it does one of these:

- **Stack:** the upper solid's bottom is exactly the lower solid's top. This only works on a flat or planar top under
  the whole footprint.
- **Carve:** the solid that was there first is cut so the new one takes its place. Ground polygons get the new
  footprint subtracted and are split back into convex pieces.
- **Stop short:** the new solid ends at the other's face, or the other ends at its face.

**Where each source of overlap is prevented:**

| Source | Rule |
|---|---|
| Ramps and banks over plot ground | **Carve.** The plot's ground (and the terrain sub-rooms under the transition) loses the ramp's or bank's footprint, and the slope's own body runs down to the ground's bottom. The pieces of one ramp (bands, flares, curve segments) tile their footprint and share edges, never volume. |
| Ramp and bank sides against a cliff | **Stop short.** The slope's body ends at the cliff plane, so its face there and the cliff face are back to back. |
| Blended-ground split tops and terrain triangles | **Carve.** Terrain is cut by the same transition footprints as the plot ground, before it is split for blending. |
| Floor and roof slabs against walls | **Stop short.** A slab covers its floor polygon inset to the walls' inner faces. Walls run from the top of the slab below to the bottom of the slab above, or to the roof's top for the outermost walls. A wall top and a slab top can lie in one plane side by side, but never on top of each other. |
| Door thresholds and openings | **Stop short.** A threshold under a door is a wall piece from the slab below up to the floor level, between the wall's faces. It meets the floor slab's inset edge back to back. |
| Stair wells | **Carve.** The well is cut from the slab polygon, and the cut edge stops at the wells' wall inner faces. |
| Overhanging and terraced upper floors | **Stop short.** A larger floor's slab is inset to its own walls. The walls of the floor below end under it. A terrace parapet runs only along the terrace's open edges and stops at the upper floor's wall face. |
| Wall corners (outer walls, interior walls, railings, parapets, boundary walls) | **Mitre or butt-join.** Two walls meeting at a corner are cut to the corner's bisecting plane (mitre), or one ends at the other's face (butt). Never overlapped by half their thickness. Walls become Prisms (a mitred quad per wall) where yawed boxes can't express the cut. |
| Interior walls against outer walls | **Stop short:** they end at the outer wall's inner face. |
| Plinths, porches and balconies | **Stack** on flat building plots: a plinth's bottom is the plot top. Porches and balconies **stop short** at the outer wall's outer face. |
| Props on the ground | **Stack** on the lowest ground under them, as now. A prop that would cross a ground seam or slope is moved or dropped, not sunk. Fence pieces butt-join. |
| Boundary walls against plot sides | **Stop short:** they stand outside the outline, touching it back to back. Each wall is its edge's band cut by the mitre lines at both ends, so a band shorter than it is thick comes to a point instead of crossing itself, and it **gives way** (carve) to every plot and to the walls made before it: past a short band's point its neighbours' bands meet, and where the outline touches itself at a corner the mitre can be taken with the wrong edge. |
| Openings and wall gaps that overlap | **Stop short.** Two entrances' doors close together on one side can overlap; an opening's sill and lintel start where the one before it ends. The same holds for gaps in an interior wall. |
| Landings and platforms against walls | **Stop short.** After a building's solids are made, each platform is cut back to the faces of the building's walls and railings beside it at its height: a floor scaled from the one below, or a core at a corner, can bring a wall's band a hair over a landing. |
| Clipping by rounding | Polygon clipping is exact to `ExactEpsilon` (1e-5, above a float's spacing across a level), and a clipper's or hole's edges shorter than 1e-3 are ignored: such an edge's direction is the rounding of its ends, and one that doubles back put the whole polygon outside it, so a later slope kept its full footprint over an earlier one. No solid is narrower than `Polygon.MinSolidWidth` (0.01): a needle or hairline left along an edge that it and a neighbour reached by different roundings lies a hair over the neighbour, face on face, so `AddPrism` and the carving leave it out (a crack too narrow to see or fall through). |

**Overlap check.** `OverlapCheck` in the core checks hard constraint 7. The generator's validation skips it
(`LevelEvaluator.Validate(…, overlaps: false)`), since its cost grows with the solid count and the rules above hold
by construction; the guarantee tests, `LevelEvaluator.Evaluate` (the editor's report) and a plain `Validate` run it:
- Every solid primitive is treated as a convex solid (a flight as one solid per step). Solids are bucketed on the XZ
  grid, and each pair that shares a bucket is tested.
- A pair fails if its intersection has volume above 1e-6, or if two of its faces lie within 1e-4 of the same plane,
  face the same way, and overlap in area above 1e-4.
- Failures are listed in the generation report, with both owners and the location, and drawn by an overlay.
- Because the rules make overlaps impossible by construction, a failure means a stage broke a rule. The guarantee
  test asserts zero failures on every preset, so a broken rule shows up as a test failure, not as a silent retry.
  `Tools/LevelGenProfile --overlaps` checks every level of a wider sweep and lists what overlaps by owner kinds.

These rules change the description for the same seed, so `generatorVersion` is bumped when they land.

## Pipeline

```
Config + seed
  → 0 seed tree
  → 1 allocate per-plot targets
  → 2 partition into plots
  → 3 plot heights + edge transitions
  → 4 buildings
  → 5 terrain in the remaining plots
  → 6 boundary walls
  → 7 props
  → 8 measure, improve, validate + repair
  → LevelDescription → builder
```

Everything up to stage 7 works in 2D polygons plus heights. Stage 8 rasterizes into the span grid and measures.
Stages 3–5 also check their own soundness on small local graphs as they go, as v1's interiors did.

### 0. Seed tree, 1. allocation

These are unchanged from v1 (stage ids are stable integers, and each plot and building draws only from its own
stream).

### 2. Partition into plots

This is partition first, shapes second, so plots tile the level and every neighbour shares an edge.

1. Scatter sites with a minimum spacing (Poisson-disk style) from the partition stream, over a box padded by one
   site spacing past every side of the level. The spacing is set so that about `plotCount + 1.5` cells fit wholly
   inside the level.
2. Give each site a weight from `sizeSpread`, using the allocation function with the plot stream.
3. Build the **power diagram** (weighted Voronoi) clipped to the padded box. Half-plane clipping of the box per
   site is enough: O(n²) for a few dozen sites, no library needed.
4. Run `regularity × 2` Lloyd iterations, moving sites toward their cell centroids.
5. **Cut** every cell that isn't wholly inside the level rectangle, so the rectangle never shapes a plot and the
   outline is made of cell edges. Redraw (next sub-seed) if fewer than `plotCount` cells are left or any is
   narrower than `minPlotSide`.
6. Cut the surplus cells from the outline (cells next to a cut cell) until `plotCount` are left. The plots must
   stay joined by shared edges of at least `minSharedEdge`; random choices are tried until they are, and the
   partition is redrawn if none works. Cut cells are void behind the boundary walls.
7. Neighbours are plots whose shared edge is at least `minSharedEdge`. Shorter contacts are left as plain
   boundaries and get no transition.

Power-diagram cells are **convex**, and carving with straight chords keeps them convex. Everything downstream
relies on this: polygon inset, point-in-polygon, chord splits and building footprints all stay simple.

Asymmetry comes free: random sites, unequal weights and partial relaxation give no mirror structure, and there's no
symmetry metric to steer.

### 3. Plot heights and edge transitions

Heights and transitions are chosen together, because whether a height difference works depends on the edge.

**Heights:**

1. Choose base heights in `heightStep` multiples spread over `0 .. levelElevationRange × maxHeight`.
2. Each plot may get a planar slope instead of a flat top: the plane tilts across the plot with at most
   `rampMaxSlope`, and its rise stays within the plot's elevation range target. Flat is the default, and a building
   plot is always flat.

**Transitions:** each edge is split into 1–3 segments along its length (each at least `doorWidth + 2` long). For a
segment where the two sides differ in height by Δ along it:

| Transition | Fits when | Geometry |
|---|---|---|
| Flat | Δ ≤ `stepHeight` | Nothing. The two plot tops meet. |
| Ledge | Δ ≤ `jumpHeight` | Nothing. The higher plot's side is a short face. Two-way. |
| Cliff | Δ ≤ `maxSafeDrop` | The higher plot's side is a vertical face. One-way down. |
| Ramp | any Δ, at most one slope per pair of plots | A yawed ramp `ramp width` wide at the segment's middle, against the face, on the lower side, running about Δ / tan(`rampMaxSlope`). The rest of the segment is a cliff or blocked face. |
| Bank | any Δ, at most one slope per pair of plots | The whole segment becomes a sloped strip cut from the lower plot, about Δ / tan(`rampMaxSlope`) deep. The more natural outdoor look. |
| Blocked | Δ > `maxSafeDrop` | A vertical face, no edge. Allowed only while constraints 1 and 2 still hold. |

Choose by `transition weights` among those that fit, then check on the **plot graph** (plots as nodes; two-way
and one-way edges from the transitions):

- It is strongly connected, and every plot has a two-way transition (constraints 1 and 2, early).
- If not, turn the cheapest cliff or blocked segment next to a failing plot, between plots that have no slope yet,
  into a ramp (or a bank, where no ramp fits), and repeat.

Two plots are joined by **at most one slope**, a ramp or a bank, however many segments their edge has. Their
other segments are flat, ledge, cliff or blocked.

**Shape of a ramp or bank.** It curves in from the lower ground at its foot (a sag curve) and rounds over onto the
higher ground at its top (a crest curve). The grade rises linearly over the first `curveFraction / 2` of the run,
holds at `rampMaxSlope`, and falls linearly over the last `curveFraction / 2`, so the run is `1 / (1 − curveFraction
/ 2)` longer than a straight slope's. It is built from `2 × curveSegments + 1` planar bands: `curveSegments` chords
of the sag, the straight middle, and as many chords of the crest. Its sides are **flared**: each band gets a planar wedge falling from the slope's
edge to the lower ground at `flareSlopeDegrees`, so there is no drop-off at the side. A ramp flares on both sides; a
bank flares where it ends mid-edge (an end at a plot corner already reaches the next edge). Flares are clipped to the
lower plot, and the keep-out zones and terrain pins cover them. At the default 4 segments per curve, a ramp is 9 bands
and up to 18 flare wedges, about 22–27 prisms; a bank about 9–27.

A ramp or bank depth must fit in the lower plot with `minPlotSide / 2` left over, otherwise that transition
doesn't fit there. Both are prisms whose planar top is fitted to the two plots' ground (either may be sloped):
the higher ground along the face, the lower ground along the foot. They fit when that plane is within
`stepHeight / 2` of both and no steeper than `rampMaxSlope`. To avoid pockets you can drop into but not leave:

- a ramp keeps a walkway's width (1.5) from every other edge of the lower plot;
- a bank that ends at a corner of the lower plot is mitred along the corner's bisector instead of cut square.

Plot ground is one prism per plot, solid from below `y = 0` to its top. Cliff faces are the sides of the higher
prism, so they need no extra geometry.

### 4. Buildings

Choose `buildingCount` plots with the building stream. Plots that are too small for a 1-floor building with a spine
are skipped. For each building:

1. **Footprint.** With `fullPlotOdds` it **fills the plot**: the plot inset by `balconyDepth` (the ground-floor
   porch goes in the inset). Otherwise it is an **inscribed shape**: a convex polygon of 4–6 sides at any angle, an
   L made of two convex parts, or the plot inset scaled down. It keeps at least spine width of ground all around,
   so it can also be walked around.
2. **Floors.** Floors are 1..3, cut to fit `maxHeight`. The first floor sits at one of:
   - **Ground:** flush with the plot.
   - **Raised** by 0.5–1.5: a plinth, with a porch and steps at each entrance.
   - **On stilts** one `floorHeight` up (at least `playerHeight + slab`): open ground underneath, with pillars only.

   Upper floors are `floorHeight` apart.

   **Floor sizes.** Each floor above the first is the floor below it, the same shape, scaled by 10–50%
   (`floorResizeMin..Max`): usually smaller, bigger with `floorGrowOdds`. The scaling is about the stair core's centre
   (or the first floor's centroid when there is no core), so a smaller floor always lies within the one below.
   - A **smaller** floor leaves the floor below's roof exposed as a **terrace**: a railing (a parapet on the wall
     below) runs along its outer edge, and the floor above gets doors onto it where it is wide enough to walk.
   - A **bigger** floor **overhangs** the one below. It must stay on the plot and clear of the plot's ramps and banks.
   - Every floor must keep the stair core inside it and stay at least the minimum footprint width. A resize that
     breaks a rule is halved, and then dropped (the floor keeps the size of the one below).
3. **Entrances.** One entrance on the footprint side nearest each two-way transition of the plot, so each direction
   a player arrives from has a door facing it, with at most one per side. At least 2. A full-plot building also
   gets one per one-way transition that leads into it.
4. **Spine** (the walk-through route). See [Walk-through buildings](#walk-through-buildings).
5. **Access plan.** This is decided **before** the floor plan, because stairs take a lot of space. For each upper
   floor, and for the roof if it is reachable (`roofAccessOdds`):
   - **Inside:** a stair core. At 0.25 risers a 4-unit floor height is 16 risers, 8 units of run. Cores are
     switchback: two 4-unit flights, `doorWidth` wide, with a landing, so about 4.5 × 5.5. They are placed off the
     spine, against an outer wall, and stack on every floor they serve.
   - **Outside:** an exterior flight along an outer wall, from the porch or ground up to a balcony.
   - Every floor gets at least one route. A reachable roof is reached from the top floor's core or from an exterior
     flight to a roof hatch.
6. **Shell.** Floor slabs are footprint prisms. The roof is a slab with a `railingHeight` parapet if it is
   reachable. Outer walls are one per footprint side, mitred or butt-joined at the corners (see [No overlapping geometry](#no-overlapping-geometry); the original rule overlapped them by half their thickness at the
   corners so the angled joints close.
7. **Floor plan.** Each floor gets 0–3 interior walls as chords of the floor polygon, which keeps every piece convex.
   Each chord has a door-wide gap. On the ground floor, chords may cross the spine only through a spine-wide gap.
   Every piece must touch a door, a core or a balcony door, which is checked on the floor's piece graph.
8. **Balconies.** The ground-floor porch runs around the footprint. On a full-plot building it fills the inset; on
   an inscribed building it is optional per side. Upper floors get balconies on 1–3 sides. Balconies are slabs
   `balconyDepth` deep with railings on their open edges. Gaps are left only where an exterior flight lands.
9. **Openings.** Doors go where entrances, balconies and cores need them. Windows go in the remaining wall stretches
   at `windowSill`, `windowHeight` tall. They have no collider, so you can shoot through. The sill (0.9) is below
   `jumpHeight` and the opening is taller than `playerHeight`, so you can jump through windows, and the movement
   graph sees them as jump edges.
10. **Local check.** All entrances are joined on the ground floor, every floor and balcony is reachable and leavable,
    and headroom holds. On failure, retry the building from its next sub-seed. After the retry budget, drop the
    building, record it in the report, and continue.

Railings at 1.1 are above `jumpHeight`, so the movement graph treats them as walls. In play a held jump (about 1.85)
clears them, and that is fine. It's a fall onto ground that is already in R, and there is no fall damage.

#### Walk-through buildings

Every building has a **spine**: a ground-level route `spineWidth` wide joining all its entrances. It's built
differently depending on how the first floor sits:

- **On stilts:** the open ground under the building is the spine. Pillars are kept off a straight strip between
  each pair of opposite entrances. This is the cheapest walk-through and needs no doors.
- **Ground or raised:** the spine is a **lobby** polygon near the footprint centroid (at least spine width across),
  plus a straight strip from the lobby to each entrance. The strips and lobby are reserved before the access plan
  and floor plan:
  - Stair cores, interior walls and props never sit on the spine.
  - Interior walls may cross a strip only with a gap the full spine width.
  - A raised floor has steps or a ramp on the porch at each entrance, so the spine is two-way from outside.

How this fits the rest of the algorithm:

- Stage 3 has already decided which plot sides are walkable, and stage 4 step 3 points an entrance at each of them.
  So whichever way you come into the plot, there's a door ahead of you and a straight, wide route through.
- Path stretch routes through spines, so a building on a chokepoint plot costs only the small detour through its
  lobby. If a spine still makes stretch too high, stage 8 can add an entrance or widen the lobby.
- Hard constraint 3 checks the spine directly, so a floor plan can't quietly close it.

### 5. Terrain

This shapes the ground inside each plot (room) that has no building, so a room isn't one flat or tilted plane.

1. Split the room into **sub-rooms** with a Voronoi pattern of its own: about one per `subRoomArea`, so bigger rooms
   get more. Sites are scattered with a minimum spacing, and cells are clipped to the room exactly as the partition
   does it, so they share their vertices bit for bit.
2. Every sub-room has a plane, at first the room's own. Each sub-room is drawn as a fan of triangles from its site,
   and each corner shared by sub-rooms takes the mean of their planes there. So the ground stays continuous: a
   changed sub-room bends its neighbours with it.
3. Repeat `minOperations..maxOperations` times: pick a sub-room and, by the operation weights, either
   - **raise** it: move its plane up or down by `minHeightChange..maxHeightChange`;
   - **tilt** it: add `minTiltDegrees..maxTiltDegrees` of slope in a random direction;
   - **raise and tilt** it;
   - make it a **sinkhole**: lower it by `sinkholeDepth` (default 0.9, just under a jump) with corners of its own, so
     its whole rim is a jumpable ledge. A sinkhole deeper than `jumpHeight` traps players: the settings check warns,
     and the no-stuck constraint exempts sinkhole floors so such levels still pass; or
   - do **nothing**.

   An operation that makes any triangle steeper than `maxGradeDegrees`, or lifts it over `maxHeight`, is undone.
4. **Pinned ground:** the room's edges always keep the room's plane, so the transitions stage 3 chose between rooms
   still fit, and so do the sub-rooms under the room's ramps and banks (plus a unit of run-out). Pinned sub-rooms are
   never picked, and sinkholes are only made from sub-rooms that don't touch the room's edge.
5. Sub-rooms are geometry only: the room stays the plot for transitions, the plot graph, the validator and the
   metrics. Where a sub-room's corner lies on another polygon's edge (a neighbouring room's), it is inserted there as a
   collinear vertex, so all ground polygons tile exactly.

### 6. Boundary walls

A wall stands along every edge of the playable outline, which is made of plot edges against cut cells. Each
is a prism, 50 tall and 1 thick, just outside its plot, mitred with its neighbours at the outline's corners and giving
way to the plots and earlier walls (see [No overlapping geometry](#no-overlapping-geometry); exempt from the bounds
check). Plots that touch the boundary need no edge treatment there.

### 7. Props

The first catalogue is placeholder shapes, yawed to the nearest plot edge:

| Prop | Shape | Role | Notes |
|---|---|---|---|
| Fence | Thin box, 0.1 × 1.2 tall, in 2-unit pieces lining a room's sides | Cover | Blocks movement (above `jumpHeight`). |
| Car | Box 4.5 × 1.9 × 1.5 | Cover | Blocks movement. Its top is unreachable from the ground. |
| Tree | Trunk 0.6 square × 3 (Pillar) and a canopy box 3 × 3 × 2 from 3 to 5 (Ceiling) | Pillar / Ceiling | Only the trunk blocks at head height. |

**Placement:**

- Props go in a band 1–3 units inside each plot's edges,
  at `density` per 100 units of edge.
- **Keep-out zones:**
  - every ramp and bank, plus a 2-unit run-out at its foot
  - a 2-unit strip along every flat and ledge segment, so props don't wall off a walkable edge
  - building footprints and porches, plus a 2-unit clear zone at each entrance
  - a spine-wide extension outward from each entrance
- **Fences** line rooms rather than standing in the band. With `fencedRoomOdds` a room has fences, on
  `min..maxFencedSides` of its sides (otherwise none), each run `fenceMargin` in from the edge. A run is cut wherever it
  meets a fence keep-out zone. That leaves a gap at every way into a neighbouring room: a gate `doorWidth + 2` wide at
  each flat or ledge crossing, and the whole of every ramp and bank, both where it starts on the lower room and where
  it arrives on the higher one. It also leaves gaps at building entrances and exterior flights. So a fence never
  blocks a ramp or bank. With `oldFenceOdds` a room's fences are old: each run gets
  `min..maxOldGaps` small gaps `min..maxOldGapWidth` wide. Fences are 2-unit pieces, each standing on the ground under
  it and a fence's height over its highest point.

### 8. Validate and retry

This is what milestones 3–6 already built. The local search and targeted repairs once planned here were dropped
(2026-10-22), because a level attempt is cheap (see [Performance](#performance)), a retry clears the rare failure, and
path stretch already lands lower (better) than its target.

1. Rasterize and run the hard constraints.
2. On failure, use the in-place fixes that already exist: the plot-graph ramp repair (stage 3), building retries and
   drops (stage 4), and the prop fallback (stage 7). Then validate again.
3. A level that still fails is regenerated from the next level-retry sub-seed. After the budget, generation reports
   failure as in v1. A level that breaks a hard constraint is never handed out.
4. Measure path stretch and the elevation metrics for the report only. Nothing steers toward them.

## Performance

Every client generates the level at match start (WebGL: single-threaded IL2CPP), so generation time is a budget.
`Tools/LevelGenProfile` (see its README) times 30 seeds on each guarantee preset and the `LevelGenConfig.asset`
values, stage by stage, on the Editor's Mono, and hashes every level so a change meant to keep levels can be checked.

Measured 2026-10-29 (mean ms per level on the Editor's Mono outside the Editor; the Editor itself is about 2× slower):

| preset | before (v16) | after (v17) | worst seed before → after |
|---|---|---|---|
| default 70×70, 6 plots | 411 | 85 | 1426 → 136 |
| small 50×50, 4 plots | 81 | 28 | 196 → 44 |
| dense 100×100, 8 plots | 1540 | 219 | 5580 → 370 |
| tall (3 floors, steep) | 568 | 99 | 2098 → 292 |
| `LevelGenConfig.asset` | 1549 | 193 | 9421 → 291 |

What made it:
- **No overlap check in the generator.** It tested every touching pair of solids (most pairs touch, since solids meet
  face to face), so it grew with the solid count and was most of every validation: 120–220 ms an attempt. The stages
  now make overlap-free geometry by construction (see [No overlapping geometry](#no-overlapping-geometry)); the tests
  and the editor's report still run it. It also caused most retries (overlapping slopes and boundary walls), so
  attempts per level fell too (default 2.6 → 1.6).
- **The buildings' local check** validates without the overlap check: it only reads walkability, walk-through and
  headroom.
- **Interior props' route check** keeps the free-cell grid as props are added and clears only the cells near a
  candidate, instead of rebuilding the room's grid (point-in-polygon and a distance to every prop per cell) for every
  candidate: 900–1050 ms → about 20 ms a level on the large presets.
- **Slope emission** keeps each earlier slope piece's bounds instead of recomputing them for every new piece.

The local-check, interior and slope-bounds changes kept every level byte-identical (180 levels hashed before and
after); the overlap-free geometry changed levels, so it is generator version 17. What is left, per attempt on the dense preset: props and interior (~75 ms), surfaces and the
movement graph for validation (~50 ms), slope emission (~40 ms), the buildings' local checks (~25 ms). On the 50×50
presets about 3 seeds in 30 find no building mix that fits and spend all 16 attempts, but such an attempt stops in
under a millisecond. Path stretch (up to 500 ms) is measured only for the editor's report, never at match start.

A wider sweep (`--overlaps`, 300 seeds a preset) finds overlaps in 4 of 1794 levels: three sub-millimetre face
overlaps between neighbouring pieces of one slope (a shared corner computed two ways, 2e-4 apart) and a core flight
2.6e-4 m³ into a scaled floor's wall. None is visible; they are open items, not retried.

## Output: `LevelDescription`

These are the v1 types reworked. They are plain data, byte-stable to serialize.

- the `generatorVersion`, seed and config hash
- **plots:** id, parent id (−1 for top level), convex polygon, base height, slope plane (gradient, or zero), and the
  assigned targets
- **transitions:** plot A, plot B, segment endpoints, type, width
- **buildings:** id, plot id, footprint, first-floor height, floor count, entrance positions, spine polygon, roof
  reachable
- **primitives:** as in v1 (role, shape, position, size, yaw, steps, owner), plus:
  - new shape **Prism**: a convex polygon (stored in a shared polygon pool, referenced by index) extruded from
    `Position.y` to a top plane `h0 + g·xz`. This is used for plot ground, banks, floor slabs, roofs and porches.
  - new owner kinds: `Plot`, `Transition`, `Building`, `Prop`, `Boundary` (these replace `Room`, `Connector` and
    `Interior`)
  - new role `Railing` (a blocker, excluded from cover counts later)
  - **surfaces** (milestone 7): a `SurfaceTag` per face group, so the runtime can pick a material per face (see
    [Materials](#materials))
  - **variant** (milestone 8): for a prop, the index of the prefab variant the prop stage drew (see
    [Prop prefabs](#prop-prefabs))

The rasterizer fills a Prism by testing cell centres against the polygon and evaluating the top plane at the cell
centre. The mesher triangulates the convex polygon as a fan and builds the side faces. Colliders are mesh colliders
per owner, combined as in v1. Boxes, ramps and stairs already support yaw in both the rasterizer and the mesher, so
walls, railings, ramps and props need nothing new.

The generation report keeps v1's structure with the new metrics:

- a row per plot
- a building section (floors, entrances, spine kind, retries, drops)
- path stretch mean, p95 and worst pair
- constraint results, repairs, retries and timings

## Art: materials, prop prefabs and decals

The core stays free of art. It tags every face it makes with a **surface**, records which **variant** each prop
is, and a pure decal pass turns a finished description into decal strips. The runtime maps surfaces and decals to
materials from a theme, and prop variants to prefabs.

Two assets hold the art, split by whether they affect geometry:

- **`LevelTheme`** (ScriptableObject): level-wide materials, building material profiles, and decal materials and
  widths. It is visual only. Changing a theme never changes geometry, colliders or the description's hash.
- **Prop catalogue**, inside `LevelGenConfig`: a list of prefab variants per prop kind. A variant's footprint decides
  where the generator can place it, so the catalogue is geometry. It is part of the config and of the share code's
  config hash.

### Materials

Every slot holds one material. The only random choice is the building profile: the theme holds a list of profiles,
and one is drawn per building. Prefabs are the other place lists are used (see [Prop prefabs](#prop-prefabs)).

**Level slots** (`LevelTheme`):

| Slot | Faces |
|---|---|
| Ground | Plot tops, and sub-rooms that are neither mound nor dip |
| Ground: mound | Convex sub-rooms (see below) |
| Ground: dip | Concave sub-rooms (see below) |
| Ground: sinkhole | Sinkhole floors |
| Cliff face | Every vertical ground face: cliffs, ledges, blocked segments and sinkhole rims |
| Ramp | Ramp tops |
| Ramp side | Ramp sides and flares. Falls back to the cliff face slot when empty. |
| Bank | Bank tops |
| Boundary wall | The 50-tall boundary walls (the "walls" in the request) |
| Prop placeholder | Props whose variant has no prefab |

**Mounds and dips.** A sub-room is drawn as a fan of triangles from its site, and its corners take the mean of the
planes that meet there (stage 5). So a sub-room's shape is a shallow cone, and its **bulge** is its site's height minus
the mean height of its corners:

- A sub-room that a raise, tilt or raise-and-tilt operation picked is a **mound** when its bulge is above +0.05 (convex) and a
  **dip** when it is below −0.05 (concave). In between, it stays ground.
- A sinkhole is always a sinkhole, whatever else touched it.
- Every other sub-room is ground. That includes the neighbours an operation bends.

Material borders follow sub-room edges and are hard. Blending across borders (vertex-colour splatting) is a later
option.

**Building material profiles** (`LevelTheme`, a list of profiles; one is drawn per building):

| Slot | Faces |
|---|---|
| Floor | Tops of floor slabs inside |
| Ceiling | Undersides of floor and roof slabs, inside |
| Outer wall, outside | Outside faces of outer walls |
| Outer wall, inside | Inside faces of outer walls |
| Interior wall | Both faces of floor-plan walls |
| Slab edge | Exposed edges of floor slabs. Falls back to outer wall, outside. |
| Balcony | Balcony and porch slabs, all faces |
| Railing | Railings and roof parapets |
| Roof | Top of the roof slab |
| Stairs | Stair cores, exterior flights and porch steps |
| Plinth | A raised first floor's plinth |
| Stilts | Pillars under a building on stilts |
| Decals | The building's decal materials (see [Decals](#decals)) |

**How faces get their surface:** each primitive gets a small `Surfaces` record with a `SurfaceTag` for its top,
bottom, front, back and ends. Boxes use all five (front and back are the two broad faces of a wall, so a wall's inside
and outside differ). Prisms use top, bottom, and front for every side. The core knows which side of a wall faces in,
so it does the tagging. Building surfaces are generic tags (`BuildingFloor`, `BuildingCeiling`, …). The runtime
resolves them through the profile drawn for the primitive's owner building. The mesher splits each owner's mesh into
one submesh per material, as it does per role now.

**Profile draws** use their own stream, `Stream(seed, Art, ownerId)`, separate from
every generation stream. So editing a theme never moves geometry, and every peer draws the same materials.

### Prop prefabs

`LevelGenConfig` gets a prop catalogue: for each kind (tree, fence, car), a list of **variants**. A variant has:

- a prefab, with its pivot at the bottom centre and its facing along +Z
- an optional weight (default 1)
- its **footprint**, which is what the generator places and validates with:
  - a car: one box
  - a tree: a trunk box and a canopy box, with the canopy's bottom height
  - a fence: the piece length, height and thickness

The variant's inspector has a **Fit from prefab** button that reads the footprint from the prefab's colliders. It
warns when a collider reaches more than 0.1 past the declared boxes, because the validator would then not see it.

**Generation:** the prop stage draws the variant before it tries to place each tree or car, and one variant per fence
run, so a fence line looks the same along its length. It places by that variant's footprint, records the variant index
on the prop's primitives, and validates as now. A fence run is divided into a whole number of pieces, each stretched
along its length by up to ±25% so the run fits exactly. The stretch is recorded on the primitive.

**Building:** for a prop whose variant has a prefab, the builder instantiates the prefab at the primitive's
position, yaw and stretch, and uses the prefab's own colliders. It skips the placeholder mesh and collider. The
placeholder primitives stay in the description for the span grid and validation. A variant with no prefab is built as
today's placeholder with the prop placeholder material.

### Decals

All decals are **mesh strips**, not URP decal projectors. The project's renderer has no Decal renderer feature, and
strips follow angled walls and sloped ground exactly at the cost of one quad per segment.

A pure core function, `DecalStage.Build(description, decalSettings)`, makes every strip from a finished description,
so it needs no generator state. The builder runs it on every peer after building geometry. It isn't part of the
description or its hash.

A strip is a polyline of 3D points on a surface, plus that surface's normal and a width. Quads sit 0.01 off the surface
along its normal. Its texture coordinates are in world units, like the level's own: u is the distance along the strip and v the distance across it, so a square texture tiles once per 1 × 1 unit.
Strips on outdoor ground are cut every 0.5 and take the ground height at each point from the plot and terrain prisms,
so they follow mounds, dips and slopes. Strips stop at openings: doors, windows, entrances and stairs. Decal materials
are expected to use an alpha-blended or alpha-clipped shader with a depth offset. That is a theme authoring note, and
the generator doesn't enforce it.

| Decal | Lies on | Runs along | Material from |
|---|---|---|---|
| Ledge | The higher ground surface | The top edge of every ledge (Δ between `stepHeight` and `jumpHeight`): ledge transitions and sinkhole rims. A theme toggle adds cliff tops. | Theme |
| Door frame | Both faces of the wall | Both jambs and the head of every door | Building profile |
| Window frame | Both faces of the wall | All four sides of every window | Building profile |
| Wall-to-ceiling | Inside wall faces (outer walls' inside faces and both faces of interior walls), on every floor | The top of every wall, where it meets the ceiling above it | Building profile |
| Roofline | Outside faces of outer walls | The top of the outer walls, where they meet the roof slab | Building profile |
| Wall corner | Both wall faces at a corner | A vertical strip at every corner where two wall faces meet, inside and outside | Building profile |
| Floor skirting | Floor slabs | Along the foot of every wall inside, broken at doors | Building profile |
| Ground skirting | Outdoor ground | Around the building's ground outline (the porch or plinth outline, or each stilt), broken at entrance steps | Building profile |
| Tree base | Outdoor ground | A square of `treeBaseSize` under each tree, following the ground | Theme |
| Car surround | Outdoor ground | A rectangle `carMargin` outside each car's footprint, following the ground | Theme |
| Dip edge | The dip sub-room's ground | Its outline, just inside it, following the ground | Theme |
| Mound edge | The mound sub-room's ground | Its outline, just inside it, following the ground | Theme |

Each decal type has one material and a width in the theme. A building's decal materials come from its profile, so
one building's trims match.

### Blended ground

When a theme sets its `terrain` material (shader `Arena/Level Terrain`), the ground, the ramps and banks, and the
cliff faces are all drawn with it, and their layers blend into each other where they meet instead of being edged
with decals. There are nine layers:

| Layer | Where | Material |
|---|---|---|
| Ground, hill, dip | Ground tops, by height (below) | `ground`, `groundMound`, `groundDip` |
| Sinkhole | A sinkhole's floor | `groundSinkhole` |
| Rim | The higher ground round a sinkhole | `groundRim` (else ground) |
| Ramp, bank | Ramp and bank tops | `ramp`, `bank` (else ground) |
| Ramp side | Ramp and bank sides and flares | `rampSide` (else `cliffFace`) |
| Cliff | Cliff, ledge and slab-side faces, and the lip of ground along a ledge's or cliff's top | `cliffFace` |

Each layer takes its textures, tiling, colour and smoothness from its material. With blended ground the dip and
mound edge decals, the sinkhole rim decals and the ledge decals are left out.

- **Ground by height.** A pure core pass, `TerrainBlendStage.Build(description, blendSettings)`, colours each ground
  piece by its height above or below its plot's plane: hill from `hillHeightThreshold − hillBlendWidth` up to
  `hillHeightThreshold`, valley likewise below. Pieces are cut at those heights, so every weight is linear in its
  triangle and neighbours agree.
- **Rims and lips.** Round each sinkhole, the ground fades into the rim over `rimBlendWidth`. Along the top edge of
  every ledge, cliff and blocked transition, the higher plot's ground fades into the cliff's layer over
  `cliffLipWidth`, so the cliff's material wraps over its edge. A lip fades round its ends, and tapers to nothing
  along an end that meets no other lip (a slope's face, a flat crossing), so the corner it shares there is plain
  ground. A triangle has three layers: ground, hill or dip, and its edge's (rim, else lip).
- **Slopes.** A ramp's or bank's top is its own layer, fading to ground within `slopeEdgeBlendWidth` of its top edge
  and its foot, and to the side layer within it of each flared side; ground wins near the top and foot, so the
  slope's corners match the ground there. A flare fades from the side layer at the slope's edge to ground at its foot,
  across its width. The ground round a slope is pinned to its plot's plane (stage 5), so it is plain ground where the
  slope meets it, and nothing shows a seam.
- **Mesh.** Every face of the blended layers goes to one submesh. Each triangle has vertices of its own, its three
  layers on all three (UV3), and each vertex's weights over them as its colour. Where the ground was carved by a
  ramp or bank, a piece's corners on the cut carry the weights at that point.
- **Shader.** Every layer's albedo and normal map are drawn into two `Texture2DArray`s, one slice per layer, at the
  largest texture's size (capped at 2048). This happens on the GPU when the level is built, and the arrays are kept
  until a layer's textures change. A pixel samples one slice where the three corners agree, and three otherwise. It
  mixes them by weight, sharpened by each layer's brightness (`Blend contrast`, `Height influence`), so the edge
  follows the texture. Lighting is Blinn-Phong, like Simple Lit; the renderer is forward, so there is no GBuffer pass.

## Sharing and networking

The host picks a seed, and **every peer generates the level itself**. The description is sent only as a fallback,
when a client's level doesn't match the host's. This replaces v1's plan of sending the description to every client.

1. **Host options.** `HostOptionsView` gets a Level section next to the bot count, stored the same way in
   `HostOptionsManager`:
   - a seed field, random by default, with a randomize button
   - a config preset and a theme, chosen from a `LevelArtRegistry` asset that lists the presets and themes in the
     build
   - a share-code field that fills all three when pasted
2. **Server start.** The server generates from `(seed, config)` and builds. The host is also a client, so it builds
   once. If no valid level comes out within the retry budget, the server moves on to the next seed and tells the host
   which seed it used.
3. **The level ticket.** A `LevelSync` NetworkBehaviour in the game scene holds a `LevelTicket`:
   - the `generatorVersion`
   - the seed
   - the compact config (the share code's payload, so clients don't depend on having the same preset asset)
   - the theme's index in the registry
   - the description's hash (`StableHash` over its serialized bytes)

   Every client gets the ticket when it connects, late joiners included.
4. **Client generation.** A client with a different `generatorVersion` is disconnected with a message saying so.
   Otherwise it generates from the ticket while `LoadingView` shows, hashes the result and compares:
   - **Match:** it builds and tells the server it is ready.
   - **Mismatch:** it asks the server for the description. The server sends the serialized description in chunks
     (broadcasts of at most 16 KB each). The client builds from that and logs a warning with its platform. A mismatch
     is a determinism bug to fix, but it never blocks a player.
5. **Spawning.** Players spawn only after their client reports ready. Bots are server-side and need no ticket.
6. **Every match gets a new seed.** The host's chosen seed (or pasted share code) is used for the first match. Each
   later match draws a fresh random seed with the same config and theme. The server generates during the post-game
   countdown and issues a new ticket. Every client tears its level down and repeats step 4, so the next level is ready
   when the countdown ends. `levelgen.regen` does the same mid-match.

Why the hash and the fallback are worth having: the basic float operations are deterministic everywhere, but
`Mathf.Sin`, `Cos`, `Pow` and `Atan2` can differ in the last bit between Mono, IL2CPP and WebAssembly. One bit can flip
a comparison and change the level. The fallback turns that into a short extra download instead of a desync. If it
fires often in practice, the fix is deterministic replacements for those functions inside the core.

## Tooling

This is v1's tooling with these changes:

- **Step through:** partition → heights and transitions → buildings → carving → boundary → props → final.
- **Overlays:**
  - plot polygons by id, with terrain sub-rooms outlined
  - transitions coloured by type, and one-way edges shown with arrows
  - building spines and entrances
  - the path stretch heatmap (each sample coloured by its mean stretch) and the worst-pair path
  - walkable surfaces by height and the movement graph, both kept from v1
  - removed: the openness and symmetry overlays
- **Metrics report:** per plot and level rows for the v2 metrics, plus the building section.
- **Theme slot:** the window takes a `LevelTheme`, and the preview builds with its materials, prefabs and decals.
  Swapping the theme rebuilds without regenerating. A **Decals** toggle hides the strips.
- The batch runner and calibration sweep (v1's evaluation tools) were dropped on 2026-10-22.

## Tests (EditMode)

- **Determinism:** byte-identical descriptions for the same seed and config.
- **Path stretch fixtures:**
  - An empty flat level scores 1.0.
  - A wall across the middle with one gap scores the known detour.
  - A cliff segment (one-way) gives asymmetric distances.
  - A building with a spine adds less than a building of the same size without one.
- **Geometry fixtures:**
  - A Prism rasterizes to the right cells and heights, including a sloped top.
  - A 30° bank is walkable everywhere along it, and a 35° one isn't.
- **Partition:** plots and cut cells tile the padded box with no gaps or overlaps (every cell of the level is in
  exactly one), plots lie wholly inside the level and are convex and at least `minPlotSide` wide, no outline edge
  runs along a side of the level, and the plots stay joined.
- **Buildings:**
  - Every building's entrances are joined on the ground floor.
  - Every floor, balcony and reachable roof is in R.
  - A window counts as a jump edge.
- **Validator:** v1's fixtures, ported to plots.
- **Art:**
  - Every face the generator makes has a surface tag (none is left at the default).
  - Changing the theme leaves the description's hash unchanged.
  - Building profile picks are the same for the same seed.
  - A prop placed with a variant's footprint passes validation, and a fence run's pieces fill it exactly.
- **Decals:**
  - Every strip lies within 0.02 of its surface, including on sloped and terrain ground.
  - Strips stop at doors, windows and entrances.
  - Each of the 12 types appears on a fixture made for it.
- **Networking:** a ticket round-trips through serialization, and regenerating from a ticket gives the ticket's hash.
- **Guarantee:** batches of seeds for the default settings and the harder presets all pass every hard constraint:
  - tall (3-floor buildings, high elevation range)
  - dense (8 plots, many props)
  - small (50 × 50 with full-plot buildings)

## Code layout

This is the same assemblies as v1 under `Assets/Scripts/LevelGen/`.

### What carries over from v1

| Keep (possibly extended) | Remove |
|---|---|
| `Pcg32`, `SeedTree`, `StableHash` | `SkeletonStage`, `FootprintStage`, `Layout`, `LayoutSizes` |
| `SpanGrid`, `Rasterizer` (+ Prism) | `ConnectorBuilder`, `ConnectorPlan`, `ElevationStage` |
| `SurfaceSet`, `WalkRegions`, `MovementGraph` (+ 8-neighbour Dijkstra) | `InteriorStage`, `RoomProbe` |
| `Validator` (constraints reworded) | Openness and symmetry functions in `Metrics` |
| Elevation functions in `Metrics` | `ConnectorMap` (replaced by a transition map) |
| `TargetAllocation`, `CellMask`, `GenerationResult`, `GenerationReport` | Tests for the removed stages |
| `LevelBuilder`, `PrimitiveMesher` (+ Prism) | |
| `LevelGenConfig`, `SettingsCheck`, window, overlays (reworked) | |

New core code under `Core/Generation/`: `Polygon` (convex clip, inset, chord split, point-in-polygon, area),
`PartitionStage`, `TransitionStage`, `BuildingStage` (with `BuildingPlan` for footprint, floors, spine and access),
`TerrainStage`, `PropStage`. Under `Core/Measurement/`: `PathStretch`, `PlotMap`, `TransitionMap`.

Art and networking add:

- **Core:**
  - `SurfaceTag` and the primitive's `Surfaces` record, with serialization
  - prop variant footprints in the settings
  - `DecalStage` with its strip data
  - `LevelTicket`, with serialization and the share-code payload
- **Runtime:**
  - `LevelTheme`, `BuildingMaterialProfile`, `PropVariant` and `LevelArtRegistry`
  - material and prefab resolution in `LevelBuilder`
  - the decal strip mesher
  - `LevelSync`, the NetworkBehaviour holding the ticket, readiness and the chunked fallback
- **Editor:** the prop variant inspector (Fit from prefab, collider check), and the window's theme slot
- **UI:** the Level section in `HostOptionsView`

## Round 2: buildings, fences and cars (milestones 11–13)

Requested 2026-10-27. These rules replace the matching parts of stages 4 and 7. Every new solid follows
[No overlapping geometry](#no-overlapping-geometry). They change the description for a given seed, so
`generatorVersion` is bumped.

### Building mix (replaces `buildingCount`)

- At the start of stage 4, draw one **mix**, with equal weights: **1 big + 2 small**, **2 big**, or **4 small**.
- A plot is eligible for a class if that class's smallest footprint fits it (after the slope clear-outs). A mix is
  only drawn if enough plots are eligible. Big buildings take the largest eligible plots first, then small buildings
  take theirs, at random among the eligible plots.
- A building that fails its local check is retried from its next sub-seeds as now. If it is still failing, it is
  **not dropped**: the level attempt fails and is retried.
- **Hard constraint 8:** the built buildings match the drawn mix.

### Big and small buildings

| | Big | Small |
|---|---|---|
| Floors | 2–3, cut to fit `maxHeight` (2 with the default 12) | exactly 1 |
| Floor height | `bigFloorHeight` 5 | `smallFloorHeight` 4 |
| Balcony | at least one, on an upper floor, reachable | none needed |
| Rooms per floor | 1 or more | 1 or more |
| Roof | reachable with `roofAccessOdds` | reachable only by an exterior flight, with `roofAccessOdds` |

- Big stair cores scale with the floor height. At 5 units that is 20 risers, two 5-unit flights, a core of about
  4.5 × 6.5.
- **Every building has a porch**, at least along each side that has an entrance. On a full-plot building the porch
  fills the inset as now. On a stilted building the porch is at first-floor level along the entrance sides, reached
  by its exterior flight.
  - The ground-level porch's slabs are kept in `BuildingPlan.Porches`. A stilted building's porch is a **first-floor
    balcony** (it is drawn and checked as one), so its `Porches` is empty.

### Rooms

A **room** is a convex piece of a floor between interior walls. An **edge** is one stretch of interior wall that two
rooms share (a chord that crosses another chord makes several edges).

- **Minimum size.** Every room is at least `minRoomArea` (12) in area and `minRoomWidth` (3) across. A chord that
  would make a smaller room is redrawn, and dropped after its tries.
- **Room elevation.** Each room has a `roomOffsetOdds` (0.5) chance of sitting **higher or lower** than its floor's
  level, by `minRoomOffset..maxRoomOffset` (0.5–1.5), in whole risers.
  - A first floor that sits on the ground only raises its rooms, never lowers them.
  - Rooms that hold an outside entrance or a stair-core landing, or that a balcony door or exterior flight lands in,
    keep offset 0.
  - An offset that would leave a room, or the room under it, with less than `playerHeight` of headroom, or a door in
    it shorter than `doorHeight`, shrinks by risers until it fits, down to 0.
  - A room's slab is its floor polygon at its own level. Where two rooms' levels differ, the higher slab's side is a
    riser face from the lower slab's top up to the higher's (stop short against both).
- **Edges.** Each edge is drawn as:
  - **wall** (`wallEdgeWeight` 33): an interior wall with a door-wide gap, as now;
  - **open** (`openEdgeWeight` 33): no wall at all;
  - **railing** (`railingEdgeWeight` 34): a railing along the edge with a door-wide gap in its middle.
  On the ground floor, an edge the spine crosses gets a spine-wide gap instead of a door-wide one (an open edge
  already has one).
- **Stairs between rooms.** Where two rooms' levels differ, a short flight `doorWidth` wide (spine-wide on the spine)
  fills the edge's gap, running into the lower room. On an open edge the flight is centred on the edge, and the rest
  of the edge is a ledge. A ledge or a railing over `jumpHeight` is one-way or blocked, and the flight keeps the two
  rooms joined both ways. The flight and its run-out are kept clear like a door.
- The local check (stage 4 step 10) and the spine check now run over rooms at their own levels, joined by their
  edges' gaps and flights.

### Inside: props and lights

- **Interior props** are placed per room at `interiorPropDensity` (1 per 10 units of floor area), against walls first,
  stacked on the room's slab. The placeholder catalogue (with prefab variants like the other props) is **table**
  1.6 × 0.8 × 0.75, **shelf** 2 × 0.5 × 2 (against a wall only, never under a window) and **crate** 1 × 1 × 1.
- They keep out of: the spine, every door, door gap and stair flight plus 1.5 of clear ground in front, the stair
  core, railing gaps, and windows' jump paths. A room keeps a walkable route between all its gaps.
- **Light fixtures** are props with no collider: one ceiling fixture per room (at the room's centroid, top flush with
  the slab above, so it stacks), one wall lamp beside each entrance on the outside, and one per porch side.
- The description lists every fixture as a **light** (`LevelDescription.Lights`): position, range, colour slot and
  priority. Priorities are a room light 20000 plus its room's area, an entrance lamp 10000, and a porch lamp 0, so
  room fixtures (largest rooms first) outrank entrances, which outrank porches.
- A room's ceiling light reaches its room's farthest corner (plus 0.5). Entrance and porch lamps have `lampRange` (6).
- The builder makes real point lights, with **no shadows**, for the `maxRealLights` (16, set on the theme) highest-priority
  fixtures in the level. The others only glow (an emissive material).

### Fences (replace the old fence rules)

Fences are still 2-unit pieces standing on the ground under each piece, butt-joined, cut at every keep-out zone,
and old fences still get their small gaps.

- **Around buildings.** With `buildingFenceOdds` (0.75), a building on an inscribed footprint gets a fence ring
  `buildingFenceMargin` (2) outside its porch, kept `fenceMargin` inside the plot. "Its porch" here is one convex
  outline: the hull of every floor's footprint, its `Porches`, its slab balconies and landings, and its exterior flight.
  The same outline is the building's keep-out zone and what cars keep `spineWidth` from. It has a gate `spineWidth + 2` wide
  on each entrance's outward line and a gap at the foot of each exterior flight. A full-plot building gets none: its
  porch fills the inset.
- **Enclosed rooms.** With `enclosedRoomOdds` (0.3), a room without a building fence is fenced on **all** its sides at
  `fenceMargin`, with the gaps the old rule leaves: a gate at every flat or ledge crossing and the whole of every ramp
  and bank. So it is closed except where it leads to other rooms. The old partial fences (`fencedRoomOdds`,
  `min..maxFencedSides`) still apply to the other rooms.
- **Around sinkholes.** With `sinkholeFenceOdds` (0.4), a sinkhole gets a fence ring `sinkholeFenceMargin` (0.75)
  outside its rim. It has `minSinkholeGaps..maxSinkholeGaps` (2–4) gaps, each `doorWidth` wide, centred at **evenly
  spaced** points along the ring's length from a random start. A ring that leaves the room or meets a keep-out zone
  is cut there.

### Cars along the boundary walls

- With `boundaryCarOdds` (0.5), each boundary-wall run (one outline edge) gets a **row of parked cars**, parallel to
  the wall, `carWallGap` (0.5) off its inner face, `minCarSpacing..maxCarSpacing` (1–3) apart. Each slot is skipped
  with `carSkipOdds` (0.25), so rows have holes.
- A car goes in a slot only if its footprint lies in the plot, the ground under its whole footprint is one level plane (so it stacks with no gap under it, and no sink into the ground), and it is
  clear of every keep-out zone, of fences, of sinkholes and their rings, and `spineWidth` clear of every building
  footprint and porch. A row stops at an obstacle and starts again after it. Rows end 1 unit short of the outline's
  corners.
- The band cars (`density`, `carWeight`) stay as they are.

### Work split

- **Milestone 11 (buildings):** the mix, big and small classes, porches, floor heights, room minimums, room offsets,
  edges and the stairs between rooms, hard constraint 8, local-check and spine-check changes, and tests.
- **Milestone 12 (fences and cars):** building fences, enclosed rooms, sinkhole rings, boundary car rows, and tests.
- **Milestone 13 (interior):** interior props, light fixtures, lights in the description and builder, the real-light
  cap, and tests. It needs milestone 11's rooms.

## Milestones

Each milestone ends with something visible in the editor window.

1. **Teardown and geometry:**
   - Remove the v1 stages and their metrics.
   - Add `Polygon`, the Prism primitive (description, serialization, rasterizer, mesher), the new owner kinds and
     roles, and the v2 settings and config.
   - The window builds a hand-written fixture of angled prisms.
   - Determinism and Prism fixture tests.
2. **Measurement:**
   - Path stretch with the 8-neighbour Dijkstra, the plot and transition maps, and the validator ported.
   - The report panel and the path stretch overlay.
   - Fixture tests.
3. **Plots:**
   - Allocation, partition, heights, slopes, transitions and the plot-graph check.
   - Boundary walls.
   - The plot and transition overlays.
4. **Buildings, shell and walk-through:**
   - Plot choice, footprints, floors and first-floor placement, entrances, spines, the access plan, stairs, slabs,
     outer walls, doors, roof.
   - The spine overlay.
5. **Buildings, detail:**
   - Floor sizes (each floor 10–50% smaller, or sometimes bigger, than the one below; same shape), terraces and
     overhangs.
   - Floor plans, balconies, railings, windows, exterior flights and roof access.
   - The building local check, retries and drops.
6. **Terrain and props:**
   - Voronoi sub-rooms with raise, tilt and sinkhole operations (stage 5).
   - The prop catalogue and placement with keep-out zones.
The original milestones 7 (improve and repair) and 8 (evaluation) were dropped on 2026-10-22. Validate-and-retry from
milestones 3–6 is the guarantee (see stage 8).

7. **Surfaces and materials:**
   - Surface tags in the description, serialization and mesher, with a submesh per material.
   - `LevelTheme` with the level slots, the terrain categories, and building profiles drawn per building.
   - The window's theme slot, and a placeholder theme asset with distinct colours per slot.
8. **Prop prefabs:**
   - Variants in the config, with Fit from prefab and the collider check.
   - Variant draws and footprints in the prop stage, and fence pieces fitted to runs.
   - Prefab instancing in the builder, and a sample catalogue of primitive-made prefabs.
9. **Decals:**
   - `DecalStage` with all 12 types.
   - Theme decal materials and widths, the strip mesher, and the window's decal toggle.
10. **Gameplay:**
    - The Level section in host options, `LevelTicket` and `LevelSync`, client generation with the hash check and the
      chunked fallback, and ready-gated spawning.
    - Generated spawn points, used by `MatchManager` (see the open items).
    - The dev HUD (seed, share code, metrics), the `levelgen.regen` and `levelgen.load` commands, and the
      `LevelGenSandbox` scene.
11. **Buildings, round 2:** see [Round 2](#round-2-buildings-fences-and-cars-milestones-1113).
12. **Fences and cars:** see the same section.
13. **Interior props and lights:** see the same section.

## Open items and assumptions

- **Defaults chosen in design (approved by the user):**
  - `plotCount` keeps v1's 4–8. On a 100 × 100 level that makes plots of about 35 units across, so carving does most
    of the local variety. A larger range (6–14) may suit open outdoor levels better, and that's worth checking in the
    first plot milestone.
  - `buildingCount` 2, `floors` 1–3, `floorHeight` 4 (v1 wall height).
  - Path stretch target 1.3.
  - Prop density and weights are left to milestone 8 tuning.
- **Stairs are long.** One riser per cell (as in v1, since rise per cell must be ≤ `stepHeight`) makes a 4-unit floor
  height an 8-unit run. Hence switchback cores. A 3.5 floor height would shorten runs to 7 units, at the cost of
  3.0 clear headroom.
- **Railings vs held jump:** see stage 4. The movement graph is conservative and play is more permissive. That's
  harmless, since falls land in R.
- **v1 measurement choices that still apply:**
  - Spans within 1e-3 merge.
  - A jump needs `playerHeight` headroom over the higher surface.
  - The ground slab may extend below y = 0.
  - Room metrics (now plot metrics) count only surfaces with a walkable role.
  - Elevation frequency pairs each surface with the closest-height walkable surface in the next column.
- **Spawn points are needed for milestone 10.** `MatchManager` spawns at hand-placed `spawnPoints` transforms, which
  can't fit a generated level. Minimal plan:
  - The core picks spawn points from the path stretch samples: outdoor, in R, and not under a canopy or overhang.
  - It spreads them by farthest-point sampling, 2 × the max player count.
  - It records them in the description.
  - `MatchManager` takes its spawn points from the built level when one exists.

  Chests are still out of scope.
- **Art and networking choices made in design (2026-10-22), to confirm:**
  - "Walls" in the material request is read as the boundary walls. Ledge faces share the cliff face slot.
  - Prop prefabs replace the placeholder colliders with the prefab's own. The declared footprint must contain
    them.
- **Corrections from the user (2026-10-22):**
  - Sub-rooms an operation picked (raise, tilt or raise-and-tilt) get the mound or dip material by their shape. The
    bulge rule in [Materials](#materials) is how "convex" and "concave" are measured, and that rule is the design's
    reading. Raised sub-rooms were first left out, then added after the M7 review: on seed 1 every visible mound and
    dip was a raise. Bent neighbours stay ground. There are mound-edge and dip-edge decals.
  - There are no horizontal wall-band decals. The floor and ground skirting decals cover what was meant.
  - The ceiling decal runs at the top of every inside wall on every floor (wall-to-ceiling). A roofline decal runs
    outside where the outer walls meet the roof.
  - Materials are single per slot. Only building profiles and prop prefabs are lists.
  - Every match gets a new seed.
- **Milestone 1 choices (2026-10-22, for review):**
  - Until the stages exist, `LevelGenerator.Generate` returns the hand-written `PrismFixture`, stamped with the seed
    and config hash. It has no validate/retry loop yet. The fixture always has 6 plots, so the plot-count constraint
    fails in the report for any other `plotCount`.
  - Prism encoding: `Primitive.Polygon` is a pool index, and the polygon is in level space. `Position.xz` is an anchor
    (the generator uses the centroid), `Size.y` is the top height at the anchor, and `Primitive.Slope` is the
    gradient. So the top is `Position.y + Size.y + Slope·(xz − anchor)`. Yaw, `Size.x` and `Size.z` are unused.
    `Polygon` and `Slope` are serialized only for Prisms.
  - Plot descriptions point at the same pool polygon as their ground prism. `BaseHeight` is the height at the
    centroid. The building description fields wait for milestone 4.
  - Point-in-polygon evaluates each edge with its endpoints in a fixed order. Polygons that share an edge then
    cover every cell centre exactly once, including centres that lie on the edge.
  - Banks overlay the lower plot's ground prism, and the span merge makes the slope. Cutting the strip out of the
    plot would leave non-convex ground when a segment covers only part of an edge.
  - `RoomMap` is renamed to `PlotMap` now (sub-plot folding comes in milestone 2). The validator checks constraint 1
    on plots only and constraint 2 on the description's transitions. Constraint numbers match this doc, with 3 kept
    for buildings. Overlap, stacking, loops, `CanSee` and the v1 step-through stage view were removed along with
    openness and symmetry. The step-through comes back with the stages.
  - Defaults the doc left open: `sizeSpread` 0.5, `regularity` 0.5, raised ground 0.3, full plot 0.3, roof access
    0.5, prop density 4 with weights 1/1/1, transition weights 1 except cliff 0.5, and path stretch tolerance 0.1.
    `floors` is `MinFloors`/`MaxFloors`, `windowWidth` is a min/max pair, and `interiorWalls` is `MaxInteriorWalls`.
    `minSharedEdge` is a field (default 4) that is raised to at least the door width. `rampMaxSlope` is clamped to
    `atan(stepHeight / cellSize)` (31° by default). `buildingCount` is clamped to `plotCount`.
  - `GenStage` keeps only `Allocation`, `Repair` and `LevelRetry`. Each new stage adds its own id.
  - The existing `Assets/Level Gen/LevelGenConfig.asset` still holds the v1 keys in its YAML until it is next saved.
    The new fields load with their defaults.
- **Milestone 2 choices (2026-10-22, for review):**
  - A diagonal step a → b is allowed when the 4-neighbour graph has both a → c₁ → b and a → c₂ → b through the two
    orthogonal neighbours, with any move kinds. That is how "both orthogonal neighbours traversable" is read here.
    The 8-neighbour Dijkstra is `MovementGraph.Distances`.
  - The sample lattice is offset by k/2 from the level's edge. A surface is outdoor when it is the top of a walkable
    primitive owned by the level, a plot or a transition (matched by height within 1e-3). A sample's heatmap value
    is the mean stretch of the pairs it is either end of. The 8-unit pair minimum is the Euclidean distance between
    cell centres. p95 is nearest-rank.
  - Pairs with no path are left out of the mean and counted in the report. Constraint 1 is what flags them.
  - `PlotMap` gives a surface to the deepest plot over its cell, whatever the height, because plots tile and
    sub-plots tile their parent. This replaces v1's height rule. Plot metrics are measured per top-level plot with
    its sub-plots folded in. Walk regions, and constraint 1's "largest walk region", are per plot, sub-plots
    included.
  - Constraint 1 now also requires every transition primitive's walkable surfaces to be in R (`TransitionMap`).
    Constraint 2 still reads the transition types from the description rather than measuring them.
  - The evaluator validates before measuring, because path stretch samples only surfaces in R.
  - Cost: path stretch takes about 1.5 s on the 70 × 70 fixture at k = 8 (289 samples, 67k pairs).
- **Milestone 3 choices (2026-10-22, for review):**
  - Exact tiling: each power-cell edge remembers its line (a side of the rectangle, or the bisector of two sites).
    Every vertex is then recomputed from its two lines in double precision, and two bisectors are replaced by the
    canonical pair for their three sites. Neighbours therefore share their vertices bit for bit, and the tests check
    that every cell centre falls in exactly one plot.
  - Sites are scattered by dart throwing at least 0.6 × the site spacing (√(W·D / n)) apart, inside the rectangle
    inset by `minPlotSide / 2`. The spacing shrinks by 10% after 200 misses in a row. Weights are
    `(allocation − 0.5) × 0.5 × spacing²`, so a bisector shifts by at most about a quarter spacing. Lloyd keeps the
    weights. A partition gets up to 32 draws of its own, separate from level retries.
  - Base heights are evenly spaced over `0 .. levelElevationRange × maxHeight` in `heightStep` multiples and then
    shuffled, so the level range is always met.
  - Slopes come from a constant `SlopeOdds` of 0.35 (not yet a config field). A sloped plot keeps its lowest corner
    at its base height. It rises by at most its elevation-range target, never steeper than `rampMaxSlope`.
    `PlotDescription.BaseHeight` stays the height at the centroid, so the level elevation range metric reads a
    little high when a plot is sloped.
  - Building plots are picked in stage 3 (from the building stream) so they can be kept flat. A plot qualifies when
    its polygon inset by `balconyDepth + spineWidth` is still at least `spineWidth` wide. Stage 4 builds on them.
  - Segments: 1 to `min(3, ⌊length / (doorWidth + 2)⌋)` per edge, cut at random points, each at least
    `doorWidth + 2` long. The natural type comes from the largest Δ along the segment. A cliff also needs the same
    side to be higher along the whole segment; otherwise the segment is blocked.
  - Ramps and banks fit only when the mean Δ is above `stepHeight` and the run fits, measured to the lower
    plot's farthest vertex, with `minPlotSide / 2` left over. The fitted top (see stage 3) is the least-squares
    plane through the four corner heights. Its run is set so the slope into the lower plot, combined with the slope
    along the face, equals `rampMaxSlope`. It fits when a quarter of the twist between the two grounds is at most
    `stepHeight / 2`. Ramps are Prisms now, not wedges. Blocked takes the cliff weight.
  - Plot-graph check: a plot fails when it has no two-way transition or is outside the strongly connected set of
    plot 0. A repair prefers a ramp (shortest run) and falls back to a bank. If no segment fits, the level attempt
    stops and the next level-retry sub-seed takes over (up to `retriesPerLevel × 4` = 16). The window's
    heights-and-transitions view shows a failed plan with its reason.
  - Base heights take the best of 6 shuffles: the one with the least height beyond `maxSafeDrop` between
    neighbours, since those edges can only be ramps, banks or blocked.
  - Outline (2026-10-22, from review): the diagram is drawn in a box padded by one site spacing, and only cells
    wholly inside the level become plots. This replaced an earlier `cutPlots` setting that cut cells on the
    rectangle's sides, which still left the rectangle's straight sides showing. The spacing solves
    `(W − 1.2·s)(D − 1.2·s) = (plotCount + 1.5)·s²` (each cell reaches about 0.6 spacings from its site; calibrated
    by sweeping). On average about one surplus cell is cut from the outline. Up to 24 random choices are tried per
    draw. Stage id 16 (`Cut`). Site labels pack two indices as `a·1024 + b`.
  - **Playable area:** dropping every cell that crosses the edge leaves 34–46% of the rectangle playable (default
    42%, small 34%, dense 46%, 100 × 50 36%). Plots are smaller than before (spacing about 18 units on the default
    70 × 70). `width` and `depth` are now the bounding box the outline must fit in. The settings check assumes
    about half is playable.
  - One slope per pair (2026-10-22, from review): ramps and banks both count, since a bank looks like a wide ramp.
  - Stage ids 11–15 (`Partition`, `PlotWeights`, `Heights`, `Transitions`, `Buildings`). The window steps through
    Partition, then Heights & transitions, then Final. Generate no longer shows the prism fixture, which stays for
    tests.
- **Milestone 4 choices (2026-10-22, for review):**
  - **Footprints:**
    - Convex only for now. The L-shaped footprint (two convex parts) is deferred, because walls, slabs and the
      spine all assume one convex polygon.
    - An inscribed footprint is a rectangle squared to one of the plot's edges (±20°), 85–100% of the largest that
      fits in the plot inset by `spineWidth`. It may have up to two opposite corners cut, giving 5 or 6 sides.
    - Both kinds keep clear of the plot's ramps and banks by their run plus the margin.
    - If no inscribed footprint fits, the building fills the plot instead. It is dropped if under
      `spineWidth + 2 × wall + 2` (5.6) wide.
  - **Placement:**
    - `raisedGroundOdds` lifts the first floor, and half of the lifted buildings are on stilts (the constant
      `StiltsShare`; it is not in the config).
    - A raised first floor sits 0.5–1.5 up in 0.25 steps, lowered until the steps at every door fit on the plot
      (or set back on the ground).
    - A ground-level first floor sits 0.1 above the plot, so it is the building's own surface and never a path
      stretch sample.
    - On stilts the first floor is one `floorHeight` up. The ground underneath is plot ground, so it does count as
      outdoor.
    - Floors are cut so the roof stays under `maxHeight`. With the default 12 and plot bases up to 3.6, three
      floors rarely fit.
  - **Entrances:**
    - At most one per footprint side; transitions on the same side share it. Each sits at the point nearest its
      transition, kept clear of the corners.
    - At least 2: when fewer face transitions, one is added on the side farthest from the first.
  - **Spine:** a spine-wide square lobby on the centroid, plus a spine-wide strip from each door to it.
  - **Stair core** (switchback):
    - Flights are `doorWidth` wide (the core is 4 wide), with one riser per cell: 8 + 8 risers of 0.25 for a 4-unit
      floor, 4 units of run each.
    - There is a 1.5 landing at the wall end and a clear 2-unit approach at the open end, so the core is about
      4 × 5.5.
    - It stands end-on or side-on against an outer wall. The core itself keeps off the spine; its approach may
      share it.
    - Flight B and every flight above the first float, so stacked levels keep a player's height under them. That
      is why `floorHeight` is clamped to at least `2 × playerHeight`. Floating parts get 0.01 of slack against float
      rounding.
    - The slabs above have the well cut out as up to 4 convex pieces. The well has no railing yet (milestone 5).
    - Under stilts the core stands 0.5 in from the wall, because the walls hang a floor up there.
  - **No core fits:** the building is cut to one floor (on stilts it goes back to the ground). Cores fit in about
    a third of the buildings that want upper floors (14 of 44 over 30 default seeds, 44 of 85 on the dense preset),
    because footprints on these plots are small. Milestone 5's exterior flights are the spec's other route up.
  - **Shell:**
    - Walls are 0.3 thick, centred on the footprint's edges and running half their thickness past the corners.
    - Doors are `doorWidth × doorHeight` on the first floor; buildings on stilts have no ground-level walls.
    - The roof is a Ceiling-role slab, unreachable until milestone 5.
  - **Thin blockers:** they rasterize at least `cellSize · max(|cos yaw|, |sin yaw|)` thick, so a 0.3 wall can't fall
    between cell centres and vanish from the grid. This narrows doors in the grid by up to half a cell.
  - **Measurement:**
    - `BuildingMap` finds each floor's largest walk region, and the surface just inside each door.
    - Constraint 1 also requires every floor's largest region to be in R, and blames a building for any sink
      inside its footprint.
    - Constraint 3 requires each building's entrances to be in R and joined by walking within a step of spine
      height under the footprint.
    - Plot elevation metrics now count only outdoor surfaces, so floors and roofs don't shape the terrain metrics.
  - **Dropping buildings:** the generator drops any building a failed constraint blames and validates again. This
    is a simple form of the stage 4 local check, and the drop is recorded in the trace. No sweep dropped a building
    this way.
- **Milestone 5 choices (2026-10-22, for review):**
  - **Floor sizes:**
    - Floors scale about the point on the core's wall nearest the core's centre (or the first floor's centroid when
      there is no core). That wall's line stays put, so the core keeps its wall and a floor shrinks or grows away
      from it. Scaling about the core's centre left no room for the core, and almost no floor could resize.
    - A shrinking floor's terrace is therefore zero-width along the core's wall and widens away from it.
    - Each level's slab is the larger of the two floors it separates. A terrace's railing is a parapet standing on
      the wall below.
    - Over 30 dense seeds: 20 floors shrank, 7 grew, and 12 kept the size below because no resize fit.
  - **Floor plans:**
    - Interior walls are chords that cross no other chord, so each one's gap joins the two pieces it separates.
      That makes every piece reachable without a separate piece-graph check.
    - Chords keep off the core and its approach, and away from doors.
  - **Doors and windows:**
    - Balcony and terrace doors sit mid-side, shifted up to 3 to clear the corners, other openings, interior walls
      and the core.
    - Windows fill each side's free wall, `windowWidth` wide, 1–2.5 apart. They have no collider, so the sill is a
      surface you can jump onto and through.
  - **Balconies:**
    - An elevated floor opens onto 1–3 sides. Where the floor is smaller than the one below and the terrace is at
      least 1.8 deep, the door opens onto the terrace.
    - Otherwise it gets a slab balcony 3–6 wide (`balconyDepth` deep), railed on its open edges, if it fits on the
      plot clear of slopes.
    - The ground-floor porch is plain ground (no slab).
    - There is no railing round the stair well: falling in is a drop of at most a floor onto the flights.
  - **Exterior flights:**
    - Used only when no core fits, and they climb one level only (to the second floor, or the first under stilts).
      Such a building has at most two floors, and its second floor keeps the first's size so the landing meets its
      wall.
    - The flight is solid from the ground with no railing; falling off its open side is a drop to the ground.
    - These flights are long (8.5–11 units at 0.25 risers), so few fit: 2 in 30 dense seeds.
  - **Roofs:** reached only through a core (one more switchback), and only when the parapet stays under `maxHeight`.
  - **Stacked landings:** above the first, landings are slabs rather than solid blocks. A solid block left 1.75 of
    headroom where the landing meets the next flight.
  - **Local check:**
    - Each building is validated alone on its flat plot: walk-through, every floor, balcony and reachable roof in R,
      no sinks, headroom.
    - A failure retries the building from its next sub-seed, up to `retriesPerPlot` (4) more times, and then drops
      it. The level-wide drop stays as a backstop.
    - Retries are rare (4–6 per 30 seeds). The remaining failures are mostly cells in the narrow end of a terrace
      that can be dropped into but not left, and the retry clears them.
  - **Walk regions:** a plot's walk regions now cover its outdoor ground only. Before this, the largest region could
    be a building floor and become the reference for R.
- **Milestone 6 choices (2026-10-22, revised after review):**
  - **Terrain replaced carve lines.** The first version cut plots with 0–3 straight chords into sub-plots at offsets.
    Review found most of them flat, because the evenly spread offsets always gave one piece offset 0, and found
    straight cuts dull. Stage 5 is now the Voronoi terrain above. The user chose:
    - continuous ground (shared corners averaged) over small steps;
    - pinned room edges, so terrain runs after buildings as the pipeline orders and transitions are unchanged;
    - explicit terrain config knobs; the elevation targets are now only measured;
    - sinkholes just under a jump deep.
  - **Removed with carving:** sub-plots, the flatten repair and the sub-plot ramp leftover. The partition's
    power-diagram code now takes any convex outline: a room for terrain, the padded box for the partition.
  - **Defaults:** about 35 units² per sub-room (6–10 in a typical room) and 2–5 operations per room. A raise moves
    0.25–1, a tilt adds 3–10°. The steepest triangle is 20°, kept below the 30° ramp limit so diagonal grid steps stay
    well walkable.
  - **Sinkholes** need a sub-room that doesn't touch the room's edge. An operation picking a pinned or edge sub-room
    for a sinkhole becomes "nothing".
  - **Speed:** about 1,150 sub-rooms over 30 default levels. Generation averages about 65 ms per level, and attempts
    are back to 30–32 per 30 seeds: the terrain has no ledges except sinkholes, so it makes no pockets.
  - **Props:**
    - A fence is 0.1 × 1.2 tall and 2–6 long. A car is 4.5 × 1.9 × 1.5. A tree is a 0.6 trunk, 3 tall, plus a
      3 × 3 × 2 canopy from 3 to 5.
    - Props stand in the 1–3 band (centred so their edge is in the band), `density` per 100 units of each room's
      perimeter.
    - They sit on the lowest ground under them, sampled on a 5 × 5 grid over the footprint, because terrain peaks
      between corners. Fences and cars need ground level to 0.3. A tree's canopy keeps a player's height over the
      ground under it. Nothing may top `maxHeight`.
    - Fences (revised after review) line room sides instead; see stage 7. A ramp's or bank's keep-out now covers
      where it arrives on the higher room too (spanning the ramp's width plus 2, or the bank's whole segment, 3 deep).
    - **Keep-out zones:**
      - every ramp and bank plus a 2-unit run-out;
      - a 2-unit strip each side of flat and ledge segments;
      - a building's largest floor outset by `balconyDepth`;
      - a spine-wide, 6-long zone out of each entrance;
      - exterior flights and their landings, outset by 1.
    - Props keep 0.5 clear of the zones and of each other.
  - **Prop fallback:** when the level fails validation and no building is to blame, the props within 4 units of a
    failing surface are removed. A headroom failure names no surfaces, so it removes the trees, the only props that
    overhang. If neither applies, all props go. (The more selective stage 8 repair was dropped with milestone 7.) Over 30 seeds this fired
    1–3 times per preset.
  - **Results:**
    - Over 30 seeds every preset (default, small, dense, tall) succeeds in 30–32 attempts. The tall preset keeps its
      targets in absolute units (a 7-unit level range at `maxHeight` 20).
    - Path stretch is 1.05–1.10, against the 1.3 target: the terrain adds no detours, so props and buildings are the
      levers.
- **Prettier slopes (2026-10-22, from review):**
  - The sag and crest curves and the flared sides are as above (stage 3).
  - Flares are steeper than walkable (40°). Their 0.42 rise per cell is a jump, so they add no pockets.
  - A flare's steepest direction combines its sideways fall with the band's grade along the ramp (up to about 46°).
  - Runs are a third longer at the default `curveFraction`, but ramps and banks still fit as often: 30 of 30 seeds
    first time on default.
- **Milestone 7 choices (2026-10-22, for review):**
  - `SurfaceTag` has 10 level tags and 12 building tags, plus `None` for hand-made test geometry only. Every
    primitive carries a `Surfaces` record (top, bottom, front, back, ends), serialized as 5 bytes. `GeneratorVersion`
    is 7.
  - Box faces: front is local +X and back is local −X; ends are ±Z. Walls run along local Z, so for an outer wall
    on a counter-clockwise side, front is the outside face and back is the inside face. Ramps and stairs use the same
    axes, with the sloped face or the treads as their top.
  - Faces nobody sees still get a tag: ground bottoms take the cliff face and slab bottoms take the ceiling.
  - Tags per stage:
    - Plot and terrain ground: the ground category on top, cliff face on the sides and bottom.
    - Ramp and bank bands: ramp or bank on top, ramp side on the sides. Flares are ramp side on every face.
    - Boundary walls: boundary wall. Props: prop placeholder (until milestone 8).
    - Floor slabs: floor on top, ceiling below, slab edge on the sides. The roof slab has roof on top. The
      first-floor slab of a raised building is plinth on its sides and bottom.
    - Outer walls: outside on the front, top, bottom and ends; inside on the back. So door and window jambs take
      the outside material.
    - Interior walls: interior wall. Railings and parapets: railing. Slab balconies and the exterior flight's
      landing: balcony.
    - Flights, porch steps and core landings: stairs. Pillars: stilts.
  - Mounds and dips: `SubRoom.Picked` is set when a raise, tilt or raise-and-tilt operation sticks (one that is undone
    doesn't set it). `TerrainPlan.SurfaceOf(cell)` applies the bulge rule with ±0.05 against the final triangles.
  - The builder resolves a face like this:
    - a level tag through the theme;
    - a building tag through the profile drawn for the owner building, using `ArtDraws.ProfileIndex`, which draws
      from `Stream(description.Seed, Art, buildingId)`. `GenStage.Art` is 20.
    - An empty slot, or no theme at all, falls back to a built-in placeholder colour per tag.
    - Fallbacks: ramp side uses the cliff face, and slab edge uses outer wall, outside.
    - Each owner mesh gets one submesh per resolved material, in order of first appearance. The vertices are
      emitted in the same order whatever the theme, so collider meshes match exactly across themes.
  - The profile's Decals slot waits for milestone 9.
  - The window has a Theme field. Changing it rebuilds the last description without regenerating. Editing the
    theme asset's materials shows up live. Editing its profile list needs Generate, or re-picking the theme.
  - `Assets/Level Gen/Placeholder Theme/PlaceholderTheme.asset` has a material per level slot and 3 profiles:
    Brick, Stucco and Concrete. Each profile has 12 materials.
- **Milestone 8 choices (2026-10-22, for review):**
  - Footprints are a core `PropShape` per variant: weight, then length (Z), width (X) and height for a car, a fence
    piece or a trunk, plus the canopy's length, width, bottom and height for a tree.
  - The config's `PropCatalogue` pairs each shape with a prefab. `LevelGenConfig.ToSettings` copies the shapes into
    `PropSettings` (fences, cars and trees, non-serialized there), and `WriteTo` adds them to the config hash. A
    prefab never enters the hash.
  - A kind with no variants gets one default variant, the old placeholder size. So a config without a catalogue
    hashes as before; its levels still change, because variant draws take random numbers.
  - The generator draws a variant from the same stream before each car or tree try, and one per fence run.
    `GeneratorVersion` is 8.
  - Each footprint is a box centred on the pivot. Fit from prefab takes the smallest centred box round the
    colliders, so a prefab that is off-centre gets a bigger footprint rather than a shifted one.
  - Tree fitting: colliders reaching within 0.1 of the ground are the trunk, and the rest are the canopy.
  - The tree headroom check now uses the canopy's bottom minus the player's height (0.9 with the defaults, as
    before).
  - Fence runs:
    - A run is found before its variant is drawn, so it keeps clear of keep-outs by the thickest fence variant.
    - A run takes `round(run / piece)` pieces, each stretched by up to ±25% (`PropStage.MaxFenceStretch`).
    - When the clamp leaves the pieces short of the run, they are centred in it. A run shorter than 0.75 of a piece
      stays open.
  - The description gets a `Props` list: id, kind, variant, pivot position, yaw and stretch per prop. Each record
    owns its placeholder primitives through `(Prop, id)`. This keeps the variant off every primitive and gives the
    builder the prop's kind, which the primitives don't carry.
  - The pivot's height is the prop's foot: the lowest ground under a car or fence piece, and the ground at a tree's
    trunk.
    - On uneven ground the placeholder rises to stand a fence's height over the highest point, but the prefab
      doesn't. So a fence on a slope can be lower in play than the validator sees it.
    - A lower fence only makes movement easier, so no-one-gets-stuck still holds.
  - The builder takes the catalogue (`LevelBuilder.Build(..., theme, props)`). A prop whose variant has a prefab is
    an instance named `Prop {id} ({prefab})`, with the prefab's colliders and Z scaled by its stretch. Its
    placeholder group isn't built.
  - `Teardown` now destroys only the meshes the builder made (`DontSave`), not prefab meshes.
  - The config inspector lists each variant with Fit from prefab, and warns under it when a collider reaches more
    than 0.1 past the footprint.
  - Sample prefabs are in `Assets/Level Gen/Sample Props`, built from Unity primitives: Car Sedan, Car Van, Tree
    Round, Tree Pine, Fence Picket (2 long) and Fence Low Wall (3 long). `Assets/Level Gen/LevelGenConfig.asset`'s
    catalogue uses all six, with fitted footprints.
- **Milestone 9 choices (2026-10-23, for review):**
  - **New in the description.** The decal pass reads only the description, which until now didn't carry openings,
    floor-plan walls or sub-rooms. It now holds:
    - per building, `Openings` (floor, side, T, half width, bottom, top, door) and `Walls` (floor, from, to, gaps, gap
      top);
    - level-wide, `SubRooms` (plot, outline, surface tag).
    These are serialized, so the hash changes but the geometry doesn't. `GeneratorVersion` is 9.
  - **Strips.** A `DecalStrip` has a type, a building (or −1), a normal, a width, and three point lists: its two
    edges `Left` and `Right`, and the middle line `Points`. It also has a v range.
    - The mesher lifts the edges by 0.01 along the normal. u is the distance along the middle line over the width.
    - Bands round corners are mitred. Where a band breaks (at a door, say), it is cut square across.
  - **Ground.** The ground under a point is the highest top of the Plot- and Transition-owned prisms there; ramps,
    banks and flares count. A strip breaks where no ground prism covers a point.
    - Ground strips are cut every 0.5.
    - Each edge point is sampled 0.02 into the strip. So a ledge or rim edge reads the higher side, and a dip or
      mound edge reads the sub-room's own side.
  - **Per type:**
    - Ledge: the top edge of every `Ledge` transition (and `Cliff` with `cliffTopLedges`), on the higher side, and
      round every sinkhole sub-room's outline, outside it.
    - Dip and mound edges: just inside the sub-room's outline. They are no wider than a fifth of the sub-room's
      narrowest width.
    - Tree base: the square is drawn as parallel bands no wider than 0.5, so it follows the ground across as well as
      along.
    - Car surround: a loop starting `carMargin` outside the car's placeholder box and running outward by the decal's
      width.
    - Door and window frames: on both faces of outer walls (±0.15 from the footprint edge) and of interior walls
      (±0.1). Each frame is a jamb either side and a head, plus a sill for a window. A frame part that would run past
      an interior wall's end is left out.
    - Wall-to-ceiling: under the slab above (floor top minus 0.5), on outer walls' inside faces and both faces of
      interior walls. It breaks where an opening reaches into it, which the defaults never do.
    - Roofline: the top floor's outside faces, below the roof slab's top.
    - Wall corners: a half-width strip on each face at every outer corner, inside and outside, per floor. Interior
      walls get the same where they meet the outer walls' inside faces.
    - Floor skirting: along the inside faces and both faces of interior walls. It breaks at doors, interior doorways
      and the stair core's rectangle.
    - Ground skirting: from the outer walls' outside faces outward. It breaks at ground-floor doors (ground
      placement) and at stairs standing on the ground, which covers porch steps and exterior flights. On stilts it
      runs round each pillar. It doesn't run round the exterior flight's landing ("porch" in the table), because the
      description can't tell a landing from a slab balcony.
  - **Theme.** The level decal materials (ledge, tree base, car surround, dip edge, mound edge) are on the theme,
    along with `cliffTopLedges`, `treeBaseSize` (1.6), `carMargin` (0.3) and a width per type. Each building profile
    has the seven building decal materials.
  - **Building.** `LevelBuilder.Build` takes `decals` (default true) and builds one "Decals" object: a submesh per
    material, no collider, no shadows.
    - With a theme, a decal whose material slot is empty is left out.
    - With no theme, every decal gets a placeholder colour.
  - **Window.** The Decals toggle rebuilds without regenerating. Decals show only in the Final stage view.
  - **Placeholder theme.** It now has semi-transparent URP Lit decal materials for all 12 types, in
    `Assets/Level Gen/Placeholder Theme/Decals`.
  - The decal pass takes about 4 ms on the default config.
- **Milestone 10 choices (2026-10-23, part A for review; the user split it in two):**
  - **Split.** Part A is the core, sync and spawning. Part B is the host options' Level section, the
    regenerate/load controls and the sandbox scene. The user chose:
    - to remove the hand-built arena from `Online.unity`;
    - DebugView buttons instead of a dev console for `levelgen.regen` and `levelgen.load` (part B).
  - **Spawn points** (`SpawnStage`) come from the validation's measurement, not the path stretch samples, so they
    cost nothing extra. A candidate:
    - is ground (a Floor surface) in an outdoor walk region, in R and not a sink, under open sky;
    - has ground of the same region within a step, also under open sky, for 2 cells on every side.
    The points are spread by farthest-point sampling from the candidate nearest the level's middle, and face the
    middle. A new setting, `spawnPoints` (default 16, twice an 8-player match), is in the config hash. The points go
    into the description, and `GeneratorVersion` is 10.
  - **Bytes both ways.** `LevelGenSettings.ReadFrom` mirrors `WriteTo` field for field, and
    `LevelDescription.Deserialize` mirrors `Serialize`. Tests check both round-trip byte for byte.
  - **Share code:** `L` + base64url(deflate(version, seed, config)). About 300 characters with a prop catalogue. A
    code from another generator version is refused, and the error says why.
  - **`LevelTicket`** holds the version, seed, config bytes, preset index, theme index, hash and a match counter.
    - It travels as a `SyncVar<byte[]>` on `LevelSync`, so FishNet needs no codegen for core types.
    - The preset index is there only for the prefabs, since the geometry comes from the config bytes. A client
      missing that preset builds placeholders.
  - **`LevelSync`** sits on the Match Manager prefab, beside `MatchManager`. Its `generateLevels` is off in the
    prefab and on in `Online.unity`, so `Debug.unity` keeps its own arena.
    - **Server:** it reads `HostOptionsData` (random or fixed seed, preset, theme, share code). If a seed gives no
      valid level, it tries the next seeds, up to 8, and logs the seed it used. It builds the level into the Online
      scene, then publishes the ticket.
    - **Client:** a client with another generator version logs why and disconnects. Otherwise it generates, compares
      hashes, then either builds and reports ready, or asks for the description.
    - **Fallback:** the description comes back as `TargetRpc` chunks of at most 16 KB (RPCs rather than broadcasts),
      is checked against the hash, deserialized and built. The client logs a warning naming its platform.
    - **Host:** builds once, on its server side, and reports ready from its client side.
  - **`MatchManager`:**
    - It uses the generated spawn points round-robin when a level is built. They come in farthest-point order, so
      consecutive spawns are far apart. Pawns start 0.05 above the ground. Without a level it falls back to the
      assigned transforms.
    - A human whose client hasn't reported ready for the current level isn't spawned; the respawn tick tries again
      every 0.5 s. Bots never wait.
    - `ServerEndMatch` asks `LevelSync` for a new random seed with the same config and art, so the next level is
      generated and synced during the postgame countdown.
  - **`LevelArtRegistry`** (`Assets/Level Gen/LevelArtRegistry.asset`) lists the presets (the sample config) and
    themes (the placeholder theme).
  - **`HostOptionsData`** has the level fields: `RandomLevelSeed`, `LevelSeed`, `LevelPreset`, `LevelTheme` and
    `LevelShareCode`. Part B adds their UI.
  - **`Online.unity`:** the inactive v1 "Level Generator" arena and the ProBuilder "Stage" (with its
    `NavMeshSurface`) are removed. No script uses NavMesh. The Hurtbox kill floor 50 units down, the lights and
    the camera stay.
  - **Debug flags on `LevelSync`:**
    - `verifyOnHost` puts the host's client through the full client path.
    - `forceDescriptionFallback` treats every hash as a mismatch.
    Both were used to test the hash check and the chunked fallback in the editor's host session.
- **Milestone 10 part B choices (2026-10-23, for review):**
  - **Host options, Level section** (built into `UIManager.prefab` beside Bots, in the existing styles):
    - The seed field gets a fresh random seed each time the view opens, until the host types one or presses
      Randomize.
    - The config and theme dropdowns list the registry's presets and themes.
    - The share-code field always shows the code for the current seed and config, so the host can copy it.
      Pasting another code takes over the seed and config, and greys out the config dropdown until the seed or
      config is changed again. Text that isn't a valid code shows why under the field.
    - The spec says a pasted code "fills all three", but a share code carries no theme (it's
      `(version, seed, config)`), so the theme stays the host's pick.
  - **DebugView, Level panel.** It is the dev HUD: level number, seed, generator version, hash, counts, metrics and
    the share code.
    - The metrics are path stretch mean, p95 and worst, total error, and the constraints. They are measured once per
      level, at stride 8.
    - The share code is shortened, with a Copy button for the whole of it.
    - The commands are buttons: **Regenerate** is `levelgen.regen` and **Load** (with a paste field) is
      `levelgen.load`. Both are host-only and disabled on clients.
  - **Mid-match regeneration.** When a level is issued during a match, `MatchManager` despawns every pawn and
    respawns them on the new level, humans once their client reports ready. The end-of-match regeneration now runs
    after the state switches to the postgame countdown, so it doesn't trigger this.
  - **`LevelSync.ServerLoad(code)`** takes a share code's seed and config with the current art.
  - **`LevelGenSandbox.unity`** is a copy of `Debug.unity` without the arena, the weapons, the chests, the pickups,
    the capsules or the hand-placed pawn. Its `LevelSync` generates and its `MatchManager` skips to the match.
    - The dev flow loads its online scene through the Debug NetworkManager's `DefaultScene` (`Debug.unity`). So
      **Arena ▸ Level Gen ▸ Play Sandbox** plays from Boot alone, using `playModeStartScene`, and points that
      online scene at the sandbox for that run.
    - When play mode ends, both are put back, whatever scenes are open.
    - The sandbox is in the build settings (enabled), because FishNet loads it by name.
  - **Tested in the editor:**
    - the sandbox: a level built and the pawn spawned on a generated point;
    - Regenerate and Load mid-match: the pawn respawned on the new level;
    - host options: seed, paste, edit, and invalid code;
    - the panel's text fits, and both views fit a 1920 × 1080 Game view.
    The only errors in a clean run were the signal server's websocket `SocketException`, which has nothing to do
    with levels.
- **World-unit UVs (2026-10-26, from the user):** every level mesh (terrain, slopes, buildings, boundary, and the
  placeholder props with them) gets UV0 in world units, so a square texture tiles once per 1 × 1 unit
  (`PrimitiveMesher.WorldUv`).
  - Faces at least 30° from vertical (|n.y| ≥ 0.5) are projected from above: u = x, v = z, mirrored on undersides.
    Flat ground is exactly 1:1, and the tiling runs unbroken across terrain triangles, plots, ramps and banks.
  - A slope tilted θ is stretched by 1/cos θ along its fall: 6% at the 20° terrain limit, 15% on a 30° ramp. No
    seamless mapping of curved ground can avoid this.
  - Every other face is mapped on its own plane: u is the horizontal distance along it (left to right, seen from
    outside) and v the height. So walls are exactly 1:1, line up from floor to floor, and have a seam only at
    corners.
  - Meshes also get tangents, for normal maps.
  - Decals use the same scale: u is the distance along a strip and v the distance across it.
  - Wall-corner strips now carry their real half width, so their texture isn't squeezed into half the space. That
    was a bug the new tests found.
- **Blended ground (2026-10-27, from the user):** texture-array layers with per-vertex layer IDs and barycentric
  weights replace the dip and mound edge decals and the sinkhole rim decals ("Blended ground" above). Choices made:
  - The layers come from the theme's existing ground materials, so a theme needs only the `terrain` material, plus
    `groundRim` for the rim's look.
  - Bands come from cutting each fan triangle, not from a texture painted over the level. The ground has corners only
    where sub-rooms meet, so per-vertex blending needed the extra vertices anyway.
  - Bands are about their width: exactly that from the nearer outline edge at each corner, wider along the farther
    edge.
  - The height blend uses each texture's brightness in place of a height map.
  - Ramps and banks keep their own materials.
  - With no `terrain` material, a theme draws as before.
- **Slope and cliff transitions (2026-10-28, from the user):** ramp and bank tops blend into the ground at their
  top edge and foot and into their sides, flares blend from side to ground, and ledges and cliffs get a lip of cliff
  material on their higher ground ("Blended ground"). Choices made:
  - The sub-rooms under a slope are pinned to the plot's plane again (they had been freed to be raised and tilted
    once slopes were fitted after the terrain). A slope still fits whatever ground is there exactly; pinning keeps that
    ground plain where the slope meets it, so the slope's ground-coloured edge matches it, and keeps the slope's grade
    the one the plan checked.
  - The lip replaces the ledge decal, and goes on cliff tops too.
  - A flare's blend runs across its whole width rather than a fixed band: flares are narrow, and a band would need the
    ground's height under the flare, which is carved away.
  - Cliff faces stay one layer; the blend between ground and cliff happens on the ground's top.
- **No z-fighting (2026-10-27): reverted.** A mesh-time fix (`HiddenFaces`, cutting faces against the solids around them in `LevelBuilder`) was tried and rejected by the user: overlapping geometry must not be generated in the first place. It is replaced by the rules and overlap check in [No overlapping geometry](#no-overlapping-geometry). The sources it found are the table there: slope feet over ground, slope sides along cliffs, wall tops under roofs, door thresholds, stair wells, overhanging floors, parapets over walls, and half-thickness wall corners.
- **Spawn points (done in milestone 10).** The plan in the item above held, except that candidates come from the
  validation's surfaces rather than the path stretch samples.
- **Pockets, mostly fixed.** Low cells boxed in by a ramp's side near a plot corner, or by a bank's square end at
  an obtuse corner, made validation fail ("no way back") and forced retries. The ramp clearance and bank mitres
  above removed most of them. Over 30 seeds every preset succeeds, needing 31–44 attempts (default 33, small 34,
  dense 31, tall 44, after the padded-box outline and the one-slope rule). A few single-cell pockets remain; the level retry clears them (milestone 7 was dropped).
- **Path stretch is low without buildings and props:** 1.02–1.13 on these levels, against the 1.3 target.
- **Slopes on the grid are anisotropic.** Rise per cell is measured along the grid axes, so a slope that runs
  diagonally is accepted up to about 40° (0.5·tanθ·cos 45° ≤ 0.3). The 35° test is axis-aligned. That's harmless
  while the generator caps slopes at 30°. Also note that the pawn's CharacterController has a `slopeLimit` of 45°,
  so play accepts up to 45° whichever way the slope runs.
- **Metric error formula (decided 2026-10-22):** `weight × max(0, |measured − target| − tolerance)`, as in
  [Path stretch](#path-stretch-level). v1's formula (`weight × |measured − target|` outside the tolerance) is gone: it
  jumped by `weight × tolerance` at the band's edge, so search overvalued moves that just crossed into the band.
- **Generation speed** isn't a goal yet. Path stretch is the new cost centre: about 600 samples × a 40k-cell Dijkstra
  on a 100 × 100 level. Levers: sample stride, a capped number of sources, and incremental re-measurement in search.
