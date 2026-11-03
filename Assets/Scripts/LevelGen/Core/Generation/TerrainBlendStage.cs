using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A layer of the blended ground: one texture of the terrain material's arrays each.</summary>
    public enum TerrainLayer : byte
    {
        Ground = 0,
        Mound = 1,
        Dip = 2,
        Sinkhole = 3,
        /// <summary>The higher ground round a sinkhole.</summary>
        Rim = 4,
        /// <summary>A ramp's top.</summary>
        Ramp = 5,
        /// <summary>A bank's top.</summary>
        Bank = 6,
        /// <summary>A ramp's or bank's sides and flares.</summary>
        RampSide = 7,
        /// <summary>Cliff and ledge faces, and the lip of ground along their tops.</summary>
        Cliff = 8,
    }

    public static class TerrainLayers
    {
        public const int Count = 9;

        /// <summary>The layer a face is drawn with on the blended ground, or false for a face that isn't part of it.
        /// Ground tops, ramp and bank tops, their sides and flares, and cliff faces all are, so they blend into each
        /// other where they meet.</summary>
        public static bool TryOf(SurfaceTag tag, out TerrainLayer layer)
        {
            layer = tag switch
            {
                SurfaceTag.GroundMound => TerrainLayer.Mound,
                SurfaceTag.GroundDip => TerrainLayer.Dip,
                SurfaceTag.GroundSinkhole => TerrainLayer.Sinkhole,
                SurfaceTag.Ramp => TerrainLayer.Ramp,
                SurfaceTag.Bank => TerrainLayer.Bank,
                SurfaceTag.RampSide => TerrainLayer.RampSide,
                SurfaceTag.CliffFace => TerrainLayer.Cliff,
                _ => TerrainLayer.Ground,
            };
            return tag is SurfaceTag.Ground or SurfaceTag.GroundMound or SurfaceTag.GroundDip or SurfaceTag.GroundSinkhole
                or SurfaceTag.Ramp or SurfaceTag.Bank or SurfaceTag.RampSide or SurfaceTag.CliffFace;
        }

        /// <summary>The surface a layer is drawn as without the blend material.</summary>
        public static SurfaceTag TagOf(TerrainLayer layer) => layer switch
        {
            TerrainLayer.Mound => SurfaceTag.GroundMound,
            TerrainLayer.Dip => SurfaceTag.GroundDip,
            TerrainLayer.Sinkhole => SurfaceTag.GroundSinkhole,
            TerrainLayer.Ramp => SurfaceTag.Ramp,
            TerrainLayer.Bank => SurfaceTag.Bank,
            TerrainLayer.RampSide => SurfaceTag.RampSide,
            TerrainLayer.Cliff => SurfaceTag.CliffFace,
            _ => SurfaceTag.Ground,
        };

        /// <summary>Whether a tag is a ground top (rather than a slope or a cliff face).</summary>
        public static bool IsGround(SurfaceTag tag) =>
            tag is SurfaceTag.Ground or SurfaceTag.GroundMound or SurfaceTag.GroundDip or SurfaceTag.GroundSinkhole;
    }

    /// <summary>Distances above/below the original room plane, in world units.</summary>
    public struct TerrainBlendSettings
    {
        public float HillThreshold;
        public float ValleyThreshold;
        /// <summary>Vertical blend ranges ending at the corresponding threshold. Clamped to the threshold so the
        /// original room plane always receives ordinary ground, even when both blends are wide.</summary>
        public float HillBlendWidth;
        public float ValleyBlendWidth;
        public float RimWidth;
        /// <summary>How far in from a ramp's or bank's top edge, foot and sides its top fades into the ground and its
        /// sides.</summary>
        public float SlopeEdgeWidth;
        /// <summary>How far back from a ledge's or cliff's top edge the ground fades into the cliff's material.</summary>
        public float LipWidth;

        public static TerrainBlendSettings Default => new()
        {
            HillThreshold = 0.5f, ValleyThreshold = 0.5f, HillBlendWidth = 0.25f, ValleyBlendWidth = 0.25f, RimWidth = 0.4f,
            SlopeEdgeWidth = 0.5f, LipWidth = 0.4f,
        };

        public TerrainBlendSettings Sanitized() => new()
        {
            HillThreshold = Mathf.Max(0.0001f, HillThreshold),
            ValleyThreshold = Mathf.Max(0.0001f, ValleyThreshold),
            HillBlendWidth = Mathf.Clamp(HillBlendWidth, 0f, Mathf.Max(0.0001f, HillThreshold)),
            ValleyBlendWidth = Mathf.Clamp(ValleyBlendWidth, 0f, Mathf.Max(0.0001f, ValleyThreshold)),
            RimWidth = Mathf.Max(0f, RimWidth),
            SlopeEdgeWidth = Mathf.Max(0f, SlopeEdgeWidth),
            LipWidth = Mathf.Max(0f, LipWidth),
        };
    }

    /// <summary>
    /// One triangle of blended ground: its corners, the three layers it blends, and each corner's weights over them
    /// (summing to 1). A triangle of a split fan triangle is one-hot, its corner k all layer k; a piece of one that the
    /// ground was carved down to has corners on the cut, whose weights are their place in the triangle.
    /// </summary>
    public struct BlendTriangle
    {
        public Vector3 A, B, C;
        public TerrainLayer Layer0, Layer1, Layer2;
        public Vector3 WeightA, WeightB, WeightC;

        public static BlendTriangle OneHot(Vector3 a, Vector3 b, Vector3 c, TerrainLayer la, TerrainLayer lb, TerrainLayer lc) =>
            new()
            {
                A = a, B = b, C = c, Layer0 = la, Layer1 = lb, Layer2 = lc,
                WeightA = new Vector3(1f, 0f, 0f), WeightB = new Vector3(0f, 1f, 0f), WeightC = new Vector3(0f, 0f, 1f),
            };

        /// <summary>How much of <paramref name="layer"/> there is at corner <paramref name="k"/> (0, 1 or 2).</summary>
        public float Weight(int k, TerrainLayer layer)
        {
            var w = k == 0 ? WeightA : k == 1 ? WeightB : WeightC;
            return (Layer0 == layer ? w.x : 0f) + (Layer1 == layer ? w.y : 0f) + (Layer2 == layer ? w.z : 0f);
        }

        public Vector3 Corner(int k) => k == 0 ? A : k == 1 ? B : C;
    }

    /// <summary>
    /// Render-side material assignment from finished ground heights relative to each plot's original plane.
    /// Splitting at the ends of the height blends makes weights linear within every output triangle, even when a
    /// single input face crosses from a hill through ordinary ground to a valley. Sinkhole floors and rims retain
    /// their dedicated materials; all other terrain materials are independent of operation tags.
    /// The description and the ground surface are unchanged.
    /// </summary>
    public static class TerrainBlendStage
    {
        public static Dictionary<int, List<BlendTriangle>> Build(LevelDescription level, in TerrainBlendSettings settings)
        {
            var result = new Dictionary<int, List<BlendTriangle>>();
            var baselines = new Dictionary<int, PlotGround>();
            foreach (var plot in level.Plots)
                baselines[plot.Id] = new PlotGround
                {
                    Height = plot.BaseHeight, Slope = plot.Slope,
                    Centroid = Polygon.Centroid(level.Polygons[plot.Polygon]),
                };
            var sinkholeCorners = new HashSet<(int plot, Vector2 point)>();
            var sinkholeEdges = new HashSet<(int plot, Vector2 from, Vector2 to)>();
            foreach (var room in level.SubRooms)
            {
                if (room.Surface != SurfaceTag.GroundSinkhole) continue;
                var cell = level.Polygons[room.Polygon];
                for (int k = 0; k < cell.Count; k++)
                {
                    var a = cell[k]; var b = cell[(k + 1) % cell.Count];
                    sinkholeCorners.Add((room.Plot, a));
                    sinkholeEdges.Add((room.Plot, a, b));
                    sinkholeEdges.Add((room.Plot, b, a));
                }
            }
            var s = settings.Sanitized();
            var cuts = new[] { -s.ValleyThreshold, -s.ValleyThreshold + s.ValleyBlendWidth,
                0f, s.HillThreshold - s.HillBlendWidth, s.HillThreshold };
            var lips = Lips(level, baselines, s.LipWidth);

            for (int i = 0; i < level.Primitives.Count; i++)
            {
                var p = level.Primitives[i];
                if (p.Shape != PrimitiveShape.Prism || p.OwnerKind != OwnerKind.Plot ||
                    p.Surfaces.Top == SurfaceTag.GroundSinkhole || !TerrainLayers.TryOf(p.Surfaces.Top, out _) ||
                    !baselines.TryGetValue(p.OwnerId, out var baseline)) continue;
                var outline = level.Polygons[p.Polygon];
                if (outline.Count < 3) continue;
                var polygon = new List<HeightVertex>();
                foreach (var v in outline)
                {
                    float height = p.PrismTop(v.x, v.y);
                    polygon.Add(new HeightVertex(new Vector3(v.x, height, v.y), height - baseline.At(v)));
                }
                var regions = RimRegions(level, p, polygon, sinkholeCorners, sinkholeEdges, s.RimWidth);
                foreach (float cut in cuts)
                {
                    var next = new List<List<HeightVertex>>();
                    foreach (var region in regions) Split(region, cut, next);
                    regions = next;
                }
                var lipsHere = new List<Lip>();
                if (lips.TryGetValue(p.OwnerId, out var plotLips))
                    foreach (var lip in plotLips)
                        if (lip.Near(outline, s.LipWidth)) lipsHere.Add(lip);
                foreach (var lip in lipsHere)
                    foreach (var (normal, offset) in lip.Cuts(s.LipWidth))
                    {
                        var next = new List<List<HeightVertex>>();
                        foreach (var region in regions) SplitLine(region, normal, offset, next);
                        regions = next;
                    }
                if (lipsHere.Count > 0)
                    foreach (var region in regions)
                        for (int k = 0; k < region.Count; k++)
                        {
                            float weight = 0f;
                            foreach (var lip in lipsHere) weight = Mathf.Max(weight, lip.Weight(Xz(region[k].Position), s.LipWidth));
                            region[k] = new HeightVertex(region[k].Position, region[k].Deviation, region[k].Rim, weight);
                        }
                var triangles = new List<BlendTriangle>();
                foreach (var region in regions)
                {
                    float middle = 0f;
                    foreach (var v in region) middle += v.Deviation / region.Count;
                    // The third layer is the edge's: a sinkhole's rim, else a ledge's or cliff's lip.
                    bool rimEdge = region.Exists(v => v.Rim > 0f);
                    bool hasRim = rimEdge || region.Exists(v => v.Lip > 0f);
                    var edgeLayer = rimEdge ? TerrainLayer.Rim : TerrainLayer.Cliff;
                    bool upper = middle >= 0f;
                    // A zero-width blend is a hard boundary; use the region's side at its boundary vertices too.
                    bool hill = middle >= s.HillThreshold;
                    bool valley = middle <= -s.ValleyThreshold;
                    for (int k = 1; k + 1 < region.Count; k++)
                    {
                        var a = region[0]; var b = region[k]; var c = region[k + 1];
                        var ab = b.Position - a.Position;
                        var ac = c.Position - a.Position;
                        if (Mathf.Abs(ab.x * ac.z - ab.z * ac.x) <= 1e-12f) continue;
                        var triangle = new BlendTriangle
                        {
                            A = a.Position, B = b.Position, C = c.Position,
                            Layer0 = TerrainLayer.Ground,
                            Layer1 = hasRim ? (upper ? TerrainLayer.Mound : TerrainLayer.Dip) : TerrainLayer.Mound,
                            Layer2 = hasRim ? edgeLayer : TerrainLayer.Dip,
                            WeightA = WithRim(a, Weights(a.Deviation, s, hill, valley), hasRim, upper, rimEdge),
                            WeightB = WithRim(b, Weights(b.Deviation, s, hill, valley), hasRim, upper, rimEdge),
                            WeightC = WithRim(c, Weights(c.Deviation, s, hill, valley), hasRim, upper, rimEdge),
                        };
                        // Keep the shader's single-texture fast path for uniform ground, hills and valleys.
                        if (triangle.WeightA == triangle.WeightB && triangle.WeightA == triangle.WeightC)
                        {
                            var w = triangle.WeightA;
                            if (w.x == 1f || w.y == 1f || w.z == 1f)
                            {
                                var layer = w.x == 1f ? triangle.Layer0 : w.y == 1f ? triangle.Layer1 : triangle.Layer2;
                                triangle = BlendTriangle.OneHot(a.Position, b.Position, c.Position, layer, layer, layer);
                            }
                        }
                        triangles.Add(triangle);
                    }
                }
                result[i] = triangles;
            }
            Slopes(level, baselines, s.SlopeEdgeWidth, result);
            return result;
        }

        /// <summary>
        /// Ramp and bank tops, and their flares. A top is the ramp's or bank's layer, fading to ground within
        /// <paramref name="width"/> of its top edge (the face) and its foot, and to its sides' layer within it of each
        /// flared side; the pieces are cut at those distances, so every weight is linear in its triangle. A flare
        /// fades from the sides' layer at the slope's edge to ground at its foot, across its width. Corners shared with
        /// the slope's neighbours get their neighbours' layer (ground at the face and foot, the side at a flare), so
        /// nothing shows a seam.
        /// </summary>
        private static void Slopes(LevelDescription level, Dictionary<int, PlotGround> planes, float width,
            Dictionary<int, List<BlendTriangle>> result)
        {
            var pieces = new Dictionary<int, List<int>>();
            for (int i = 0; i < level.Primitives.Count; i++)
            {
                var p = level.Primitives[i];
                if (p.OwnerKind != OwnerKind.Transition || p.Shape != PrimitiveShape.Prism) continue;
                if (!pieces.TryGetValue(p.OwnerId, out var list)) pieces[p.OwnerId] = list = new List<int>();
                list.Add(i);
            }
            foreach (var t in level.Transitions)
            {
                if (t.Type is not (TransitionType.Ramp or TransitionType.Bank) || !pieces.TryGetValue(t.Id, out var own)) continue;
                if (!planes.TryGetValue(t.PlotA, out var a) || !planes.TryGetValue(t.PlotB, out var b)) continue;
                var mid = (t.From + t.To) * 0.5f;
                int low = a.At(mid) < b.At(mid) ? t.PlotA : t.PlotB;
                var lowPlot = level.Plots.Find(q => q.Id == low);
                if (lowPlot == null) continue;
                var lower = level.Polygons[lowPlot.Polygon];
                var dir = (t.To - t.From).normalized;
                var into = new Vector2(-dir.y, dir.x);
                if (Vector2.Dot(Polygon.Centroid(lower) - mid, into) < 0f) into = -into;
                var top = t.Type == TransitionType.Ramp ? TerrainLayer.Ramp : TerrainLayer.Bank;

                // The run, from the top pieces; the flared sides (along the face), as in TransitionStage.EmitSlopes.
                float run = 0f;
                foreach (int i in own)
                    if (level.Primitives[i].Surfaces.Top != SurfaceTag.RampSide)
                        foreach (var v in level.Polygons[level.Primitives[i].Polygon]) run = Mathf.Max(run, Vector2.Dot(into, v - t.From));
                if (run <= 1e-3f) continue;
                float tFrom = Vector2.Dot(dir, t.From);
                var sides = new List<(float at, float outward)>();
                if (t.Type == TransitionType.Ramp)
                {
                    float m = Vector2.Dot(dir, mid);
                    sides.Add((m - t.Width * 0.5f, -1f));
                    sides.Add((m + t.Width * 0.5f, 1f));
                }
                else
                {
                    if (!lower.Contains(t.From)) sides.Add((tFrom, -1f));
                    if (!lower.Contains(t.To)) sides.Add((Vector2.Dot(dir, t.To), 1f));
                }
                float U(Vector2 p) => Vector2.Dot(into, p - t.From);
                // The top's ground weight a distance u from the face: fading in at the face and at the foot.
                float GroundNear(float u) =>
                    width > 0f ? Mathf.Clamp01(Mathf.Max(1f - u / width, 1f - (run - u) / width)) : 0f;

                // How far each flare reaches out from its side, at each band line it has corners on.
                var reach = new List<(float side, float u, float w)>();
                foreach (int i in own)
                {
                    if (level.Primitives[i].Surfaces.Top != SurfaceTag.RampSide) continue;
                    foreach (var v in level.Polygons[level.Primitives[i].Polygon])
                        foreach (var (at, outward) in sides)
                        {
                            float d = (Vector2.Dot(dir, v) - at) * outward;
                            if (d > 1e-4f) reach.Add((at, U(v), d));
                        }
                }
                float Reach(float side, float u)
                {
                    float best = 0f;
                    foreach (var r in reach)
                        if (r.side == side && Mathf.Abs(r.u - u) < 1e-3f) best = Mathf.Max(best, r.w);
                    return best;
                }

                foreach (int i in own)
                {
                    var p = level.Primitives[i];
                    var outline = level.Polygons[p.Polygon];
                    if (outline.Count < 3) continue;
                    var triangles = new List<BlendTriangle>();
                    if (p.Surfaces.Top == SurfaceTag.RampSide)
                    {
                        // Ground by how far across the flare a corner is; on the slope's edge, the top's ground there.
                        var corners = new List<(Vector3 at, float ground)>();
                        foreach (var v in outline)
                        {
                            float ground = GroundNear(U(v));
                            foreach (var (at, outward) in sides)
                            {
                                float d = (Vector2.Dot(dir, v) - at) * outward;
                                if (d <= 1e-4f) continue;
                                float w = Reach(at, U(v));
                                ground = Mathf.Max(ground, w > 1e-4f ? Mathf.Clamp01(d / w) : 1f);
                            }
                            corners.Add((Top(p, v), ground));
                        }
                        for (int k = 1; k + 1 < corners.Count; k++)
                            triangles.Add(new BlendTriangle
                            {
                                A = corners[0].at, B = corners[k].at, C = corners[k + 1].at,
                                Layer0 = TerrainLayer.RampSide, Layer1 = TerrainLayer.Ground, Layer2 = TerrainLayer.Ground,
                                WeightA = new Vector3(1f - corners[0].ground, corners[0].ground, 0f),
                                WeightB = new Vector3(1f - corners[k].ground, corners[k].ground, 0f),
                                WeightC = new Vector3(1f - corners[k + 1].ground, corners[k + 1].ground, 0f),
                            });
                        result[i] = triangles;
                        continue;
                    }

                    // A top piece, cut where its edge bands end.
                    var regions = new List<List<HeightVertex>> { outline.ConvertAll(v => new HeightVertex(Top(p, v), 0f)) };
                    var cuts = new List<(Vector2 normal, float offset)>
                    {
                        (into, Vector2.Dot(into, t.From) + width), (into, Vector2.Dot(into, t.From) + run - width),
                    };
                    foreach (var (at, outward) in sides) cuts.Add((dir, at - outward * width));
                    foreach (var (normal, offset) in cuts)
                    {
                        var next = new List<List<HeightVertex>>();
                        foreach (var region in regions) SplitLine(region, normal, offset, next);
                        regions = next;
                    }
                    // Ground wins over the side near the face and foot, so a corner there matches the ground it meets.
                    Vector3 Weights(Vector3 position)
                    {
                        var v = Xz(position);
                        float ground = GroundNear(U(v));
                        float side = 0f;
                        foreach (var (at, outward) in sides)
                            if (width > 0f) side = Mathf.Max(side, Mathf.Clamp01(1f - (at - Vector2.Dot(dir, v)) * outward / width));
                        side *= 1f - ground;
                        return new Vector3(1f - ground - side, ground, side);
                    }
                    foreach (var region in regions)
                        for (int k = 1; k + 1 < region.Count; k++)
                        {
                            var pa = region[0].Position; var pb = region[k].Position; var pc = region[k + 1].Position;
                            var ab = pb - pa; var ac = pc - pa;
                            if (Mathf.Abs(ab.x * ac.z - ab.z * ac.x) <= 1e-12f) continue;
                            triangles.Add(new BlendTriangle
                            {
                                A = pa, B = pb, C = pc, Layer0 = top, Layer1 = TerrainLayer.Ground, Layer2 = TerrainLayer.RampSide,
                                WeightA = Weights(pa), WeightB = Weights(pb), WeightC = Weights(pc),
                            });
                        }
                    result[i] = triangles;
                }
            }
        }

        /// <summary>The original sinkhole rim bands, clipped to surviving ground pieces after slope carving.</summary>
        private static List<List<HeightVertex>> RimRegions(LevelDescription level, Primitive p,
            List<HeightVertex> polygon, HashSet<(int plot, Vector2 point)> sinkholeCorners,
            HashSet<(int plot, Vector2 from, Vector2 to)> sinkholeEdges, float width)
        {
            var plain = new List<List<HeightVertex>> { polygon };
            if (sinkholeCorners.Count == 0 || width <= 0f) return plain;
            var centre = Polygon.Centroid(level.Polygons[p.Polygon]);
            foreach (var room in level.SubRooms)
            {
                if (room.Plot != p.OwnerId || room.Surface == SurfaceTag.GroundSinkhole) continue;
                var outline = level.Polygons[room.Polygon];
                if (!Polygon.Contains(outline, centre)) continue;
                for (int k = 0; k < outline.Count; k++)
                {
                    var a = outline[k]; var b = outline[(k + 1) % outline.Count];
                    if (!InTriangle(centre, room.Site, a, b)) continue;
                    bool rimA = sinkholeCorners.Contains((p.OwnerId, a)), rimB = sinkholeCorners.Contains((p.OwnerId, b));
                    if (!rimA && !rimB) return plain;
                    var site = Top(p, room.Site); var va = Top(p, a); var vb = Top(p, b);
                    var ca = Vector3.LerpUnclamped(va, site, Cut(outline, room.Site, k, width));
                    var cb = Vector3.LerpUnclamped(vb, site, Cut(outline, room.Site, (k + 1) % outline.Count, width));
                    var la = rimA ? TerrainLayer.Rim : TerrainLayer.Ground;
                    var lb = rimB ? TerrainLayer.Rim : TerrainLayer.Ground;
                    var masks = new List<BlendTriangle>();
                    if (rimA && rimB && !sinkholeEdges.Contains((p.OwnerId, a, b)))
                    {
                        // Corners can touch different sinkholes without the edge itself bordering either hole.
                        // Cap both rims locally instead of painting a connecting strip along the entire edge.
                        float reach = Mathf.Min(0.5f, width / (b - a).magnitude);
                        var stopA = Top(p, Vector2.LerpUnclamped(a, b, reach));
                        var stopB = Top(p, Vector2.LerpUnclamped(b, a, reach));
                        masks.Add(BlendTriangle.OneHot(va, stopA, ca, TerrainLayer.Rim, TerrainLayer.Ground, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(stopB, vb, cb, TerrainLayer.Ground, TerrainLayer.Rim, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(stopA, stopB, cb, TerrainLayer.Ground, TerrainLayer.Ground, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(stopA, cb, ca, TerrainLayer.Ground, TerrainLayer.Ground, TerrainLayer.Ground));
                    }
                    else if (rimA != rimB)
                    {
                        var from = rimA ? a : b; var to = rimA ? b : a;
                        var stop = Top(p, Vector2.LerpUnclamped(from, to,
                            Mathf.Min(0.5f, width / (to - from).magnitude)));
                        masks.Add(BlendTriangle.OneHot(va, stop, ca, la, TerrainLayer.Ground, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(stop, vb, cb, TerrainLayer.Ground, lb, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(stop, cb, ca, TerrainLayer.Ground, TerrainLayer.Ground, TerrainLayer.Ground));
                    }
                    else
                    {
                        masks.Add(BlendTriangle.OneHot(va, vb, cb, la, lb, TerrainLayer.Ground));
                        masks.Add(BlendTriangle.OneHot(va, cb, ca, la, TerrainLayer.Ground, TerrainLayer.Ground));
                    }
                    masks.Add(BlendTriangle.OneHot(site, ca, cb, TerrainLayer.Ground, TerrainLayer.Ground, TerrainLayer.Ground));
                    var regions = new List<List<HeightVertex>>();
                    foreach (var mask in masks)
                    {
                        var clipped = ClipMask(polygon, mask);
                        if (clipped.Count >= 3) regions.Add(clipped);
                    }
                    return regions;
                }
            }
            return plain;
        }

        private static Vector2 Xz(Vector3 p) => new(p.x, p.z);
        private static Vector3 Top(Primitive p, Vector2 v) => new(v.x, p.PrismTop(v.x, v.y), v.y);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float x = Cross(b - a, p - a), y = Cross(c - b, p - b), z = Cross(a - c, p - c);
            return !((x < -1e-7f || y < -1e-7f || z < -1e-7f) && (x > 1e-7f || y > 1e-7f || z > 1e-7f));
        }

        private static float Cut(List<Vector2> outline, Vector2 site, int corner, float width)
        {
            var v = outline[corner];
            float Distance(Vector2 a, Vector2 b) => Mathf.Abs(Cross(b - a, site - a)) / Mathf.Max(1e-6f, (b - a).magnitude);
            float h = Mathf.Min(Distance(outline[(corner + outline.Count - 1) % outline.Count], v),
                Distance(v, outline[(corner + 1) % outline.Count]));
            return h <= 1e-5f ? 0f : Mathf.Min(0.7f, width / h);
        }

        private static List<HeightVertex> ClipMask(List<HeightVertex> polygon, BlendTriangle mask)
        {
            var a = Xz(mask.A); var b = Xz(mask.B); var c = Xz(mask.C);
            float area = Cross(b - a, c - a);
            if (Mathf.Abs(area) <= 1e-12f) return new List<HeightVertex>();
            float sign = area > 0f ? 1f : -1f;
            var corners = new[] { a, b, c };
            var result = new List<HeightVertex>(polygon);
            for (int i = 0; i < 3 && result.Count >= 3; i++)
            {
                var from = corners[i]; var edge = corners[(i + 1) % 3] - from;
                var kept = new List<HeightVertex>();
                for (int k = 0; k < result.Count; k++)
                {
                    var v = result[k]; var w = result[(k + 1) % result.Count];
                    float dv = sign * Cross(edge, Xz(v.Position) - from);
                    float dw = sign * Cross(edge, Xz(w.Position) - from);
                    if (dv >= 0f) kept.Add(v);
                    if ((dv < 0f && dw > 0f) || (dv > 0f && dw < 0f))
                    {
                        float t = dv / (dv - dw);
                        kept.Add(new HeightVertex(Vector3.LerpUnclamped(v.Position, w.Position, t),
                            Mathf.LerpUnclamped(v.Deviation, w.Deviation, t)));
                    }
                }
                result = kept;
            }
            for (int k = 0; k < result.Count; k++)
            {
                var v = result[k]; var at = Xz(v.Position);
                float wb = Cross(at - a, c - a) / area, wc = Cross(b - a, at - a) / area;
                float rim = (1f - wb - wc) * mask.Weight(0, TerrainLayer.Rim) +
                    wb * mask.Weight(1, TerrainLayer.Rim) + wc * mask.Weight(2, TerrainLayer.Rim);
                result[k] = new HeightVertex(v.Position, v.Deviation, Mathf.Clamp01(rim));
            }
            return result;
        }

        private static Vector3 Weights(float deviation, in TerrainBlendSettings s, bool hillRegion, bool valleyRegion)
        {
            float hill = s.HillBlendWidth > 0f
                ? Mathf.Clamp01((deviation - (s.HillThreshold - s.HillBlendWidth)) / s.HillBlendWidth)
                : hillRegion ? 1f : 0f;
            float valley = s.ValleyBlendWidth > 0f
                ? Mathf.Clamp01((-deviation - (s.ValleyThreshold - s.ValleyBlendWidth)) / s.ValleyBlendWidth)
                : valleyRegion ? 1f : 0f;
            return new Vector3(Mathf.Max(0f, 1f - hill - valley), hill, valley);
        }

        private static Vector3 WithRim(HeightVertex v, Vector3 weights, bool hasRim, bool upper, bool rimEdge)
        {
            if (!hasRim) return weights;
            float rim = Mathf.Clamp01(rimEdge ? v.Rim : v.Lip);
            return new Vector3(weights.x * (1f - rim), (upper ? weights.y : weights.z) * (1f - rim), rim);
        }

        private readonly struct HeightVertex
        {
            public readonly Vector3 Position;
            public readonly float Deviation;
            public readonly float Rim;
            /// <summary>The cliff lip's weight, found once the region is cut (see <see cref="Lip"/>).</summary>
            public readonly float Lip;
            public HeightVertex(Vector3 position, float deviation, float rim = 0f, float lip = 0f)
            { Position = position; Deviation = deviation; Rim = rim; Lip = lip; }
        }

        /// <summary>
        /// The top edge of a ledge, cliff or blocked transition, on its higher plot: the ground there fades into the
        /// cliff's material, fully at the edge and not at all <c>LipWidth</c> back. Pieces are cut at that distance and
        /// a lip's width past each end (where it fades round), so the weight is linear in every triangle but those
        /// round the ends.
        /// </summary>
        private readonly struct Lip
        {
            public readonly Vector2 From, Dir, Inward;
            public readonly float Length;
            /// <summary>An end that meets no other lip (a slope's face, a flat crossing): the lip tapers to nothing
            /// along the edge there, so the corner it shares with its neighbour is plain ground.</summary>
            public readonly bool TaperFrom, TaperTo;

            public Lip(Vector2 from, Vector2 to, Vector2 inward, bool taperFrom, bool taperTo)
            {
                From = from;
                Length = (to - from).magnitude;
                Dir = (to - from) / Mathf.Max(1e-6f, Length);
                Inward = inward;
                TaperFrom = taperFrom;
                TaperTo = taperTo;
            }

            /// <summary>The cut lines, as <c>dot(normal, p) = offset</c>.</summary>
            public IEnumerable<(Vector2 normal, float offset)> Cuts(float width)
            {
                float d = Vector2.Dot(Inward, From), t = Vector2.Dot(Dir, From);
                yield return (Inward, d + width);
                yield return (Dir, t - width);
                yield return (Dir, t);
                yield return (Dir, t + Length);
                yield return (Dir, t + Length + width);
                if (TaperFrom) yield return (Dir, t + width);
                if (TaperTo) yield return (Dir, t + Length - width);
            }

            /// <summary>1 on the edge, falling to 0 a lip's width away from it (on the higher side only).</summary>
            public float Weight(Vector2 p, float width)
            {
                if (width <= 0f) return 0f;
                var r = p - From;
                float d = Vector2.Dot(Inward, r);
                if (d < -1e-4f) return 0f;
                float along = Vector2.Dot(Dir, r);
                float t = Mathf.Clamp(along, 0f, Length);
                float distance = (r - Dir * t).magnitude;
                float taper = 1f;
                if (TaperFrom) taper = Mathf.Min(taper, Mathf.Clamp01(along / width));
                if (TaperTo) taper = Mathf.Min(taper, Mathf.Clamp01((Length - along) / width));
                return Mathf.Clamp01(1f - distance / width) * taper;
            }

            public bool Near(List<Vector2> polygon, float width)
            {
                foreach (var v in polygon)
                {
                    var r = v - From;
                    float t = Vector2.Dot(Dir, r);
                    if (t < -width - 1f || t > Length + width + 1f) continue;
                    if (Mathf.Abs(Vector2.Dot(Inward, r)) < width + 1f) return true;
                }
                // A large piece can span the edge's strip without a corner near it.
                var centre = Polygon.Centroid(polygon);
                float reach = 0f;
                foreach (var v in polygon) reach = Mathf.Max(reach, (v - centre).magnitude);
                var c = centre - From;
                float tc = Mathf.Clamp(Vector2.Dot(Dir, c), 0f, Length);
                return (c - Dir * tc).magnitude < reach + width;
            }
        }

        /// <summary>Every ledge's, cliff's and blocked edge's lip, by the higher plot it lies on.</summary>
        private static Dictionary<int, List<Lip>> Lips(LevelDescription level, Dictionary<int, PlotGround> planes, float width)
        {
            var lips = new Dictionary<int, List<Lip>>();
            if (width <= 0f) return lips;
            bool IsLip(TransitionType type) => type is TransitionType.Ledge or TransitionType.Cliff or TransitionType.Blocked;
            // An end tapers unless another lip of the same edge between the same plots continues from it.
            bool Continues(TransitionDescription t, Vector2 end)
            {
                foreach (var o in level.Transitions)
                    if (o.Id != t.Id && IsLip(o.Type) && (o.From == end || o.To == end)
                        && (o.PlotA == t.PlotA || o.PlotA == t.PlotB || o.PlotB == t.PlotA || o.PlotB == t.PlotB))
                        return true;
                return false;
            }
            foreach (var t in level.Transitions)
            {
                if (!IsLip(t.Type)) continue;
                if (!planes.TryGetValue(t.PlotA, out var a) || !planes.TryGetValue(t.PlotB, out var b)) continue;
                var mid = (t.From + t.To) * 0.5f;
                int high = a.At(mid) >= b.At(mid) ? t.PlotA : t.PlotB;
                var dir = (t.To - t.From).normalized;
                var inward = new Vector2(-dir.y, dir.x);
                var plot = level.Plots.Find(q => q.Id == high);
                if (plot == null) continue;
                if (Vector2.Dot(Polygon.Centroid(level.Polygons[plot.Polygon]) - mid, inward) < 0f) inward = -inward;
                if (!lips.TryGetValue(high, out var list)) lips[high] = list = new List<Lip>();
                list.Add(new Lip(t.From, t.To, inward, !Continues(t, t.From), !Continues(t, t.To)));
            }
            return lips;
        }

        /// <summary>Splits a region where it crosses the line <c>dot(normal, p) = offset</c>, interpolating every field.</summary>
        private static void SplitLine(List<HeightVertex> polygon, Vector2 normal, float offset, List<List<HeightVertex>> into)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            var d = new float[polygon.Count];
            for (int k = 0; k < polygon.Count; k++)
            {
                d[k] = Vector2.Dot(normal, Xz(polygon[k].Position)) - offset;
                min = Mathf.Min(min, d[k]);
                max = Mathf.Max(max, d[k]);
            }
            if (min >= -1e-6f || max <= 1e-6f) { into.Add(polygon); return; }
            var below = new List<HeightVertex>();
            var above = new List<HeightVertex>();
            for (int k = 0; k < polygon.Count; k++)
            {
                int j = (k + 1) % polygon.Count;
                var a = polygon[k]; var b = polygon[j];
                if (d[k] <= 0f) below.Add(a);
                if (d[k] >= 0f) above.Add(a);
                if ((d[k] < 0f && d[j] > 0f) || (d[k] > 0f && d[j] < 0f))
                {
                    // Always from the lower side to the higher, so a shared edge is cut at the same point either way.
                    bool up = d[k] < d[j];
                    var lo = up ? a : b; var hi = up ? b : a;
                    float dl = up ? d[k] : d[j], dh = up ? d[j] : d[k];
                    float t = -dl / (dh - dl);
                    var crossing = new HeightVertex(Vector3.LerpUnclamped(lo.Position, hi.Position, t),
                        Mathf.LerpUnclamped(lo.Deviation, hi.Deviation, t), Mathf.LerpUnclamped(lo.Rim, hi.Rim, t));
                    below.Add(crossing);
                    above.Add(crossing);
                }
            }
            if (below.Count >= 3) into.Add(below);
            if (above.Count >= 3) into.Add(above);
        }

        /// <summary>Split only when a cut strictly crosses a polygon: a face on a threshold is emitted once.</summary>
        private static void Split(List<HeightVertex> polygon, float cut, List<List<HeightVertex>> into)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var v in polygon) { min = Mathf.Min(min, v.Deviation); max = Mathf.Max(max, v.Deviation); }
            if (min >= cut || max <= cut) { into.Add(polygon); return; }
            var below = new List<HeightVertex>();
            var above = new List<HeightVertex>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count];
                if (a.Deviation <= cut) below.Add(a);
                if (a.Deviation >= cut) above.Add(a);
                if ((a.Deviation < cut && b.Deviation > cut) || (a.Deviation > cut && b.Deviation < cut))
                {
                    // Always interpolate from lower to higher, independent of the shared edge's winding.
                    var low = a.Deviation < b.Deviation ? a : b;
                    var high = a.Deviation < b.Deviation ? b : a;
                    float t = (cut - low.Deviation) / (high.Deviation - low.Deviation);
                    var crossing = new HeightVertex(Vector3.LerpUnclamped(low.Position, high.Position, t), cut,
                        Mathf.LerpUnclamped(low.Rim, high.Rim, t));
                    below.Add(crossing); above.Add(crossing);
                }
            }
            if (below.Count >= 3) into.Add(below);
            if (above.Count >= 3) into.Add(above);
        }
    }
}
