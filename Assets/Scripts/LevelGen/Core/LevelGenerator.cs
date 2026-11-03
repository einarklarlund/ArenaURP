using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>
    /// Entry point: <c>(settings, seed) → GenerationResult</c>. Settings that can't work are refused up front. Otherwise
    /// it runs the pipeline so far: target allocation, partition into plots, plot heights and edge transitions with the
    /// plot-graph check, buildings, terrain, the boundary walls and props, then validates the hard constraints. Buildings are
    /// never dropped (hard constraint 8: they match the drawn mix): a mix that no plots fit, or a building that keeps failing
    /// its local check, stops the attempt; props that could cause a broken constraint are removed and the level validated again. An attempt that stops early or
    /// breaks a constraint is regenerated from the next level-retry sub-seed, within a fixed budget. The first attempt
    /// that passes is the result, so a seed always gives the same level; if none passes there is no level, only the
    /// reason. The passing attempt then gets its spawn points, chest sites, pickup lines and room links.
    /// <see cref="Start"/> runs the same thing a stage at a time.
    /// </summary>
    public static class LevelGenerator
    {
        /// <summary>Bump whenever the output for the same seed and settings changes. v2 starts at 6.</summary>
        public const int GeneratorVersion = 18;

        /// <summary>Whole-level attempts per <c>retries per level</c>.</summary>
        public const int AttemptsPerRetry = 4;

        /// <summary>One whole-level attempt: a level and its hard-constraint check, or why it stopped early.</summary>
        private sealed class Attempt
        {
            public int Index;
            public LevelDescription Level;
            public ValidationResult Check;
            public string Stopped;
            /// <summary>The playable area's outline, for the boundary's chest sites and pickup lines.</summary>
            public List<PlotEdge> Outline;

            public int Failures => Check?.Failures ?? int.MaxValue;

            public string Describe()
            {
                if (Check == null) return $"stopped early: {Stopped}";
                var broken = Check.Constraints.FindAll(c => !c.Passed).ConvertAll(c => $"{c.Constraint}: {c.Detail}");
                return "broke " + string.Join("; ", broken);
            }
        }

        public static GenerationResult Generate(LevelGenSettings settings, ulong seed, GenerationTrace trace = null)
        {
            var run = Start(settings, seed, trace);
            while (run.MoveNext()) { }
            return run.Result;
        }

        /// <summary>
        /// The same generation as <see cref="Generate"/>, a stage at a time: each <see cref="GenerationRun.MoveNext"/>
        /// runs one stage and says which comes next, so a caller on the main thread (WebGL has no other) can let a frame
        /// draw between stages. <see cref="GenerationRun.Result"/> is set once it returns false.
        /// </summary>
        public static GenerationRun Start(LevelGenSettings settings, ulong seed, GenerationTrace trace = null)
        {
            var run = new GenerationRun();
            run.Steps = Run(settings, seed, trace ?? new GenerationTrace(), run);
            return run;
        }

        private static IEnumerator<GenerationProgress> Run(LevelGenSettings settings, ulong seed, GenerationTrace trace,
            GenerationRun run)
        {
            var issues = SettingsCheck.Check(settings);
            if (SettingsCheck.HasErrors(issues))
            {
                var errors = issues.FindAll(i => i.Severity == IssueSeverity.Error).ConvertAll(i => i.Message);
                run.Result = new GenerationResult(null, "The settings can't work. " + string.Join(" ", errors), issues, null, 0);
                yield break;
            }

            settings = settings.Sanitized();
            int attempts = Mathf.Max(1, settings.SearchBudget.RetriesPerLevel) * AttemptsPerRetry;
            Attempt closest = null;
            GenerationTrace closestTrace = null;
            var dropped = new List<string>();
            for (int i = 0; i < attempts; i++)
            {
                ulong stageSeed = i == 0 ? seed : SeedTree.Derive(seed, GenStage.LevelRetry, i);
                var attemptTrace = new GenerationTrace();
                var attempt = new Attempt { Index = i };
                var stages = BuildAttempt(settings, seed, stageSeed, attemptTrace, attempt);
                // The profiler (LEVELGEN_PROFILE only) drives this without pausing between stages, so an attempt is one
                // section there.
                StageProfiler.Begin("Attempt");
                while (stages.MoveNext())
                    yield return new GenerationProgress { Attempt = i + 1, Attempts = attempts, Stage = stages.Current };
                StageProfiler.End();
                if (attempt.Failures == 0)
                {
                    yield return new GenerationProgress { Attempt = i + 1, Attempts = attempts, Stage = SitesStage };
                    var timer = Stopwatch.StartNew();
                    attempt.Level.SpawnPoints.AddRange(SpawnStage.Pick(attempt.Level, attempt.Check, settings));
                    SiteStage.Emit(attempt.Level, attempt.Check, settings, attempt.Outline);
                    Lap(attemptTrace, timer, SitesStage);
                    attemptTrace.LevelAttempts = i + 1;
                    attemptTrace.DroppedAttempts = dropped;
                    attemptTrace.CopyTo(trace);
                    run.Result = new GenerationResult(attempt.Level, null, issues, null, i + 1);
                    yield break;
                }
                dropped.Add($"Attempt {i + 1} {attempt.Describe()}");
                if (closest == null || attempt.Failures < closest.Failures)
                {
                    closest = attempt;
                    closestTrace = attemptTrace;
                }
            }

            // The trace keeps the closest attempt, for the editor's stage views; the level itself is not handed out.
            closestTrace.LevelAttempts = attempts;
            closestTrace.DroppedAttempts = dropped;
            closestTrace.CopyTo(trace);
            var failures = closest.Check?.Constraints.FindAll(c => !c.Passed);
            run.Result = new GenerationResult(null,
                $"Seed {seed}: all {attempts} level attempts failed. The closest, attempt {closest.Index + 1}, {closest.Describe()}.",
                issues, failures, attempts);
        }

        /// <summary>The last stage's name: spawn points, chest sites, pickup lines and room links.</summary>
        public const string SitesStage = "Spawn sites";

        /// <summary>Builds one attempt into <paramref name="attempt"/>, yielding each stage's name before running it.</summary>
        private static IEnumerator<string> BuildAttempt(LevelGenSettings s, ulong seed, ulong stageSeed, GenerationTrace trace,
            Attempt attempt)
        {
            yield return "Allocate targets";
            var timer = Stopwatch.StartNew();
            var targets = trace.Targets = AllocateTargets(s, stageSeed);
            Lap(trace, timer, "Allocate targets");

            yield return "Partition";
            timer.Restart();
            var partition = trace.Partition = PartitionStage.Build(s, stageSeed, out string failure);
            Lap(trace, timer, "Partition");
            if (partition == null)
            {
                attempt.Stopped = failure;
                yield break;
            }
            attempt.Outline = partition.Outline;

            yield return "Heights & transitions";
            timer.Restart();
            var plan = trace.Plots = TransitionStage.Build(partition, targets, s, stageSeed);
            Lap(trace, timer, "Heights & transitions");
            if (plan.Failure != null)
            {
                attempt.Stopped = plan.Failure;
                yield break;
            }
            trace.Repairs.AddRange(plan.Repairs);
            if (plan.Mix == BuildingMix.None)
            {
                attempt.Stopped = "no building mix fits the plots";
                yield break;
            }

            yield return "Buildings";
            timer.Restart();
            var buildings = trace.Buildings = BuildingStage.Build(partition, plan, s, stageSeed);
            Lap(trace, timer, "Buildings");
            var failed = buildings.Find(b => b.Dropped != null);
            if (failed != null)
            {
                attempt.Stopped = $"the {failed.Class} building on plot {failed.Plot} {failed.Dropped}";
                yield break;
            }

            yield return "Terrain";
            timer.Restart();
            var terrain = trace.Terrain = TerrainStage.Build(partition, plan, s, stageSeed);
            var terrained = new HashSet<int>();
            var terrainByPlot = new Dictionary<int, TerrainPlan>();
            foreach (var t in terrain)
            {
                terrained.Add(t.Plot);
                terrainByPlot[t.Plot] = t;
            }
            Lap(trace, timer, "Terrain");

            yield return "Props";
            timer.Restart();
            var ground = new GroundSurface(plan, terrainByPlot);
            var props = trace.Props = PropStage.Build(partition, plan, buildings, ground, s, stageSeed, terrain);
            Lap(trace, timer, "Props");

            yield return "Geometry & validate";
            timer.Restart();
            LevelDescription level = null;
            ValidationResult check = null;
            for (int round = 0; round <= 4; round++)
            {
                level = new LevelDescription
                {
                    GeneratorVersion = GeneratorVersion,
                    Seed = seed,
                    ConfigHash = s.ComputeHash(),
                    Size = new Vector3(s.Width, s.MaxHeight, s.Depth),
                    Mix = plan.Mix,
                };
                TransitionStage.Emit(level, partition, plan, targets, s, terrained);
                TerrainStage.Emit(level, terrain, partition);
                TransitionStage.EmitSlopes(level, partition, plan, s, ground);
                BuildingStage.Emit(level, buildings, s);
                BoundaryStage.Emit(level, s.Boundary, partition.Outline);
                PropStage.Emit(level, props);
                // No overlap check: the stages build geometry that doesn't overlap (see the design's "No overlapping
                // geometry"), and the tests hold them to it.
                check = LevelEvaluator.Validate(level, s, overlaps: false);
                if (check.Failures == 0) break;

                // Remove the props that could be the cause: those an overlap or a primitive out of bounds names, and those
                // near a failing surface or a point a failure names (cells short of headroom). A failure that names no
                // place, or none near a prop, is not the props' doing: they stay, and the attempt fails as it stands.
                // Buildings stay: one that breaks a constraint fails the attempt.
                bool dropped = false;
                if (props.Count > 0)
                {
                    int before = props.Count;
                    var grid = check.Surfaces.Grid;
                    var doomed = new HashSet<PropPlan>();
                    foreach (var c in check.Constraints)
                    {
                        if (c.Passed) continue;
                        if (c.Constraint is Constraint.NoOverlap or Constraint.InsideBounds)
                            foreach (int item in c.Items)
                                if (level.Primitives[item].OwnerKind == OwnerKind.Prop) doomed.Add(props[level.Primitives[item].OwnerId]);
                        var places = new List<Vector2>(c.Where);
                        foreach (int surface in c.Surfaces)
                            places.Add(new Vector2(grid.CellCenterX(check.Surfaces[surface].X), grid.CellCenterZ(check.Surfaces[surface].Z)));
                        foreach (var at in places)
                            foreach (var p in props)
                                if (!PropKinds.IsFixture(p.Kind) && (p.Centre - at).magnitude < p.Length * 0.5f + 4f) doomed.Add(p);
                    }
                    props.RemoveAll(doomed.Contains);
                    if (props.Count < before)
                    {
                        var broken = check.Constraints.FindAll(c => !c.Passed).ConvertAll(c => c.Constraint.ToString());
                        trace.Repairs.Add($"{before - props.Count} prop(s) removed near a broken constraint ({string.Join(", ", broken)})");
                        dropped = true;
                    }
                }
                if (!dropped) break;
            }
            Lap(trace, timer, "Geometry & validate");
            attempt.Level = level;
            attempt.Check = check;
        }

        private static PlotTargets[] AllocateTargets(in LevelGenSettings s, ulong seed)
        {
            var rng = SeedTree.Stream(seed, GenStage.Allocation);
            int n = s.PlotCount;
            var range = TargetAllocation.Allocate(s.ElevationRange, n, ref rng);
            var frequency = TargetAllocation.Allocate(s.ElevationFrequency, n, ref rng);
            var targets = new PlotTargets[n];
            for (int i = 0; i < n; i++)
                targets[i] = new PlotTargets { ElevationRange = range[i], ElevationFrequency = frequency[i] };
            return targets;
        }

        private static void Lap(GenerationTrace trace, Stopwatch timer, string stage)
        {
            trace.Timings.Add(new StageTiming { Stage = stage, Milliseconds = timer.Elapsed.TotalMilliseconds });
            StageProfiler.Record(stage, timer.Elapsed.TotalMilliseconds);
            timer.Restart();
        }
    }
}

namespace Arena.LevelGen
{
    /// <summary>The stage a stepped generation is about to run, and which whole-level attempt it is in.</summary>
    public struct GenerationProgress
    {
        /// <summary>1-based.</summary>
        public int Attempt;
        public int Attempts;
        public string Stage;
    }

    /// <summary>A generation in progress (<see cref="LevelGenerator.Start"/>). Not thread-safe; drive it from one thread.</summary>
    public sealed class GenerationRun
    {
        internal IEnumerator<GenerationProgress> Steps;

        /// <summary>The stage the last <see cref="MoveNext"/> said comes next.</summary>
        public GenerationProgress Current => Steps.Current;

        /// <summary>Set once <see cref="MoveNext"/> has returned false.</summary>
        public GenerationResult Result { get; internal set; }

        public bool Done => Result != null;

        /// <summary>Runs the next stage. False once generation is over and <see cref="Result"/> is set.</summary>
        public bool MoveNext() => Result == null && Steps.MoveNext();
    }
}
