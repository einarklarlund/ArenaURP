using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Stage 6: a wall along every edge of the playable area's outline: the rectangle's sides and the edges against
    /// cut-out cells. Each wall is a prism standing just outside its plot, face to face with it: its edge's band, cut at
    /// each end by the mitre line on the corner's bisector (square at a free end), so walls meeting at a corner close it
    /// without poking into a plot at a corner that turns inward. A band shorter than it is thick comes to a point where its
    /// mitre lines cross; past that its neighbours' bands meet, so each wall gives way to the walls made before it and none
    /// overlaps. Each also gives way to every plot: where the outline touches itself at a corner, the mitre may be taken
    /// with the wrong edge there. Walls are exempt from the bounds check; plots need no edge treatment along them.
    /// </summary>
    public static class BoundaryStage
    {
        /// <summary>How far outside the outline a wall starts, so a cell centre lying exactly on it stays plot ground.</summary>
        public const float Clearance = 1e-3f;

        public static void Emit(LevelDescription level, in BoundarySettings boundary, List<PlotEdge> outline)
        {
            float t = boundary.Thickness;
            var outward = new Vector2[outline.Count];
            for (int i = 0; i < outline.Count; i++)
            {
                var along = (outline[i].To - outline[i].From).normalized;
                // The plot is on the left of a counter-clockwise edge, so outward is to the right.
                outward[i] = new Vector2(along.y, -along.x);
            }
            // The line an edge's band stops at, at its end `joint`, as dot(normal, p) ≤ offset with the normal pointing
            // away from the edge: the mitre with the edge meeting it there, or square at a free end (or a hairpin).
            (Vector2 normal, float offset) End(int i, Vector2 joint, bool start)
            {
                var away = (outline[i].To - outline[i].From).normalized * (start ? -1f : 1f);
                for (int j = 0; j < outline.Count; j++)
                    if (j != i && (start ? outline[j].To : outline[j].From) == joint)
                    {
                        var mitre = outward[i] + outward[j];
                        if (mitre.sqrMagnitude < 1e-6f) break;
                        var normal = new Vector2(-mitre.y, mitre.x).normalized;
                        if (Vector2.Dot(normal, away) < 0f) normal = -normal;
                        return (normal, Vector2.Dot(normal, joint));
                    }
                return (away, Vector2.Dot(away, joint));
            }

            var earlier = new List<List<Vector2>>();
            foreach (var plot in level.Plots)
                if (plot.ParentId < 0) earlier.Add(level.Polygons[plot.Polygon]);
            for (int i = 0; i < outline.Count; i++)
            {
                var edge = outline[i];
                float length = (edge.To - edge.From).magnitude;
                if (length < Polygon.Epsilon) continue;
                var o = outward[i];
                var dir = (edge.To - edge.From) / length;
                // A rectangle round the band, longer than any mitre reaches, cut to the band and its two end lines.
                float reach = length + 4f * (t + Clearance) + 1f;
                var mid = (edge.From + edge.To) * 0.5f;
                var band = new List<Vector2>
                {
                    mid - dir * reach - o * reach, mid + dir * reach - o * reach, mid + dir * reach + o * reach, mid - dir * reach + o * reach,
                };
                float near = Vector2.Dot(o, edge.From);
                band = Polygon.ClipHalfPlane(band, -o, -(near + Clearance), true);
                band = Polygon.ClipHalfPlane(band, o, near + Clearance + t, true);
                var (startNormal, startOffset) = End(i, edge.From, true);
                band = Polygon.ClipHalfPlane(band, startNormal, startOffset, true);
                var (endNormal, endOffset) = End(i, edge.To, false);
                band = Polygon.ClipHalfPlane(band, endNormal, endOffset, true);
                if (band.Count < 3) continue;
                if (Polygon.SignedArea(band) < 0f) band.Reverse();

                var parts = new List<List<Vector2>> { band };
                foreach (var other in earlier)
                {
                    var next = new List<List<Vector2>>();
                    foreach (var part in parts) next.AddRange(Polygon.Subtract(part, other));
                    parts = next;
                }
                foreach (var part in parts)
                {
                    if (Polygon.MinWidth(part) <= Polygon.SliverWidth) continue;
                    level.AddPrism(PrimitiveRole.Wall, level.AddPolygon(part), TransitionStage.GroundBottom,
                        TransitionStage.GroundBottom + boundary.WallHeight, Vector2.zero, OwnerKind.Boundary, 0,
                        Surfaces.All(SurfaceTag.BoundaryWall));
                    earlier.Add(part);
                }
            }
        }

        /// <summary>The level rectangle's four sides, counter-clockwise.</summary>
        public static List<PlotEdge> Rectangle(float width, float depth)
        {
            var corners = new[] { new Vector2(0f, 0f), new Vector2(width, 0f), new Vector2(width, depth), new Vector2(0f, depth) };
            var edges = new List<PlotEdge>();
            for (int i = 0; i < 4; i++) edges.Add(new PlotEdge { A = -1, B = -1, From = corners[i], To = corners[(i + 1) % 4] });
            return edges;
        }
    }
}
