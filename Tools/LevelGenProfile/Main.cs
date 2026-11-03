using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arena.LevelGen;
using UnityEngine;

/// <summary>
/// Generates seeds 0..n-1 on every preset, timing each level, each attempt and each stage, and hashes every level's
/// description bytes and build plan. Options: --seeds N, --presets a,b, --hashes FILE (write), --compare FILE (check),
/// --report FILE (markdown). --overlaps runs the overlap check on every generated level instead; --dump PRESET SEED N
/// [OWNER] prints the first N overlaps a check finds (optionally only those involving an owner kind).
/// </summary>
public static class Program
{
    private static readonly string[] PresetNames = { "default", "small", "dense", "tall", "small-full", "asset" };

    /// <summary>GuaranteeTests' presets 0–4, then the values in Assets/Level Gen/LevelGenConfig.asset.</summary>
    private static LevelGenSettings Preset(int preset)
    {
        var s = LevelGenSettings.Default;
        switch (preset)
        {
            case 1:
                s.Width = s.Depth = 50f;
                s.PlotCount = 4;
                break;
            case 2:
                s.Width = s.Depth = 100f;
                s.PlotCount = 8;
                break;
            case 3:
                s.MaxHeight = 20f;
                s.LevelElevationRange.Target = 0.36f;
                s.ElevationRange.Target = 0.24f;
                s.Buildings.MinFloors = 3;
                s.Buildings.MaxFloors = 3;
                break;
            case 4:
                s.Width = s.Depth = 50f;
                s.PlotCount = 4;
                s.Buildings.FullPlotOdds = 1f;
                break;
            case 5:
                s.Width = s.Depth = 100f;
                s.TransitionSizes.CurveFraction = 0.6f;
                s.TransitionSizes.CurveSegments = 6;
                s.Terrain.MinOperations = 25;
                s.Terrain.MaxOperations = 40;
                s.Terrain.MinHeightChange = 1.5f;
                s.Terrain.MaxHeightChange = 3f;
                s.Terrain.MinTiltDegrees = 30f;
                s.Terrain.MaxTiltDegrees = 60f;
                s.Terrain.MaxGradeDegrees = 120f;
                s.Terrain.SinkholeDepth = 1.4f;
                s.Props.FencedRoomOdds = 0.209f;
                s.Props.Fences = new[]
                {
                    new PropShape { Weight = 1f, Length = 2f, Width = 0.12f, Height = 1.2f },
                    new PropShape { Weight = 1f, Length = 3f, Width = 0.3f, Height = 1f },
                };
                s.Props.Cars = new[]
                {
                    new PropShape { Weight = 1f, Length = 4.4f, Width = 1.8f, Height = 1.6f },
                    new PropShape { Weight = 1f, Length = 5f, Width = 2f, Height = 2.3f },
                };
                s.Props.Trees = new[]
                {
                    new PropShape
                    {
                        Weight = 1f, Length = 0.4f, Width = 0.4f, Height = 3.2f,
                        CanopyLength = 2.8f, CanopyWidth = 2.8f, CanopyBottom = 3.2999992f, CanopyHeight = 2.6000004f,
                    },
                    new PropShape
                    {
                        Weight = 1f, Length = 0.35f, Width = 0.35f, Height = 3.4f,
                        CanopyLength = 3.11127f, CanopyWidth = 3.11127f, CanopyBottom = 3.4f, CanopyHeight = 2.5f,
                    },
                };
                break;
        }
        return s;
    }

    private sealed class LevelRun
    {
        public ulong Seed;
        public bool Success;
        public double Ms, PlanMs, StretchMs;
        public int Attempts, Primitives;
        public string Hash, PlanHash;
        public List<string> Dropped = new();
        public List<double> AttemptMs = new();
        public Dictionary<string, double> Stages = new();
        public Dictionary<OwnerKind, int> ByOwner = new();
    }

