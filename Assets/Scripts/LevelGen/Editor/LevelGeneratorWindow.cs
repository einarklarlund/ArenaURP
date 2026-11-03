using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>
    /// Generates a level from a config and seed into a temporary preview root in the open scene, measures it,
    /// and shows the metrics report and scene overlays. The root is never saved and is replaced on each run.
    /// </summary>
    public class LevelGeneratorWindow : EditorWindow
    {
        private const string PreviewRootName = "[LevelGen Preview]";

        private static readonly string[] PlotColumns = { "Elev range", "Elev freq" };
        private static readonly string[] LevelColumns = { "Path stretch", "Elev range" };

        [SerializeField] private LevelGenConfig config;
        [SerializeField] private LevelTheme theme;
        [SerializeField] private bool showDecals = true;
        [SerializeField] private string seedText = "1";
        [SerializeField] private int stretchStride = 8;

        private static readonly string[] StageNames = { "Partition", "Heights & transitions", "Buildings & terrain", "Final" };

        private LevelDescription _lastDescription;
        private GenerationTrace _trace;
        private GenerationReport _report;
        private string _status = "";
        private MessageType _statusType = MessageType.None;
        private string _stats = "";
        private Vector2 _scroll;
        private bool _showOverlays = true;

        [MenuItem("Window/Arena/Level Generator")]
        public static void Open() => GetWindow<LevelGeneratorWindow>("Level Generator");

        private void OnEnable() => LevelGenOverlay.Enable();

        private void OnDisable() => LevelGenOverlay.Disable();

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUI.BeginChangeCheck();
            config = (LevelGenConfig)EditorGUILayout.ObjectField("Config", config, typeof(LevelGenConfig), false);
            if (EditorGUI.EndChangeCheck() && config != null) seedText = config.seed.ToString();
            if (config == null)
                EditorGUILayout.HelpBox("No config: generating with LevelGenSettings.Default.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            theme = (LevelTheme)EditorGUILayout.ObjectField(
                new GUIContent("Theme", "Materials only: changing it rebuilds the preview without regenerating."), theme,
                typeof(LevelTheme), false);
            if (EditorGUI.EndChangeCheck()) Rebuild();
            EditorGUI.BeginChangeCheck();
            showDecals = EditorGUILayout.Toggle(new GUIContent("Decals", "Build the decal strips. Rebuilds without regenerating."),
                showDecals);
            if (EditorGUI.EndChangeCheck()) Rebuild();

            using (new EditorGUILayout.HorizontalScope())
            {
                seedText = EditorGUILayout.TextField("Seed", seedText);
                if (GUILayout.Button("Randomize", GUILayout.Width(80)))
                {
                    seedText = RandomSeed().ToString();
                    GUI.FocusControl(null);
                }
            }
            stretchStride = Mathf.Max(1, EditorGUILayout.IntField(
                new GUIContent("Path stretch stride", "Sample every k-th cell for path stretch. Lower is slower and finer."),
                stretchStride));
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28))) Generate();
                if (GUILayout.Button("Check Determinism", GUILayout.Height(28))) CheckDeterminism();
                if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(28))) ClearPreview();
            }

            if (_trace?.Partition != null)
            {
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                var stage = (StageView)GUILayout.Toolbar((int)LevelGenOverlay.Stage, StageNames);
                if (EditorGUI.EndChangeCheck()) ShowStage(stage);
                EditorGUILayout.LabelField(StageCaption(LevelGenOverlay.Stage), EditorStyles.wordWrappedMiniLabel);
            }

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, _statusType);
            if (!string.IsNullOrEmpty(_stats))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last generation", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(_stats, EditorStyles.textArea, GUILayout.Height(80));
            }

            EditorGUILayout.Space();
            _showOverlays = EditorGUILayout.Foldout(_showOverlays, "Scene overlays", true);
            if (_showOverlays)
                using (new EditorGUI.IndentLevelScope())
                    LevelGenOverlay.DrawToggles();

            if (_report != null) DrawReport(_report);

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------------- actions

        private LevelGenSettings Settings => config != null ? config.ToSettings() : LevelGenSettings.Default.Sanitized();

        private bool TryParseSeed(out ulong seed)
        {
            if (ulong.TryParse(seedText.Trim(), out seed)) return true;
            SetStatus($"Seed must be a whole number from 0 to {ulong.MaxValue}.", MessageType.Error);
            return false;
        }

        private void Generate()
        {
            if (!TryParseSeed(out ulong seed)) return;
            var settings = Settings;

            var timer = Stopwatch.StartNew();
            var trace = new GenerationTrace();
            var result = LevelGenerator.Generate(settings, seed, trace);
            if (!result.Success)
            {
                // Nothing to preview: a level that breaks a hard constraint is never built.
                ClearPreview();
                _lastDescription = null;
                _report = null;
                _trace = null;
                _stats = "";
                LevelGenOverlay.Trace = null;
                LevelGenOverlay.Description = null;
                LevelGenOverlay.Report = null;
                SetStatus(result.Reason, MessageType.Error);
                SceneView.RepaintAll();
                return;
            }
            var description = result.Level;

            var options = MeasureOptions.Default;
            options.PathStretchStride = stretchStride;
            options.CollectDebug = true;
            var report = LevelEvaluator.Evaluate(description, settings, options);
            report.Timings.InsertRange(0, trace.Timings);
            report.Retries = trace.Retries;
            report.Repairs = trace.Repairs.Count;

            timer.Restart();
            var root = BuildPreview(description);
            report.Timings.Add(new StageTiming { Stage = "Build", Milliseconds = timer.Elapsed.TotalMilliseconds });

            _lastDescription = description;
            _report = report;
            _trace = trace;
            LevelGenOverlay.Trace = trace;
            LevelGenOverlay.Description = description;
            LevelGenOverlay.Report = report;

            var grid = report.Debug.Grid;
            _stats =
                $"Seed {seed}   generator v{description.GeneratorVersion}\n" +
                $"Description hash  {description.ComputeHash():X16}\n" +
                $"Config hash       {description.ConfigHash:X16}\n" +
                $"Plots {description.Plots.Count}   transitions {description.Transitions.Count}   primitives {description.Primitives.Count}   polygons {description.Polygons.Count}   {description.Serialize().Length / 1024f:0.#} KB\n" +
                $"Span grid {grid.CellsX}×{grid.CellsZ} @ {grid.CellSize}   spans {grid.CountSpans()}   surfaces {report.Debug.Surfaces.Count}   moves {report.Debug.Graph.Edges.Count}";
            var warnings = result.SettingsIssues.ConvertAll(i => i.Message);
            if (warnings.Count == 0) SetStatus($"Generated into '{PreviewRootName}' (not saved with the scene).", MessageType.None);
            else SetStatus($"Generated into '{PreviewRootName}' (not saved with the scene). Settings: {string.Join(" ", warnings)}", MessageType.Warning);
            ShowStage(LevelGenOverlay.Stage);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            SceneView.RepaintAll();
        }

        /// <summary>Replaces the preview with a build of <paramref name="description"/> in the current theme.</summary>
        private GameObject BuildPreview(LevelDescription description)
        {
            ClearPreview();
            var root = LevelBuilder.Build(description, null, PreviewRootName, theme, config != null ? config.props : null,
                showDecals);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
            return root;
        }

        /// <summary>Rebuilds the last level in the current theme, keeping the stage view. Nothing is regenerated.</summary>
        private void Rebuild()
        {
            if (_lastDescription == null) return;
            BuildPreview(_lastDescription);
            ShowStage(LevelGenOverlay.Stage);
        }

        private void CheckDeterminism()
        {
            if (!TryParseSeed(out ulong seed)) return;
            var settings = Settings;
            const int runs = 5;
            var firstResult = LevelGenerator.Generate(settings, seed);
            if (!firstResult.Success)
            {
                SetStatus(firstResult.Reason, MessageType.Error);
                return;
            }
            ulong first = firstResult.Level.ComputeHash();
            for (int i = 1; i < runs; i++)
            {
                var nextResult = LevelGenerator.Generate(settings, seed);
                ulong next = nextResult.Success ? nextResult.Level.ComputeHash() : 0UL;
                if (next != first)
                {
                    SetStatus($"Run {i + 1} hashed {next:X16}, run 1 hashed {first:X16}: generation is not deterministic.",
                        MessageType.Error);
                    return;
                }
            }
            string vsPreview = _lastDescription != null && _lastDescription.Seed == seed
                ? (_lastDescription.ComputeHash() == first ? " It matches the preview." : " It differs from the preview (config changed?).")
                : "";
            SetStatus($"{runs} runs of seed {seed} all hashed {first:X16}.{vsPreview}", MessageType.Info);
        }

        /// <summary>Shows one stage's output. The built geometry is hidden for the partition stage, buildings before
        /// the buildings stage, and props and decals before the final view.</summary>
        private void ShowStage(StageView stage)
        {
            LevelGenOverlay.Stage = stage;
            var root = FindPreviewRoot();
            if (root != null)
            {
                root.SetActive(stage != StageView.Partition);
                foreach (Transform child in root.transform)
                {
                    if (child.name.StartsWith("Building")) child.gameObject.SetActive(stage >= StageView.Buildings);
                    if (child.name.StartsWith("Prop") || child.name.StartsWith("Light"))
                        child.gameObject.SetActive(stage >= StageView.Final);
                    if (child.name == "Decals") child.gameObject.SetActive(stage >= StageView.Final);
                }
            }
            SceneView.RepaintAll();
        }

        private string StageCaption(StageView stage)
        {
            var partition = _trace.Partition;
            var plots = _trace.Plots;
            string attempts = _trace.LevelAttempts > 1
                ? $" Level attempts: {_trace.LevelAttempts}. {string.Join(" ", _trace.DroppedAttempts)}"
                : "";
            switch (stage)
            {
                case StageView.Partition:
                    return $"Power diagram of {partition.Sites.Count + partition.CutPolygons.Count} sites drawn in a box padded past " +
                        "the level (yellow), relaxed. Only cells wholly inside the level become plots (crosses mark their " +
                        "sites); the rest are cut (grey), leaving the playable outline (white). " +
                        $"White links join neighbours sharing at least the min shared edge ({partition.Edges.Count} pairs). " +
                        $"{partition.Attempts} draw(s).";
                case StageView.Transitions:
                    if (plots == null) return "No heights: the attempt stopped before them." + attempts;
                    var counts = new int[6];
                    foreach (var segment in plots.Segments) counts[(int)segment.Type]++;
                    return $"Segments: {counts[0]} flat (white), {counts[1]} ledge (light green), {counts[2]} cliff " +
                        $"(orange, arrow points down), {counts[3]} ramp (blue), {counts[4]} bank (green), {counts[5]} blocked (red). " +
                        $"Building plots kept flat: {(plots.BuildingPlots.Count == 0 ? "none" : string.Join(", ", plots.BuildingPlots))}. " +
                        "Plot-graph repairs: " + (plots.Repairs.Count == 0 ? "none." : string.Join("; ", plots.Repairs)) +
                        (plots.Failure != null ? $" Failed: {plots.Failure}." : "") + attempts;
                case StageView.Buildings:
                    if (_trace.Buildings == null) return "No buildings: the attempt stopped before them." + attempts;
                    int built = _trace.Buildings.FindAll(b => b.Dropped == null).Count;
                    return TerrainCaption() + $" {built} of {_trace.Buildings.Count} building(s) built. Spines in cyan (lobby and a strip to each " +
                        "entrance), entrances as white ticks, upper floors' outlines in white, balconies and their doors in " +
                        "green, exterior flights in blue, stair cores in magenta with their clear approach; dropped " +
                        "footprints in red. Labels give placement, floors, floor sizes and what was cut back." + attempts;
                default:
                    return $"Built level with {(_trace.Props?.Count ?? 0)} props and the overlays chosen below." + attempts;
            }
        }

        private string TerrainCaption()
        {
            if (_trace.Terrain == null) return "";
            int cells = 0, undone = 0;
            var counts = new int[5];
            foreach (var t in _trace.Terrain)
            {
                cells += t.Cells.Count;
                undone += t.Undone;
                foreach (var op in t.Applied) counts[(int)op]++;
            }
            return $"Terrain: {cells} sub-rooms in {_trace.Terrain.Count} rooms (white outlines; sinkholes in orange, " +
                $"pinned in grey). Operations: {counts[0]} raise, {counts[1]} tilt, {counts[2]} raise and tilt, " +
                $"{counts[3]} sinkhole, {counts[4]} nothing; {undone} undone for being too steep or high.";
        }

        /// <summary>
        /// Every preview root, in any scene or none. A preview is DontSave, so when the scene it was built into is
        /// unloaded Unity keeps it alive outside any scene, still drawn; searching the loaded scenes alone would miss it.
        /// </summary>
        private static List<GameObject> PreviewRoots()
        {
            var roots = new List<GameObject>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == PreviewRootName && go.transform.parent == null && !EditorUtility.IsPersistent(go))
                    roots.Add(go);
            return roots;
        }

        private static GameObject FindPreviewRoot()
        {
            var roots = PreviewRoots();
            return roots.Count > 0 ? roots[0] : null;
        }

        private void ClearPreview()
        {
            foreach (var go in PreviewRoots()) LevelBuilder.Teardown(go);
        }

        // ---------------------------------------------------------------- report

        private void DrawReport(GenerationReport report)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Metrics report", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Cells show measured / target. Green is within tolerance.", EditorStyles.miniLabel);

            HeaderRow("Plot", PlotColumns);
            foreach (var plot in report.Plots)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string subPlots = plot.SubPlotIds.Count == 0 ? "" : $"Sub-plots: {string.Join(", ", plot.SubPlotIds)}";
                    EditorGUILayout.LabelField(new GUIContent(
                            plot.SubPlotIds.Count == 0 ? $"Plot {plot.PlotId}" : $"Plot {plot.PlotId} (+{plot.SubPlotIds.Count})",
                            subPlots), GUILayout.Width(FirstColumnWidth));
                    foreach (var m in plot.Metrics) MetricCell(m, false);
                    LabelCell(plot.Error.ToString("F3"));
                }
            }

            EditorGUILayout.Space();
            HeaderRow("Level", LevelColumns);
            using (new EditorGUILayout.HorizontalScope())
            {
                LabelCell("Level", FirstColumnWidth);
                for (int i = 0; i < report.Level.Length; i++)
                    MetricCell(report.Level[i], false);
                LabelCell(report.LevelError.ToString("F3"));
            }

            var stretch = report.PathStretch;
            EditorGUILayout.LabelField(
                $"Path stretch: mean {stretch.Mean:F3}   p95 {stretch.P95:F3}   worst {stretch.Worst:F3}   " +
                $"{stretch.Samples.Count} samples, {stretch.Pairs} pairs" +
                (stretch.Unreachable > 0 ? $", {stretch.Unreachable} unreachable" : ""), EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Total error {report.TotalError:F3}    retries {report.Retries}    repairs {report.Repairs}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Constraints", EditorStyles.boldLabel);
            foreach (var c in report.Constraints)
            {
                var style = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    normal = { textColor = c.Passed ? new Color(0.35f, 0.8f, 0.4f) : new Color(1f, 0.4f, 0.35f) },
                };
                EditorGUILayout.LabelField($"{(c.Passed ? "PASS" : "FAIL")}  {(int)c.Constraint}. {Nicify(c.Constraint)}: {c.Detail}", style);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Timings", EditorStyles.boldLabel);
            double total = 0;
            foreach (var t in report.Timings)
            {
                EditorGUILayout.LabelField(t.Stage, $"{t.Milliseconds:F1} ms");
                total += t.Milliseconds;
            }
            EditorGUILayout.LabelField("Total", $"{total:F1} ms");
        }

        private const float FirstColumnWidth = 80f;
        private const float CellWidth = 84f;

        private static void HeaderRow(string first, string[] columns)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(first, EditorStyles.miniBoldLabel, GUILayout.Width(FirstColumnWidth));
                foreach (var c in columns) EditorGUILayout.LabelField(c, EditorStyles.miniBoldLabel, GUILayout.Width(CellWidth));
                EditorGUILayout.LabelField("Error", EditorStyles.miniBoldLabel, GUILayout.Width(CellWidth));
            }
        }

        private static void LabelCell(string text, float width = CellWidth) =>
            EditorGUILayout.LabelField(text, GUILayout.Width(width));

        private static void MetricCell(MetricResult m, bool integer)
        {
            var rect = GUILayoutUtility.GetRect(CellWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(CellWidth));
            var tint = m.WithinTolerance ? new Color(0.2f, 0.6f, 0.25f, 0.45f) : new Color(0.75f, 0.2f, 0.15f, 0.45f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, rect.width - 4, rect.height - 2), tint);
            string text = integer ? $"{m.Measured:F0} / {m.Target:F0}" : $"{m.Measured:F2} / {m.Target:F2}";
            EditorGUI.LabelField(rect, new GUIContent(text, $"error {m.Error:F3}  (tolerance {m.Tolerance:F2}, weight {m.Weight:F2})"),
                EditorStyles.miniLabel);
        }

        private static string Nicify(Constraint c) => ObjectNames.NicifyVariableName(c.ToString());

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
            Repaint();
        }

        private static ulong RandomSeed()
        {
            // Editor convenience only; generation itself never touches this.
            var bytes = System.Guid.NewGuid().ToByteArray();
            return System.BitConverter.ToUInt64(bytes, 0);
        }
    }
}
