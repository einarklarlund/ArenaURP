using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Scene-view debug layers for the last generated level. Drawn in level space, which is world space
    /// because the preview root sits at the origin.
    /// </summary>
    /// <summary>Which pipeline stage's output the scene view shows.</summary>
    internal enum StageView
    {
        Partition,
        Transitions,
        Buildings,
        Final,
    }

    internal static class LevelGenOverlay
    {
        public static StageView Stage = StageView.Final;
        public static GenerationTrace Trace;
        public static LevelDescription Description;
        public static GenerationReport Report;

        public static bool Plots = Pref("Plots", true);
        public static bool WalkableSurfaces = Pref("WalkableSurfaces", false);
        public static bool MovementGraph = Pref("MovementGraph", false);
        public static bool WalkEdges = Pref("WalkEdges", false);
        public static bool Stretch = Pref("Stretch", true);
        public static bool Spines = Pref("Spines", true);
        public static bool Sites = Pref("Sites", true);

        private const float Lift = 0.03f;
        private static Material s_material;

        public static void Enable()
        {
            SceneView.duringSceneGui -= Draw;
            SceneView.duringSceneGui += Draw;
        }

        public static void Disable() => SceneView.duringSceneGui -= Draw;

        public static void DrawToggles()
        {
            EditorGUI.BeginChangeCheck();
            Plots = Toggle("Plot outlines and transitions", "Plots", Plots);
            WalkableSurfaces = Toggle("Walkable surfaces by height", "WalkableSurfaces", WalkableSurfaces);
            MovementGraph = Toggle("Movement graph (jump, drop, violations)", "MovementGraph", MovementGraph);
            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!MovementGraph))
                WalkEdges = Toggle("Include walk edges", "WalkEdges", WalkEdges);
            Stretch = Toggle("Path stretch heatmap and worst pair", "Stretch", Stretch);
            Spines = Toggle("Building spines, entrances and stair cores", "Spines", Spines);
            Sites = Toggle("Chest sites, pickup lines and room links", "Sites", Sites);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        }

        private static void Draw(SceneView view)
        {
            var debug = Report?.Debug;
            if (debug == null || Description == null || Event.current.type != EventType.Repaint) return;

            Material.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            if (Stage == StageView.Partition && Trace?.Partition != null)
            {
                DrawPartition(Trace.Partition);
                GL.PopMatrix();
                DrawPartitionLabels(Trace.Partition);
                return;
            }
            if (Stage == StageView.Transitions)
            {
                DrawPlots();
                GL.PopMatrix();
                DrawPlotLabels();
                return;
            }
            if (Stage == StageView.Buildings)
            {
                DrawPlots();
                DrawTerrain();
                DrawBuildings();
                GL.PopMatrix();
                DrawBuildingLabels();
                return;
            }
            if (WalkableSurfaces) DrawSurfaces(debug);
            if (MovementGraph) DrawMovementGraph(debug);
            if (Stretch) DrawStretch(debug);
            if (Plots) DrawPlots();
            if (Spines) DrawBuildings();
            if (Sites) DrawSites();
            GL.PopMatrix();

            if (Plots) DrawPlotLabels();
        }

        // ---------------------------------------------------------------- layers

        /// <summary>
        /// Each path stretch sample as a tile coloured by its mean stretch (green 1.0 to red at 2.0 or more), and the
        /// worst pair's path in magenta from a white start to a black end.
        /// </summary>
        private static void DrawStretch(MeasurementDebug debug)
        {
            var stretch = Report.PathStretch;
            var surfaces = debug.Surfaces;
            GL.Begin(GL.QUADS);
            for (int i = 0; i < stretch.Samples.Count; i++)
            {
                var surface = surfaces[stretch.Samples[i]];
                float value = stretch.SampleStretch[i];
                var color = value <= 0f ? new Color(0.5f, 0.5f, 0.5f, 0.8f)
                    : Color.Lerp(new Color(0.2f, 0.9f, 0.3f), new Color(0.95f, 0.15f, 0.1f), Mathf.Clamp01(value - 1f));
                color.a = 0.9f;
                CellQuad(debug.Grid, surface.X, surface.Z, surface.Height + 3f * Lift, color, 3f);
            }
            if (stretch.WorstPath.Count > 1)
            {
                var first = surfaces[stretch.WorstPath[0]];
                var last = surfaces[stretch.WorstPath[^1]];
                CellQuad(debug.Grid, first.X, first.Z, first.Height + 4f * Lift, Color.white, 4f);
                CellQuad(debug.Grid, last.X, last.Z, last.Height + 4f * Lift, Color.black, 4f);
            }
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(new Color(1f, 0.2f, 0.9f));
            for (int i = 0; i + 1 < stretch.WorstPath.Count; i++)
            {
                var a = SurfacePoint(debug.Grid, surfaces[stretch.WorstPath[i]]) + Vector3.up * 0.2f;
                var b = SurfacePoint(debug.Grid, surfaces[stretch.WorstPath[i + 1]]) + Vector3.up * 0.2f;
                GL.Vertex(a);
                GL.Vertex(b);
            }
            GL.End();
        }

        private static void DrawSurfaces(MeasurementDebug debug)
        {
            var surfaces = debug.Surfaces;
            float maxHeight = Description.Size.y;
            GL.Begin(GL.QUADS);
            for (int s = 0; s < surfaces.Count; s++)
            {
                var surface = surfaces[s];
                var color = HeightColor(surface.Height / maxHeight);
                color.a = surface.HasWalkableRole ? 0.75f : 0.3f;
                CellQuad(debug.Grid, surface.X, surface.Z, surface.Height + Lift, color, 0.85f);
            }
            GL.End();
        }

        private static void DrawMovementGraph(MeasurementDebug debug)
        {
            var surfaces = debug.Surfaces;
            var grid = debug.Grid;
            GL.Begin(GL.LINES);
            foreach (var edge in debug.Graph.Edges)
            {
                if (edge.Kind == MoveKind.Walk && (!WalkEdges || edge.From > edge.To)) continue;
                if (edge.Kind == MoveKind.Jump && edge.From > edge.To) continue;
                var from = SurfacePoint(grid, surfaces[edge.From]);
                var to = SurfacePoint(grid, surfaces[edge.To]);
                GL.Color(edge.Kind switch
                {
                    MoveKind.Walk => new Color(0.6f, 0.6f, 0.6f, 0.5f),
                    MoveKind.Jump => new Color(1f, 0.9f, 0.1f, 1f),
                    _ => new Color(1f, 0.5f, 0.1f, 1f),
                });
                GL.Vertex(from);
                GL.Vertex(to);
                if (edge.Kind == MoveKind.Drop)
                {
                    // A short tick at the landing marks the direction.
                    GL.Vertex(to);
                    GL.Vertex(to + Vector3.up * 0.3f);
                }
            }
            GL.End();

            GL.Begin(GL.QUADS);
            var noStuck = Report.Constraints.Find(c => c.Constraint == Constraint.NoOneGetsStuck);
            if (noStuck != null)
                foreach (int s in noStuck.Surfaces)
                {
                    var surface = surfaces[s];
                    CellQuad(grid, surface.X, surface.Z, surface.Height + 3f * Lift, new Color(1f, 0.1f, 0.1f, 0.6f), 0.9f);
                }
            GL.End();
        }

        /// <summary>Plot outlines on their ground (sub-plots dimmer), and transition segments coloured by type.</summary>
        private static void DrawPlots()
        {
            GL.Begin(GL.LINES);
            for (int r = 0; r < Description.Plots.Count; r++)
            {
                var plot = Description.Plots[r];
                var polygon = Description.PolygonOf(plot);
                var color = PlotColor(r);
                if (plot.ParentId >= 0) color.a = 0.5f;
                GL.Color(color);
                for (int i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Count];
                    GL.Vertex3(a.x, PlotHeight(plot, a) + 0.15f, a.y);
                    GL.Vertex3(b.x, PlotHeight(plot, b) + 0.15f, b.y);
                }
            }

            foreach (var t in Description.Transitions)
            {
                var a = PlotById(t.PlotA);
                var b = PlotById(t.PlotB);
                var mid = (t.From + t.To) * 0.5f;
                float y = Mathf.Max(a != null ? PlotHeight(a, mid) : 0f, b != null ? PlotHeight(b, mid) : 0f) + 0.3f;
                GL.Color(TransitionColor(t.Type));
                // Three lines a little apart, so a transition reads thicker than a plot outline.
                for (int k = 0; k < 3; k++)
                {
                    GL.Vertex3(t.From.x, y + 0.05f * k, t.From.y);
                    GL.Vertex3(t.To.x, y + 0.05f * k, t.To.y);
                }
                if (t.Type == TransitionType.Cliff && a != null && b != null) DrawDropArrow(t, a, b, mid, y);
            }
            GL.End();
        }

        /// <summary>
        /// Chest sites as upright crosses coloured by kind, pickup lines as yellow polylines with a tick per point, and
        /// each room link as a line between plot centroids, white both ways, orange one way (thicker at the end it goes to),
        /// red blocked.
        /// </summary>
        private static void DrawSites()
        {
            GL.Begin(GL.LINES);
            foreach (var site in Description.ChestSites)
            {
                GL.Color(SiteColor(site.Kind));
                var p = site.Position;
                GL.Vertex3(p.x, p.y, p.z);
                GL.Vertex3(p.x, p.y + 1.2f, p.z);
                GL.Vertex3(p.x - 0.4f, p.y + 0.6f, p.z);
                GL.Vertex3(p.x + 0.4f, p.y + 0.6f, p.z);
                GL.Vertex3(p.x, p.y + 0.6f, p.z - 0.4f);
                GL.Vertex3(p.x, p.y + 0.6f, p.z + 0.4f);
            }

            GL.Color(new Color(1f, 0.9f, 0.1f));
            foreach (var line in Description.PickupLines)
                for (int i = 0; i < line.Points.Count; i++)
                {
                    var p = line.Points[i] + Vector3.up * 0.25f;
                    GL.Vertex3(p.x, p.y - 0.25f, p.z);
                    GL.Vertex3(p.x, p.y + 0.25f, p.z);
                    if (i == 0) continue;
                    var q = line.Points[i - 1] + Vector3.up * 0.25f;
                    GL.Vertex3(q.x, q.y, q.z);
                    GL.Vertex3(p.x, p.y, p.z);
                }

            foreach (var link in Description.RoomLinks)
            {
                if (link.RoomA >= Description.Plots.Count || link.RoomB >= Description.Plots.Count) continue;
                var pa = Description.Plots[link.RoomA];
                var pb = Description.Plots[link.RoomB];
                var a = Polygon.Centroid(Description.PolygonOf(pa));
                var b = Polygon.Centroid(Description.PolygonOf(pb));
                float ya = PlotHeight(pa, a) + 2f, yb = PlotHeight(pb, b) + 2f;
                GL.Color(link.AToB && link.BToA ? Color.white : link.AToB || link.BToA ? new Color(1f, 0.55f, 0.1f) : Color.red);
                GL.Vertex3(a.x, ya, a.y);
                GL.Vertex3(b.x, yb, b.y);
                if (link.AToB != link.BToA)
                {
                    // A short post at the end the one-way link goes to.
                    var to = link.AToB ? new Vector3(b.x, yb, b.y) : new Vector3(a.x, ya, a.y);
                    GL.Vertex3(to.x, to.y, to.z);
                    GL.Vertex3(to.x, to.y + 1f, to.z);
                }
            }
            GL.End();
        }

        private static Color SiteColor(ChestSiteKind kind) => kind switch
        {
            ChestSiteKind.SinkholeMiddle => new Color(1f, 0.55f, 0.1f),
            ChestSiteKind.BorderCorner => new Color(0.6f, 0.6f, 1f),
            ChestSiteKind.BuildingGround => new Color(0.2f, 0.9f, 0.9f),
            ChestSiteKind.BuildingUpper => new Color(0.2f, 0.5f, 1f),
            ChestSiteKind.Balcony => new Color(0.3f, 1f, 0.3f),
            ChestSiteKind.Hilltop => new Color(0.9f, 0.3f, 0.9f),
            ChestSiteKind.ValleyBottom => new Color(0.6f, 0.2f, 0.8f),
            ChestSiteKind.BehindFence => new Color(0.8f, 0.6f, 0.3f),
            ChestSiteKind.NearCar => new Color(1f, 0.2f, 0.2f),
            _ => Color.white,
        };

        /// <summary>Stage 5: each sub-room's outline at its corners' heights; sinkholes orange, pinned ones grey.</summary>
        private static void DrawTerrain()
        {
            if (Trace?.Terrain == null) return;
            GL.Begin(GL.LINES);
            foreach (var terrain in Trace.Terrain)
                foreach (var cell in terrain.Cells)
                {
                    GL.Color(cell.Sinkhole ? new Color(1f, 0.55f, 0.1f) : cell.Pinned ? new Color(0.6f, 0.6f, 0.6f, 0.6f) : new Color(1f, 1f, 1f, 0.7f));
                    for (int k = 0; k < cell.Polygon.Count; k++)
                    {
                        var a = cell.Polygon[k];
                        var b = cell.Polygon[(k + 1) % cell.Polygon.Count];
                        GL.Vertex3(a.x, terrain.HeightAt(a + (cell.Site - a) * 0.02f) + 0.1f, a.y);
                        GL.Vertex3(b.x, terrain.HeightAt(b + (cell.Site - b) * 0.02f) + 0.1f, b.y);
                    }
                }
            GL.End();
        }

        /// <summary>
        /// Stage 4, from the trace's plans: each building's spine (lobby and strips) in cyan at spine height, entrances
        /// as white ticks out through the wall, upper floors' outlines, balconies and balcony doors in green, the exterior
        /// flight in blue, the stair core in magenta with its approach, and the footprint of a dropped building in red.
        /// </summary>
        private static void DrawBuildings()
        {
            if (Trace?.Buildings == null) return;
            foreach (var plan in Trace.Buildings)
            {
                if (plan.Footprint == null || plan.Footprint.Count < 3) continue;
                float y = (plan.Placement == FirstFloor.Stilts ? plan.Ground : plan.FirstFloorY) + 0.12f;
                if (plan.Dropped != null)
                {
                    GL.Color(new Color(1f, 0.2f, 0.2f));
                    Outline(plan.Footprint, plan.Ground + 0.2f);
                    continue;
                }
                GL.Color(new Color(0.2f, 0.9f, 1f, 0.9f));
                foreach (var spine in plan.Spine) Outline(spine, y);
                GL.Color(Color.white);
                for (int i = 0; i < plan.Entrances.Count; i++)
                {
                    var a = plan.Entrances[i] - plan.Outward[i] * 0.8f;
                    var b = plan.Entrances[i] + plan.Outward[i] * 1.2f;
                    for (int k = 0; k < 3; k++)
                    {
                        GL.Vertex3(a.x, y + 0.05f * k, a.y);
                        GL.Vertex3(b.x, y + 0.05f * k, b.y);
                    }
                }
                // Upper floors' outlines at their height, balconies and terraces' doors in green, the exterior flight in blue.
                GL.Color(new Color(1f, 1f, 1f, 0.5f));
                for (int k = 1; k < plan.FloorPlans.Count; k++) Outline(plan.FloorPlans[k].Footprint, plan.FloorY(k) + 0.15f);
                GL.Color(new Color(0.3f, 1f, 0.4f));
                foreach (var balcony in plan.Balconies)
                {
                    float by = plan.FloorY(balcony.Floor) + 0.15f;
                    if (!balcony.Terrace) Outline(balcony.Polygon, by);
                    var a = balcony.Door - balcony.Outward * 0.8f;
                    var b = balcony.Door + balcony.Outward * 1.2f;
                    GL.Vertex3(a.x, by, a.y);
                    GL.Vertex3(b.x, by, b.y);
                }
                if (plan.Flight != null)
                {
                    GL.Color(new Color(0.3f, 0.6f, 1f));
                    var f = plan.Flight;
                    var top = f.Low + f.Uphill * f.Run;
                    GL.Vertex3(f.Low.x, f.BottomY + 0.2f, f.Low.y);
                    GL.Vertex3(top.x, f.TopY + 0.2f, top.y);
                    Outline(f.Landing, f.TopY + 0.15f);
                }
                if (!plan.HasCore) continue;
                GL.Color(new Color(1f, 0.3f, 0.9f));
                float coreY = plan.CoreBottom + 0.15f;
                Outline(BuildingStage.CoreRect(plan.Core), coreY);
                Outline(BuildingStage.CoreRect(plan.Core, BuildingStage.CoreApproach), coreY);
            }
        }

        private static void DrawBuildingLabels()
        {
            if (Trace?.Buildings == null) return;
            foreach (var plan in Trace.Buildings)
            {
                if (plan.Footprint == null || plan.Footprint.Count < 3) continue;
                var c = Polygon.Centroid(plan.Footprint);
                string text = plan.Dropped != null ? $"Dropped: {plan.Dropped}"
                    : $"{plan.Placement}, {plan.Floors} floor(s){(plan.HasCore ? ", core" : "")}{(plan.Flight != null ? ", exterior flight" : "")}" +
                      $"{(plan.RoofReachable ? ", roof" : "")}{(plan.FullPlot ? ", full plot" : "")}";
                if (plan.Dropped == null && plan.FloorPlans.Count > 1)
                    text += "\nfloor sizes " + string.Join(" → ", plan.FloorPlans.ConvertAll(f => f.Scale.ToString("0.##")));
                foreach (var note in plan.Notes) text += "\n" + note;
                Handles.Label(new Vector3(c.x, plan.Ground + 4f, c.y), text, EditorStyles.whiteLabel);
            }
        }

        private static void Outline(List<Vector2> polygon, float y)
        {
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k];
                var b = polygon[(k + 1) % polygon.Count];
                GL.Vertex3(a.x, y, a.y);
                GL.Vertex3(b.x, y, b.y);
            }
        }

        /// <summary>A one-way cliff: an arrow across the segment's middle, from the higher plot down to the lower.</summary>
        private static void DrawDropArrow(TransitionDescription t, PlotDescription a, PlotDescription b, Vector2 mid, float y)
        {
            var along = (t.To - t.From).normalized;
            var across = new Vector2(-along.y, along.x);
            var high = PlotHeight(a, mid) >= PlotHeight(b, mid) ? a : b;
            if (Vector2.Dot(Polygon.Centroid(Description.PolygonOf(high)) - mid, across) > 0f) across = -across;
            var tail = mid - across * 1.5f;
            var head = mid + across * 1.5f;
            GL.Vertex3(tail.x, y, tail.y);
            GL.Vertex3(head.x, y, head.y);
            foreach (float side in new[] { -1f, 1f })
            {
                var barb = head - across * 0.6f + along * (0.5f * side);
                GL.Vertex3(head.x, y, head.y);
                GL.Vertex3(barb.x, y, barb.y);
            }
        }

        /// <summary>Stage 2: plot polygons on the ground plane, sites, neighbour links (through shared edge midpoints),
        /// the cut-out cells and the playable outline.</summary>
        private static void DrawPartition(PlotPartition partition)
        {
            const float y = 0.3f;
            GL.Begin(GL.LINES);
            for (int i = 0; i < partition.Polygons.Count; i++)
            {
                var polygon = partition.Polygons[i];
                GL.Color(PlotColor(i));
                for (int k = 0; k < polygon.Count; k++)
                {
                    var a = polygon[k];
                    var b = polygon[(k + 1) % polygon.Count];
                    GL.Vertex3(a.x, y, a.y);
                    GL.Vertex3(b.x, y, b.y);
                }
                var site = partition.Sites[i];
                GL.Vertex3(site.x - 0.5f, y, site.y);
                GL.Vertex3(site.x + 0.5f, y, site.y);
                GL.Vertex3(site.x, y, site.y - 0.5f);
                GL.Vertex3(site.x, y, site.y + 0.5f);
            }
            // Cut cells: grey outline with a cross; the playable outline in white on top.
            GL.Color(new Color(0.55f, 0.55f, 0.55f, 0.8f));
            foreach (var polygon in partition.CutPolygons)
            {
                for (int k = 0; k < polygon.Count; k++)
                {
                    var a = polygon[k];
                    var b = polygon[(k + 1) % polygon.Count];
                    GL.Vertex3(a.x, y, a.y);
                    GL.Vertex3(b.x, y, b.y);
                }
                var c = Polygon.Centroid(polygon);
                GL.Vertex3(c.x - 2f, y, c.y - 2f);
                GL.Vertex3(c.x + 2f, y, c.y + 2f);
                GL.Vertex3(c.x - 2f, y, c.y + 2f);
                GL.Vertex3(c.x + 2f, y, c.y - 2f);
            }
            GL.Color(new Color(1f, 0.85f, 0.2f, 0.7f));
            RectLines(Vector2.zero, new Vector2(Description.Size.x, Description.Size.z), y);
            GL.Color(new Color(1f, 0.85f, 0.2f, 0.3f));
            RectLines(partition.BoxMin, partition.BoxMax, y);
            GL.Color(Color.white);
            foreach (var edge in partition.Outline)
            {
                GL.Vertex3(edge.From.x, y + 0.05f, edge.From.y);
                GL.Vertex3(edge.To.x, y + 0.05f, edge.To.y);
            }
            GL.Color(new Color(1f, 1f, 1f, 0.6f));
            foreach (var edge in partition.Edges)
            {
                var ca = Polygon.Centroid(partition.Polygons[edge.A]);
                var cb = Polygon.Centroid(partition.Polygons[edge.B]);
                var mid = (edge.From + edge.To) * 0.5f;
                GL.Vertex3(ca.x, y, ca.y);
                GL.Vertex3(mid.x, y, mid.y);
                GL.Vertex3(mid.x, y, mid.y);
                GL.Vertex3(cb.x, y, cb.y);
            }
            GL.End();
        }

        private static void RectLines(Vector2 min, Vector2 max, float y)
        {
            GL.Vertex3(min.x, y, min.y); GL.Vertex3(max.x, y, min.y);
            GL.Vertex3(max.x, y, min.y); GL.Vertex3(max.x, y, max.y);
            GL.Vertex3(max.x, y, max.y); GL.Vertex3(min.x, y, max.y);
            GL.Vertex3(min.x, y, max.y); GL.Vertex3(min.x, y, min.y);
        }

        private static void DrawPartitionLabels(PlotPartition partition)
        {
            for (int i = 0; i < partition.Polygons.Count; i++)
            {
                var c = Polygon.Centroid(partition.Polygons[i]);
                Handles.Label(new Vector3(c.x, 1.5f, c.y),
                    $"Plot {i}  w {Polygon.MinWidth(partition.Polygons[i]):0.#}  a {Polygon.Area(partition.Polygons[i]):0}",
                    EditorStyles.whiteLargeLabel);
            }
        }

        private static void DrawPlotLabels()
        {
            var style = EditorStyles.whiteLargeLabel;
            foreach (var plot in Description.Plots)
            {
                var centre = Polygon.Centroid(Description.PolygonOf(plot));
                string slope = plot.Slope == Vector2.zero ? "" : $", slope {Mathf.Atan(plot.Slope.magnitude) * Mathf.Rad2Deg:0.#}°";
                Handles.Label(new Vector3(centre.x, plot.BaseHeight + 2.5f, centre.y),
                    $"Plot {plot.Id}  h {plot.BaseHeight:0.##}{slope}", style);
            }
            foreach (var t in Description.Transitions)
            {
                var mid = (t.From + t.To) * 0.5f;
                var a = PlotById(t.PlotA);
                var b = PlotById(t.PlotB);
                float y = Mathf.Max(a != null ? PlotHeight(a, mid) : 0f, b != null ? PlotHeight(b, mid) : 0f) + 1f;
                Handles.Label(new Vector3(mid.x, y, mid.y), t.Type.ToString(), EditorStyles.miniBoldLabel);
            }
        }

        private static PlotDescription PlotById(int id) => Description.Plots.Find(p => p.Id == id);

        /// <summary>Ground height of a plot at a point, from its base height at the centroid and its slope.</summary>
        private static float PlotHeight(PlotDescription plot, Vector2 p) =>
            plot.BaseHeight + Vector2.Dot(plot.Slope, p - Polygon.Centroid(Description.PolygonOf(plot)));

        // ---------------------------------------------------------------- helpers

        private static void CellQuad(SpanGrid grid, int x, int z, float y, Color color, float scale)
        {
            float half = grid.CellSize * 0.5f * scale;
            float cx = grid.CellCenterX(x), cz = grid.CellCenterZ(z);
            GL.Color(color);
            GL.Vertex3(cx - half, y, cz - half);
            GL.Vertex3(cx - half, y, cz + half);
            GL.Vertex3(cx + half, y, cz + half);
            GL.Vertex3(cx + half, y, cz - half);
        }

        private static Vector3 SurfacePoint(SpanGrid grid, in Surface s) =>
            new(grid.CellCenterX(s.X), s.Height + 0.1f, grid.CellCenterZ(s.Z));

        private static Color HeightColor(float t) => Color.HSVToRGB(Mathf.Lerp(0.66f, 0f, Mathf.Clamp01(t)), 0.85f, 0.95f);

        public static Color PlotColor(int index) => Color.HSVToRGB(index * 0.618034f % 1f, 0.7f, 1f);

        private static Color TransitionColor(TransitionType type) => type switch
        {
            TransitionType.Flat => Color.white,
            TransitionType.Ledge => new Color(0.6f, 1f, 0.6f),
            TransitionType.Cliff => new Color(1f, 0.5f, 0.1f),
            TransitionType.Ramp => new Color(0.45f, 0.7f, 1f),
            TransitionType.Bank => new Color(0.3f, 0.9f, 0.5f),
            _ => new Color(1f, 0.2f, 0.2f),
        };

        private static Material Material
        {
            get
            {
                if (s_material != null) return s_material;
                s_material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                s_material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                s_material.SetInt("_ZWrite", 0);
                s_material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                return s_material;
            }
        }

        private static bool Pref(string key, bool fallback) => EditorPrefs.GetBool("Arena.LevelGen.Overlay." + key, fallback);

        private static bool Toggle(string label, string key, bool value)
        {
            bool now = EditorGUILayout.ToggleLeft(label, value);
            if (now != value) EditorPrefs.SetBool("Arena.LevelGen.Overlay." + key, now);
            return now;
        }
    }
}