    public static int Main(string[] args)
    {
        int seeds = 30;
        var presets = Enumerable.Range(0, PresetNames.Length).ToList();
        string hashesOut = null, compare = null, reportOut = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seeds": seeds = int.Parse(args[++i]); break;
                case "--presets": presets = args[++i].Split(',').Select(int.Parse).ToList(); break;
                case "--hashes": hashesOut = args[++i]; break;
                case "--compare": compare = args[++i]; break;
                case "--report": reportOut = args[++i]; break;
                case "--overlaps": return Overlaps(seeds, presets);
                case "--dump": return Dump(int.Parse(args[++i]), ulong.Parse(args[++i]), int.Parse(args[++i]), args.Length > i + 1 ? args[++i] : null);
            }
        }

        // Warm the JIT so the first level isn't timed with it.
        LevelGenerator.Generate(Preset(1), 1000UL);

        var all = new Dictionary<int, List<LevelRun>>();
        var hashLines = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (int preset in presets)
        {
            var s = Preset(preset);
            var runs = all[preset] = new List<LevelRun>();
            for (ulong seed = 0; seed < (ulong)seeds; seed++)
            {
                runs.Add(Run(s, seed));
                var r = runs[runs.Count - 1];
                hashLines.Add($"{PresetNames[preset]} {seed} {r.Hash} {r.PlanHash}");
                Console.Error.Write(".");
            }
            Console.Error.WriteLine($" {PresetNames[preset]}");
        }

        var report = Report(all, seeds);
        Console.WriteLine(report);
        Console.WriteLine($"Total {total.Elapsed.TotalSeconds:F1} s");
        if (reportOut != null) File.WriteAllText(reportOut, report);
        if (hashesOut != null) File.WriteAllLines(hashesOut, hashLines);
        if (compare != null)
        {
            var expected = File.ReadAllLines(compare);
            var expectedByKey = expected.ToDictionary(l => string.Join(" ", l.Split(' ').Take(2)), l => l);
            int mismatches = 0, compared = 0;
            foreach (var line in hashLines)
            {
                var key = string.Join(" ", line.Split(' ').Take(2));
                if (!expectedByKey.TryGetValue(key, out var want)) continue;
                compared++;
                if (want != line)
                {
                    mismatches++;
                    Console.WriteLine($"MISMATCH {key}: was {want}, now {line}");
                }
            }
            Console.WriteLine(mismatches == 0 ? $"HASHES IDENTICAL ({compared} levels)" : $"HASHES DIFFER: {mismatches} of {compared}");
            return mismatches == 0 ? 0 : 1;
        }
        return 0;
    }

    private static LevelRun Run(LevelGenSettings s, ulong seed)
    {
        var run = new LevelRun { Seed = seed };
        StageProfiler.Reset();
        var trace = new GenerationTrace();
        var timer = Stopwatch.StartNew();
        var result = LevelGenerator.Generate(s, seed, trace);
        run.Ms = timer.Elapsed.TotalMilliseconds;
        run.Success = result.Success;
        run.Attempts = result.Attempts;
        run.Dropped.AddRange(trace.DroppedAttempts);
        if (StageProfiler.Sections.TryGetValue("Attempt", out var attempts)) run.AttemptMs.AddRange(attempts.Samples);

        if (result.Success)
        {
            var level = result.Level;
            run.Hash = Hash(level.Serialize());
            run.Primitives = level.Primitives.Count;
            foreach (var p in level.Primitives)
                run.ByOwner[p.OwnerKind] = run.ByOwner.TryGetValue(p.OwnerKind, out int n) ? n + 1 : 1;

            timer.Restart();
            StageProfiler.Begin("Build plan");
            var plan = BuildPlan.Create(level, new BuildOptions { BlendedGround = true });
            StageProfiler.End();
            run.PlanMs = timer.Elapsed.TotalMilliseconds;
            run.PlanHash = PlanHash(plan);

            var evaluation = LevelEvaluator.Evaluate(level, s, MeasureOptions.Default);
            run.StretchMs = evaluation.Timings.Find(t => t.Stage == "Path stretch").Milliseconds;
        }
        else
        {
            run.Hash = "FAIL";
            run.PlanHash = "-";
        }
        foreach (var kv in StageProfiler.Sections) run.Stages[kv.Key] = kv.Value.Milliseconds;
        run.Stages["Path stretch (evaluate only)"] = run.StretchMs;
        return run;
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes), 0, 12).Replace("-", "");
    }

    private static string PlanHash(BuildPlan plan)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        void V3(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        void Mesh(PlanMesh m)
        {
            if (m == null) { w.Write(-1); return; }
            w.Write(m.Vertices.Count);
            foreach (var v in m.Vertices) V3(v);
            foreach (var v in m.Normals) V3(v);
            foreach (var v in m.Uvs) { w.Write(v.x); w.Write(v.y); }
            foreach (var t in m.Triangles) w.Write(t);
            foreach (var k in m.Keys) w.Write(k.GetHashCode());
            foreach (var t in m.TriangleKeys) w.Write(t);
            if (m.LayerIds != null) foreach (var v in m.LayerIds) V3(v);
            if (m.LayerWeights != null) foreach (var v in m.LayerWeights) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
        }
        foreach (var g in plan.Groups)
        {
            w.Write((byte)g.Kind); w.Write(g.Id); w.Write(g.Prefab != null); w.Write(g.Profile); w.Write(g.CastShadows);
            Mesh(g.Mesh);
            if (g.CollisionVertices != null) foreach (var v in g.CollisionVertices) V3(v);
            if (g.CollisionTriangles != null) foreach (var t in g.CollisionTriangles) w.Write(t);
        }
        foreach (var f in plan.Fixtures) { w.Write(f.Prefab); Mesh(f.Placeholder); }
        foreach (var l in plan.RealLights) w.Write(l);
        if (plan.Decals != null)
            foreach (var d in plan.Decals)
            {
                w.Write((int)d.Type); w.Write(d.Building); V3(d.Normal); w.Write(d.Width); w.Write(d.V0); w.Write(d.V1);
                foreach (var p in d.Points) V3(p);
                foreach (var p in d.Left) V3(p);
                foreach (var p in d.Right) V3(p);
            }
        if (plan.DecalProfiles != null) foreach (var p in plan.DecalProfiles) w.Write(p);
        w.Flush();
        return Hash(ms.ToArray());
    }

    private static double Pct(List<double> xs, double p)
    {
        if (xs.Count == 0) return 0;
        var sorted = xs.OrderBy(x => x).ToList();
        int i = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Max(0, Math.Min(sorted.Count - 1, i))];
    }

    private static string Stats(List<double> xs) =>
        xs.Count == 0 ? "-" : $"{xs.Average(),7:F0} | {Pct(xs, 0.5),6:F0} | {Pct(xs, 0.95),6:F0} | {xs.Max(),6:F0}";

    /// <summary>"Attempt 3 stopped early: the Small building on plot 2 ..." → "stopped early: the # building on plot # ..."</summary>
    private static string Reason(string dropped)
    {
        var r = Regex.Replace(dropped, @"^Attempt \d+ ", "");
        r = Regex.Replace(r, @"\b(Big|Small|Medium|Tall|Wide)\b", "#");
        r = Regex.Replace(r, @"-?\d+(\.\d+)?", "#");
        return r.Length > 140 ? r.Substring(0, 140) + "…" : r;
    }

    private static string Report(Dictionary<int, List<LevelRun>> all, int seeds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Per level and per attempt ({seeds} seeds, ms: mean | median | p95 | max)");
        sb.AppendLine();
        sb.AppendLine("| preset | failed | level ms | attempt ms | attempts mean / max | build plan ms (median) | primitives (median) |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (preset, runs) in all)
        {
            var attemptMs = runs.SelectMany(r => r.AttemptMs).ToList();
            var ok = runs.Where(r => r.Success).ToList();
            sb.AppendLine($"| {PresetNames[preset]} | {runs.Count(r => !r.Success)} | {Stats(runs.Select(r => r.Ms).ToList())} | " +
                          $"{Stats(attemptMs)} | {runs.Average(r => r.Attempts):F2} / {runs.Max(r => r.Attempts)} | " +
                          $"{Pct(ok.Select(r => r.PlanMs).ToList(), 0.5):F0} | {Pct(ok.Select(r => (double)r.Primitives).ToList(), 0.5):F0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Stage time per level (ms, mean over seeds; nested stages are included in their parent)");
        sb.AppendLine();
        var stages = all.Values.SelectMany(rs => rs.SelectMany(r => r.Stages.Keys)).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        sb.AppendLine("| stage | " + string.Join(" | ", all.Keys.Select(p => PresetNames[p])) + " |");
        sb.AppendLine("|---|" + string.Concat(all.Keys.Select(_ => "---|")));
        foreach (var stage in stages)
            sb.AppendLine($"| {stage} | " + string.Join(" | ", all.Values.Select(rs =>
                rs.Average(r => r.Stages.TryGetValue(stage, out double ms) ? ms : 0).ToString("F1"))) + " |");

        sb.AppendLine();
        sb.AppendLine("## Primitive counts by owner (median)");
        sb.AppendLine();
        var owners = (OwnerKind[])Enum.GetValues(typeof(OwnerKind));
        sb.AppendLine("| preset | " + string.Join(" | ", owners) + " |");
        sb.AppendLine("|---|" + string.Concat(owners.Select(_ => "---|")));
        foreach (var (preset, runs) in all)
            sb.AppendLine($"| {PresetNames[preset]} | " + string.Join(" | ", owners.Select(o =>
                Pct(runs.Where(r => r.Success).Select(r => (double)(r.ByOwner.TryGetValue(o, out int n) ? n : 0)).ToList(), 0.5).ToString("F0"))) + " |");

        sb.AppendLine();
        sb.AppendLine("## Why attempts were retried");
        foreach (var (preset, runs) in all)
        {
            var reasons = runs.SelectMany(r => r.Dropped).GroupBy(Reason).OrderByDescending(g => g.Count()).ToList();
            if (reasons.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"**{PresetNames[preset]}** (seeds with retries: " +
                          string.Join(", ", runs.Where(r => r.Attempts > 1).Select(r => $"{r.Seed}x{r.Attempts}")) + ")");
            foreach (var g in reasons) sb.AppendLine($"- {g.Count()}x {g.Key}");
        }
        return sb.ToString();
    }

    /// <summary>Every overlap in the generated levels, by the kinds of the two primitives.</summary>
    private static int Overlaps(int seeds, List<int> presets)
    {
        foreach (int preset in presets)
        {
            var s = Preset(preset);
            var kinds = new Dictionary<string, int>();
            var examples = new Dictionary<string, List<string>>();
            ulong current = 0;
            OverlapCheck.Observed = (level, overlaps) =>
            {
                foreach (var o in overlaps)
                {
                    var a = level.Primitives[o.A];
                    var b = level.Primitives[o.B];
                    string key = $"{a.OwnerKind} {a.Role} {a.Shape} / {b.OwnerKind} {b.Role} {b.Shape}" + (o.Volume > 0 ? " (volume)" : " (face)");
                    kinds[key] = kinds.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (!examples.TryGetValue(key, out var list)) examples[key] = list = new List<string>();
                    if (list.Count < 3)
                        list.Add($"seed {current}: #{o.A} {a.OwnerId} pos {a.Position} size {a.Size} yaw {a.YawDegrees} | #{o.B} {b.OwnerId} pos {b.Position} size {b.Size} yaw {b.YawDegrees} | at {o.Where} vol {o.Volume:G3} area {o.Area:G3}");
                }
            };
            int levels = 0;
            for (ulong seed = 0; seed < (ulong)seeds; seed++)
            {
                current = seed;
                var result = LevelGenerator.Generate(s, seed);
                if (!result.Success) continue;
                levels++;
                OverlapCheck.Check(result.Level);
            }
            OverlapCheck.Observed = null;
            Console.WriteLine($"## {PresetNames[preset]}: {levels} levels checked, {kinds.Values.Sum()} overlaps");
            foreach (var kv in kinds.OrderByDescending(k => k.Value))
            {
                Console.WriteLine($"{kv.Value,5}  {kv.Key}");
                foreach (var e in examples[kv.Key]) Console.WriteLine($"         {e}");
            }
        }
        return 0;
    }

    /// <summary>The first overlaps of a seed's first checked attempt, with both primitives' footprints.</summary>
    private static int Dump(int preset, ulong seed, int count, string kind)
    {
        bool done = false;
        OverlapCheck.Observed = (level, overlaps) =>
        {
            if (kind != null)
                overlaps = overlaps.FindAll(o => level.Primitives[o.A].OwnerKind.ToString() == kind
                                                 || level.Primitives[o.B].OwnerKind.ToString() == kind);
            if (done || overlaps.Count == 0) return;
            done = true;
            foreach (var o in overlaps.Take(count))
            {
                Console.WriteLine($"overlap #{o.A} / #{o.B} vol {o.Volume:G4} area {o.Area:G4} at {o.Where}");
                foreach (int k in new[] { o.A, o.B })
                {
                    var p = level.Primitives[k];
                    Console.WriteLine($"  #{k} {p.OwnerKind} {p.OwnerId} {p.Role} bottom {p.Position.y:R} top {p.PrismTop(p.Position.x, p.Position.z):R}");
                    Console.WriteLine("    " + string.Join(" ", level.Polygons[p.Polygon].Select(v => $"({v.x:R},{v.y:R})")));
                }
            }
        };
        var result = LevelGenerator.Generate(Preset(preset), seed);
        if (result.Success) OverlapCheck.Check(result.Level);
        return 0;
    }
}
