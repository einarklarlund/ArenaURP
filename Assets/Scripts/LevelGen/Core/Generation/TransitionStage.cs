using System.Collections.Generic;
using UnityEngine;

namespace Arena.LevelGen
{
    /// <summary>A plot's ground plane: <see cref="Height"/> at the polygon's centroid, tilted by <see cref="Slope"/>.</summary>
    public struct PlotGround
    {
        public float Height;
        public Vector2 Slope;
        public Vector2 Centroid;

        public bool Flat => Slope == Vector2.zero;

        public float At(Vector2 p) => Height + Vector2.Dot(Slope, p - Centroid);
    }

    /// <summary>
    /// A sloped quad from a face (a stretch of the edge) into the lower plot: a planar top fitted to the higher plot's
    /// ground along the face and the lower plot's ground along the foot, both of which may be sloped.
    /// </summary>
    public struct SlopeFit
    {
        public bool Ok;
        /// <summary>How far it runs into the lower plot.</summary>
        public float Run;
        /// <summary>Top gradient (rise per unit of x and z).</summary>
        public Vector2 Gradient;
        /// <summary>The quad's centre and the top's height there.</summary>
        public Vector2 Centre;
        public float CentreHeight;

        public float At(Vector2 p) => CentreHeight + Vector2.Dot(Gradient, p - Centre);
    }

    /// <summary>One segment of a plot edge and how it is crossed.</summary>
    public sealed class SegmentPlan
    {
        public TransitionType Type;
        public int PlotA;
        public int PlotB;
        public Vector2 From;
        public Vector2 To;
        /// <summary>Ground height of A minus that of B, at each end.</summary>
        public float DeltaFrom;
        public float DeltaTo;
        /// <summary>The lower plot (by the mean of the two ends): where a ramp or bank is cut.</summary>
        public int Lower;
        /// <summary>The ramp this segment would get at its middle; <see cref="SlopeFit.Ok"/> says whether it fits.</summary>
        public SlopeFit Ramp;
        /// <summary>The bank this segment would get along its whole length.</summary>
        public SlopeFit Bank;

        public bool RampFits => Ramp.Ok;
        public bool BankFits => Bank.Ok;
        /// <summary>Run of a ramp (or of a bank, where no ramp fits): the cost the plot-graph repair minimizes.</summary>
        public float Depth => Ramp.Ok ? Ramp.Run : Bank.Run;
        /// <summary>The plot-graph check turned it into a ramp or bank.</summary>
        public bool Repaired;

        public int Higher => Lower == PlotA ? PlotB : PlotA;
        public float Length => (To - From).magnitude;
        public float MaxDelta => Mathf.Max(Mathf.Abs(DeltaFrom), Mathf.Abs(DeltaTo));
        /// <summary>The higher side changes along the segment.</summary>
        public bool Flips => DeltaFrom * DeltaTo < 0f;
    }

    /// <summary>Stage 3's output.</summary>
    public sealed class PlotPlan
    {
        public PlotGround[] Ground;
        public List<SegmentPlan> Segments = new();
        /// <summary>Plots kept flat for buildings, chosen with the building stream for the drawn mix.</summary>
        public List<int> BuildingPlots = new();
        /// <summary>Per building plot, the class of building it gets.</summary>
        public List<BuildingClass> BuildingClasses = new();
        /// <summary>The building mix drawn, or None when no mix fits the plots (the level attempt fails).</summary>
        public BuildingMix Mix;
        public List<string> Repairs = new();
        /// <summary>Why the plot-graph check couldn't be satisfied, or null when it was.</summary>
        public string Failure;
    }

    /// <summary>
    /// Stage 3: plot heights, slopes and edge transitions, chosen together because whether a height difference works
    /// depends on the edge. Then the plot-graph check: plots as nodes, two-way and one-way edges from the transitions.
    /// While it isn't strongly connected, or a plot has no two-way transition, the cheapest cliff or blocked segment
    /// that touches a failing plot becomes a ramp, or a bank where no ramp fits. Two plots are joined by at most one
    /// slope, ramp or bank.
    /// </summary>
    public static class TransitionStage
    {
        /// <summary>Odds that a plot (not kept for a building) gets a planar slope. Not a config knob yet.</summary>
        public const float SlopeOdds = 0.35f;

        /// <summary>Height shuffles drawn; the one with the least unsafe drop between neighbours is kept.</summary>
        public const int HeightShuffles = 6;

        /// <summary>Heights, then transitions: <see cref="PlanHeights"/> then <see cref="PlanTransitions"/>, then the building
        /// mix checked against the slopes' clear-outs (<see cref="BuildingStage.ConfirmMix"/>). The plan has
        /// <see cref="PlotPlan.Failure"/> set when the plot-graph check can't be satisfied.</summary>
        public static PlotPlan Build(PlotPartition partition, PlotTargets[] targets, in LevelGenSettings s, ulong seed)
        {
            var plan = PlanHeights(partition, targets, s, seed);
            PlanTransitions(partition, plan, s, seed);
            BuildingStage.ConfirmMix(partition, plan, s, seed);
            return plan;
        }

        /// <summary>Top-level plots' heights and slopes, and the plots kept flat for buildings.</summary>
        public static PlotPlan PlanHeights(PlotPartition partition, PlotTargets[] targets, in LevelGenSettings s, ulong seed)
        {
            var plan = new PlotPlan { Ground = new PlotGround[partition.Polygons.Count] };
            ChooseBuildingPlots(partition, plan, s, seed);
            AssignHeights(partition, plan, targets, s, seed);
            return plan;
        }

        /// <summary>Segments and transitions on every edge between plots, then the plot-graph check over them.</summary>
        public static void PlanTransitions(PlotPartition partition, PlotPlan plan, in LevelGenSettings s, ulong seed)
        {
            int n = partition.Polygons.Count;
            var rng = SeedTree.Stream(seed, GenStage.Transitions);
            foreach (var edge in partition.Edges) Split(edge, partition, plan, s, ref rng);
            // At most one slope (ramp or bank) between any two plots: once a pair has one, its other segments
            // can't choose either.
            var sloped = new HashSet<(int, int)>();
            foreach (var segment in plan.Segments)
            {
                segment.Type = Choose(segment, !sloped.Contains((segment.PlotA, segment.PlotB)), s, ref rng);
                if (IsSlope(segment.Type)) sloped.Add((segment.PlotA, segment.PlotB));
            }

            CheckAndRepair(plan, n, out plan.Failure);
        }

        // ---------------------------------------------------------------- heights

        /// <summary>
        /// The building mix and its plots (<see cref="BuildingStage.ChooseMix"/>), drawn with the building stream. Stage 4
        /// builds on them; here they only stay flat.
        /// </summary>
        private static void ChooseBuildingPlots(PlotPartition partition, PlotPlan plan, in LevelGenSettings s, ulong seed)
        {
            var rng = SeedTree.Stream(seed, GenStage.Buildings);
            plan.Mix = BuildingStage.ChooseMix(partition.Polygons, s, ref rng, plan.BuildingPlots, plan.BuildingClasses);
        }

        /// <summary>
        /// Base heights in <c>heightStep</c> multiples spread evenly over <c>0 .. levelElevationRange × maxHeight</c>
        /// and shuffled (the best of a few shuffles, see <see cref="HeightShuffles"/>), so the level range is met. A plot not kept for a building may then tilt: its lowest corner
        /// stays at the base height and it rises along a random direction by at most its elevation range target,
        /// never steeper than <c>rampMaxSlope</c>.
        /// </summary>
        private static void AssignHeights(PlotPartition partition, PlotPlan plan, PlotTargets[] targets,
            in LevelGenSettings s, ulong seed)
        {
            int n = partition.Polygons.Count;
            var rng = SeedTree.Stream(seed, GenStage.Heights);
            float step = s.Plots.HeightStep;
            int steps = Mathf.FloorToInt(s.LevelElevationRange.Target * s.MaxHeight / step + 1e-4f);
            var bases = new float[n];
            for (int i = 0; i < n; i++) bases[i] = n > 1 ? Mathf.Round((float)i / (n - 1) * steps) * step : 0f;
            // Of a few shuffles, keep the one with the least height between neighbours beyond a safe drop: those
            // edges can only be ramps, banks or blocked, and too many of them strand plots.
            float[] best = null;
            float bestExcess = float.PositiveInfinity;
            for (int draw = 0; draw < HeightShuffles; draw++)
            {
                var order = (float[])bases.Clone();
                for (int i = n - 1; i > 0; i--)
                {
                    int j = rng.Range(0, i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                float excess = 0f;
                foreach (var edge in partition.Edges)
                    excess += Mathf.Max(0f, Mathf.Abs(order[edge.A] - order[edge.B]) - s.Player.MaxSafeDrop);
                if (excess >= bestExcess) continue;
                best = order;
                bestExcess = excess;
            }
            bases = best;
            BuildingStage.KeepBigPlotsLow(plan, bases, s);

            float maxGrade = Mathf.Tan(s.TransitionSizes.RampMaxSlopeDegrees * Mathf.Deg2Rad);
            for (int i = 0; i < n; i++)
            {
                var polygon = partition.Polygons[i];
                var ground = new PlotGround { Height = bases[i], Centroid = Polygon.Centroid(polygon) };
                float allowedRise = Mathf.Min(targets[i].ElevationRange * s.MaxHeight, s.MaxHeight - bases[i]);
                // Draw every plot's numbers, slope or not, so one plot's choice doesn't shift the next plot's stream.
                bool tilt = rng.Chance(SlopeOdds);
                float angle = rng.Range(0f, 2f * Mathf.PI);
                float riseT = rng.NextFloat();
                if (tilt && !plan.BuildingPlots.Contains(i) && allowedRise >= step)
                {
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Polygon.Extent(polygon, dir, out float min, out float max);
                    float rise = Mathf.Max(step, Mathf.Floor(riseT * allowedRise / step) * step);
                    float grade = Mathf.Min(rise / (max - min), maxGrade);
                    ground.Slope = dir * grade;
                    ground.Height = bases[i] + grade * (Vector2.Dot(dir, ground.Centroid) - min);
                }
                plan.Ground[i] = ground;
            }
        }

        // ---------------------------------------------------------------- transitions

        /// <summary>1–3 segments, each at least <c>doorWidth + 2</c> long, cut at random points.</summary>
        private static void Split(PlotEdge edge, PlotPartition partition, PlotPlan plan, in LevelGenSettings s, ref Pcg32 rng)
        {
            float minLength = s.Buildings.DoorWidth + 2f;
            float length = edge.Length;
            int count = Mathf.Clamp(Mathf.FloorToInt(length / minLength), 1, 3);
            count = rng.Range(1, count + 1);
            var cuts = new List<float>();
            float slack = length - count * minLength;
            for (int k = 0; k < count - 1; k++) cuts.Add(rng.NextFloat() * Mathf.Max(0f, slack));
            cuts.Sort();

            var dir = (edge.To - edge.From) / length;
            float start = 0f;
            for (int k = 0; k < count; k++)
            {
                float end = k == count - 1 ? length : (k + 1) * minLength + cuts[k];
                var from = k == 0 ? edge.From : edge.From + dir * start;
                var to = k == count - 1 ? edge.To : edge.From + dir * end;
                plan.Segments.Add(Measure(edge.A, edge.B, from, to, partition, plan, s));
                start = end;
            }
        }

        private static SegmentPlan Measure(int a, int b, Vector2 from, Vector2 to, PlotPartition partition, PlotPlan plan,
            in LevelGenSettings s)
        {
            var ga = plan.Ground[a];
            var gb = plan.Ground[b];
            var segment = new SegmentPlan
            {
                PlotA = a, PlotB = b, From = from, To = to,
                DeltaFrom = ga.At(from) - gb.At(from),
                DeltaTo = ga.At(to) - gb.At(to),
            };
            segment.Lower = segment.DeltaFrom + segment.DeltaTo >= 0f ? b : a;
            float delta = Mathf.Abs(segment.DeltaFrom + segment.DeltaTo) * 0.5f;
            if (delta <= s.Player.StepHeight) return segment;

            var lowerPolygon = partition.Polygons[segment.Lower];
            var into = Into(lowerPolygon, from, to);
            float room = float.NegativeInfinity;
            foreach (var v in lowerPolygon) room = Mathf.Max(room, Vector2.Dot(into, v - from));
            room -= s.Plots.MinPlotSide * 0.5f;

            var low = plan.Ground[segment.Lower];
            var high = plan.Ground[segment.Higher];
            float maxGrade = Mathf.Tan(s.TransitionSizes.RampMaxSlopeDegrees * Mathf.Deg2Rad);
            float tolerance = s.Player.StepHeight * 0.5f;

            float rampWidth = s.TransitionSizes.RampWidth;
            var mid = (from + to) * 0.5f;
            var dir = (to - from).normalized;
            if (segment.Length >= rampWidth)
            {
                var ramp = Fit(low, high, mid - dir * rampWidth * 0.5f, mid + dir * rampWidth * 0.5f, into, maxGrade, tolerance,
                    s.TransitionSizes.CurveFraction);
                ramp.Ok &= ramp.Run <= room && RampClear(lowerPolygon, from, to, mid, dir, into, rampWidth, ramp.Run);
                segment.Ramp = ramp;
            }
            if (!segment.Flips)
            {
                var bank = Fit(low, high, from, to, into, maxGrade, tolerance, s.TransitionSizes.CurveFraction);
                bank.Ok &= bank.Run <= room;
                segment.Bank = bank;
            }
            return segment;
        }

        /// <summary>
        /// Fits a sloped quad to a face <paramref name="a"/>–<paramref name="b"/>. Its run is set so the slope into the
        /// lower plot, combined with the slope along the face, is <paramref name="maxGrade"/>. The top is the
        /// least-squares plane through the four corner heights (the higher ground at the face, the lower ground at the
        /// foot); it fits when that plane is within <paramref name="tolerance"/> of every corner, i.e. a quarter of the
        /// twist between the two grounds.
        /// </summary>
        /// <param name="curveFraction">Share of the run spent curving at the foot and top: the run grows by
        /// 1 / (1 − this / 2) so the steepest band keeps <paramref name="maxGrade"/> (see <see cref="Profile"/>).</param>
        public static SlopeFit Fit(PlotGround low, PlotGround high, Vector2 a, Vector2 b, Vector2 into, float maxGrade,
            float tolerance, float curveFraction = 0f)
        {
            var fit = new SlopeFit();
            float length = (b - a).magnitude;
            var dir = (b - a) / length;
            var mid = (a + b) * 0.5f;
            float along = (Vector2.Dot(high.Slope, dir) + Vector2.Dot(low.Slope, dir)) * 0.5f;
            if (Mathf.Abs(along) >= maxGrade) return fit;
            float intoGrade = Mathf.Sqrt(maxGrade * maxGrade - along * along);
            float rise = high.At(mid) - low.At(mid);
            float denominator = intoGrade + Vector2.Dot(low.Slope, into);
            if (rise <= 0f || denominator <= 1e-3f) return fit;
            fit.Run = rise / denominator / (1f - curveFraction * 0.5f);

            var footA = a + into * fit.Run;
            var footB = b + into * fit.Run;
            float ha = high.At(a), hb = high.At(b), la = low.At(footA), lb = low.At(footB);
            float twist = (hb - ha) - (lb - la);
            fit.Centre = mid + into * (fit.Run * 0.5f);
            fit.CentreHeight = (ha + hb + la + lb) * 0.25f;
            fit.Gradient = dir * ((hb - ha + lb - la) * 0.5f / length) + into * ((la - ha + lb - hb) * 0.5f / fit.Run);
            fit.Ok = Mathf.Abs(twist) * 0.25f <= tolerance && fit.Gradient.magnitude <= maxGrade * 1.02f;
            return fit;
        }

        /// <summary>
        /// Whether a ramp's footprint keeps a walkway's width (<see cref="LevelGenSettings.MinWalkWidth"/>) from every
        /// edge of the lower plot except the face it climbs. Closer than that, the gap between its side and a
        /// neighbouring plot's face is too narrow to walk out of: a pocket you can drop into but not leave.
        /// </summary>
        private static bool RampClear(List<Vector2> lower, Vector2 from, Vector2 to, Vector2 mid, Vector2 dir,
            Vector2 into, float width, float run)
        {
            var corners = new[]
            {
                mid - dir * width * 0.5f, mid + dir * width * 0.5f,
                mid - dir * width * 0.5f + into * run, mid + dir * width * 0.5f + into * run,
            };
            for (int k = 0; k < lower.Count; k++)
            {
                var a = lower[k];
                var b = lower[(k + 1) % lower.Count];
                var edge = (b - a).normalized;
                // Skip the face: the edge the segment lies on.
                if (Mathf.Abs(Cross(edge, from - a)) < 1e-3f && Mathf.Abs(Cross(edge, to - a)) < 1e-3f) continue;
                var inward = new Vector2(-edge.y, edge.x);
                foreach (var c in corners)
                    if (Vector2.Dot(inward, c - a) < LevelGenSettings.MinWalkWidth) return false;
            }
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>A ramp or a bank: what two plots may have only one of between them.</summary>
        public static bool IsSlope(TransitionType type) => type is TransitionType.Ramp or TransitionType.Bank;

        /// <summary>By the transition weights, among the types that fit (blocked takes the cliff weight). Ramps and
        /// banks are options only when <paramref name="slopeAllowed"/>.</summary>
        private static TransitionType Choose(SegmentPlan segment, bool slopeAllowed, in LevelGenSettings s, ref Pcg32 rng)
        {
            var w = s.TransitionWeights;
            var p = s.Player;
            float delta = segment.MaxDelta;
            var natural = delta <= p.StepHeight ? TransitionType.Flat
                : delta <= p.JumpHeight ? TransitionType.Ledge
                : delta <= p.MaxSafeDrop && !segment.Flips ? TransitionType.Cliff
                : TransitionType.Blocked;

            var options = new List<(TransitionType type, float weight)>();
            options.Add((natural, natural switch
            {
                TransitionType.Flat => w.Flat,
                TransitionType.Ledge => w.Ledge,
                _ => w.Cliff,
            }));
            if (segment.RampFits && slopeAllowed) options.Add((TransitionType.Ramp, w.Ramp));
            if (segment.BankFits && slopeAllowed) options.Add((TransitionType.Bank, w.Bank));

            float total = 0f;
            foreach (var o in options) total += Mathf.Max(0f, o.weight);
            float pick = rng.NextFloat() * total;
            if (total <= 0f) return natural;
            foreach (var o in options)
            {
                pick -= Mathf.Max(0f, o.weight);
                if (pick < 0f) return o.type;
            }
            return options[^1].type;
        }

        // ---------------------------------------------------------------- plot graph

        private static bool CheckAndRepair(PlotPlan plan, int n, out string failure)
        {
            failure = null;
            for (int round = 0; round <= plan.Segments.Count; round++)
            {
                var failing = FailingPlots(plan, n);
                if (failing.Count == 0) return true;

                var sloped = new HashSet<(int, int)>();
                foreach (var segment in plan.Segments)
                    if (IsSlope(segment.Type)) sloped.Add((segment.PlotA, segment.PlotB));

                // Only between plots with no slope yet: a ramp where one fits, otherwise a bank.
                SegmentPlan cheapest = null;
                var type = TransitionType.Ramp;
                foreach (var segment in plan.Segments)
                {
                    if (segment.Type is not (TransitionType.Cliff or TransitionType.Blocked)) continue;
                    if (!failing.Contains(segment.PlotA) && !failing.Contains(segment.PlotB)) continue;
                    if (sloped.Contains((segment.PlotA, segment.PlotB))) continue;
                    bool ramp = segment.RampFits;
                    if (!ramp && !segment.BankFits) continue;
                    var candidate = ramp ? TransitionType.Ramp : TransitionType.Bank;
                    bool better = cheapest == null
                        || (candidate == TransitionType.Ramp && type == TransitionType.Bank)
                        || (candidate == type && segment.Depth < cheapest.Depth);
                    if (!better) continue;
                    cheapest = segment;
                    type = candidate;
                }
                if (cheapest == null)
                {
                    failure = $"plots {string.Join(", ", failing)} can't be joined: no cliff or blocked segment next to " +
                              "them has room for a ramp or bank";
                    return false;
                }
                plan.Repairs.Add($"Plots {cheapest.PlotA}–{cheapest.PlotB}: {cheapest.Type} segment became a " +
                                 $"{type.ToString().ToLowerInvariant()} ({cheapest.MaxDelta:0.##} high) for plots {string.Join(", ", failing)}");
                cheapest.Type = type;
                cheapest.Repaired = true;
            }
            failure = "the plot-graph repair didn't converge";
            return false;
        }

        /// <summary>
        /// Plots with no two-way transition, plus those outside the strongly connected set of plot 0 (not reachable
        /// from it, or unable to get back).
        /// </summary>
        public static List<int> FailingPlots(PlotPlan plan, int n)
        {
            var forward = new List<int>[n];
            var backward = new List<int>[n];
            var twoWay = new bool[n];
            for (int i = 0; i < n; i++)
            {
                forward[i] = new List<int>();
                backward[i] = new List<int>();
            }
            void Link(int from, int to)
            {
                forward[from].Add(to);
                backward[to].Add(from);
            }
            foreach (var segment in plan.Segments)
            {
                if (TransitionTypes.IsTwoWay(segment.Type))
                {
                    Link(segment.PlotA, segment.PlotB);
                    Link(segment.PlotB, segment.PlotA);
                    twoWay[segment.PlotA] = twoWay[segment.PlotB] = true;
                }
                else if (segment.Type == TransitionType.Cliff) Link(segment.Higher, segment.Lower);
            }
            var reached = Reach(forward, n);
            var returns = Reach(backward, n);
            var failing = new List<int>();
            for (int i = 0; i < n; i++)
                if (!twoWay[i] || !reached[i] || !returns[i]) failing.Add(i);
            return failing;
        }

        private static bool[] Reach(List<int>[] links, int n)
        {
            var seen = new bool[n];
            var stack = new Stack<int>();
            seen[0] = true;
            stack.Push(0);
            while (stack.Count > 0)
                foreach (int t in links[stack.Pop()])
                    if (!seen[t])
                    {
                        seen[t] = true;
                        stack.Push(t);
                    }
            return seen;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>
        /// Plot descriptions, a ground prism per plot solid from below y = 0 to its plane (except plots in
        /// <paramref name="terrained"/>, whose ground stage 5 draws), and the transitions. The ramps' and banks' solids
        /// come later, from <see cref="EmitSlopes"/>, once the terrain is shaped.
        /// </summary>
        public static void Emit(LevelDescription level, PlotPartition partition, PlotPlan plan, PlotTargets[] targets,
            in LevelGenSettings s, HashSet<int> terrained = null)
        {
            for (int i = 0; i < partition.Polygons.Count; i++)
            {
                var ground = plan.Ground[i];
                int polygon = level.AddPolygon(partition.Polygons[i]);
                level.Plots.Add(new PlotDescription
                {
                    Id = i, Polygon = polygon, BaseHeight = ground.Height, Slope = ground.Slope, Targets = targets[i],
                });
                if (terrained == null || !terrained.Contains(i))
                    level.AddPrism(PrimitiveRole.Floor, polygon, GroundBottom, ground.Height, ground.Slope, OwnerKind.Plot, i,
                        GroundSurfaces);
            }

            for (int id = 0; id < plan.Segments.Count; id++)
            {
                var segment = plan.Segments[id];
                level.Transitions.Add(new TransitionDescription
                {
                    Id = id, Type = segment.Type, PlotA = segment.PlotA, PlotB = segment.PlotB,
                    From = segment.From, To = segment.To,
                    Width = segment.Type == TransitionType.Ramp ? s.TransitionSizes.RampWidth : segment.Length,
                });
            }
        }

        /// <summary>
        /// Every ramp's and bank's solids, fitted to the ground as it ends up (the terrain shaped): the slope's top meets
        /// the higher plot's ground exactly along the face and the lower plot's ground exactly along the foot and the
        /// flares' outer edges. The plot ground and terrain under them is then carved away, so slope and ground meet
        /// face to face and never overlap; a slope also gives way to the slopes made before it.
        /// </summary>
        public static void EmitSlopes(LevelDescription level, PlotPartition partition, PlotPlan plan, in LevelGenSettings s,
            GroundSurface ground)
        {
            var earlier = new List<(List<Vector2> polygon, Rect bounds)>();
            var carve = new List<List<Vector2>>();
            for (int id = 0; id < plan.Segments.Count; id++)
            {
                var segment = plan.Segments[id];
                if (!IsSlope(segment.Type)) continue;
                var lower = partition.Polygons[segment.Lower];
                var dir = (segment.To - segment.From).normalized;
                var flares = new List<(Vector2 side, Vector2 outward)>();
                List<Vector2> outline;
                SlopeFit fit;
                if (segment.Type == TransitionType.Bank)
                {
                    outline = SlopeOutline(partition, segment, 0f);
                    fit = segment.Bank;
                    if (!IsCorner(lower, segment.From)) flares.Add((segment.From, -dir));
                    if (!IsCorner(lower, segment.To)) flares.Add((segment.To, dir));
                }
                else
                {
                    float width = s.TransitionSizes.RampWidth;
                    outline = SlopeOutline(partition, segment, 0f, width);
                    fit = segment.Ramp;
                    var mid = (segment.From + segment.To) * 0.5f;
                    flares.Add((mid - dir * width * 0.5f, -dir));
                    flares.Add((mid + dir * width * 0.5f, dir));
                }
                var pieces = new List<List<Vector2>>();
                AddSlope(level, id, segment, Polygon.ClipConvex(outline, lower, true), fit, lower, plan.Ground[segment.Higher],
                    ground, flares, s, earlier, pieces, carve);
                foreach (var piece in pieces) earlier.Add((piece, Bounds(piece)));
            }
            Carve(level, carve);
        }

        /// <summary>
        /// Replaces every plot ground and terrain prism that a region of <paramref name="carve"/> overlaps with the
        /// convex pieces of it outside them, in the same plane.
        /// </summary>
        private static void Carve(LevelDescription level, List<List<Vector2>> carve)
        {
            var bounds = carve.ConvertAll(Bounds);
            var primitives = new List<Primitive>(level.Primitives.Count);
            foreach (var p in level.Primitives)
            {
                if (p.OwnerKind != OwnerKind.Plot || p.Shape != PrimitiveShape.Prism)
                {
                    primitives.Add(p);
                    continue;
                }
                var pieces = new List<List<Vector2>> { level.Polygons[p.Polygon] };
                var box = Bounds(pieces[0]);
                bool cut = false;
                for (int c = 0; c < carve.Count; c++)
                {
                    if (!box.Overlaps(bounds[c])) continue;
                    var next = new List<List<Vector2>>();
                    foreach (var piece in pieces)
                        if (Polygon.ClipConvex(piece, carve[c], true).Count == 0) next.Add(piece);
                        else
                        {
                            next.AddRange(Polygon.Subtract(piece, carve[c]));
                            cut = true;
                        }
                    pieces = next;
                }
                if (!cut)
                {
                    primitives.Add(p);
                    continue;
                }
                foreach (var piece in pieces)
                {
                    if (Polygon.MinWidth(piece) <= Polygon.MinSolidWidth) continue;
                    var q = p;
                    q.Polygon = level.AddPolygon(piece);
                    primitives.Add(q);
                }
            }
            level.Primitives.Clear();
            level.Primitives.AddRange(primitives);
        }

        private static Rect Bounds(List<Vector2> polygon)
        {
            Polygon.Extent(polygon, Vector2.right, out float x0, out float x1);
            Polygon.Extent(polygon, Vector2.up, out float z0, out float z1);
            return new Rect(x0, z0, x1 - x0, z1 - z0);
        }

        /// <summary>A plot's plain ground: ground on top, cliff face on every side.</summary>
        public static readonly Surfaces GroundSurfaces = Surfaces.Of(SurfaceTag.Ground, SurfaceTag.CliffFace, SurfaceTag.CliffFace);

        /// <summary>Ground runs from here up to its top, as the ground slab may extend below y = 0.</summary>
        public const float GroundBottom = -LevelGenSettings.SlabThickness;

        /// <summary>
        /// The ground a ramp or bank covers on the lower plot: a bank's strip (see <see cref="ClipEnd"/>), or a ramp's
        /// <paramref name="rampWidth"/>-wide quad at the segment's middle. <paramref name="runOut"/> extends it past the
        /// foot, and <paramref name="flare"/> widens it at each flared side (a ramp's two, a bank's ends cut mid-edge)
        /// to cover the flares: for keep-out zones and terrain pins. Empty for other transitions.
        /// </summary>
        public static List<Vector2> SlopeOutline(PlotPartition partition, SegmentPlan segment, float runOut, float rampWidth = 0f,
            float flare = 0f)
        {
            var lower = partition.Polygons[segment.Lower];
            var into = Into(lower, segment.From, segment.To);
            var dir = (segment.To - segment.From).normalized;
            if (segment.Type == TransitionType.Bank)
            {
                var strip = Polygon.ClipHalfPlane(lower, into, Vector2.Dot(into, segment.From) + segment.Bank.Run + runOut);
                strip = ClipEnd(strip, lower, segment.From, -dir, flare);
                return ClipEnd(strip, lower, segment.To, dir, flare);
            }
            if (segment.Type != TransitionType.Ramp) return new List<Vector2>();
            var mid = (segment.From + segment.To) * 0.5f;
            var a = mid - dir * (rampWidth * 0.5f + flare);
            var b = mid + dir * (rampWidth * 0.5f + flare);
            float depth = segment.Ramp.Run + runOut;
            var quad = new List<Vector2> { a, b, b + into * depth, a + into * depth };
            if (Polygon.SignedArea(quad) < 0f) quad.Reverse();
            return flare > 0f ? Polygon.ClipConvex(quad, lower) : quad;
        }

        /// <summary>How far a slope's flared sides reach out at most: its rise at <c>flareSlopeDegrees</c>.</summary>
        public static float FlareReach(SegmentPlan segment, in LevelGenSettings s) =>
            segment.MaxDelta / Mathf.Tan(s.TransitionSizes.FlareSlopeDegrees * Mathf.Deg2Rad);

        private static bool IsCorner(List<Vector2> polygon, Vector2 p)
        {
            foreach (var v in polygon)
                if (v.x == p.x && v.y == p.y) return true;
            return false;
        }

        /// <summary>
        /// Ends a bank strip at <paramref name="end"/>. Mid-edge, the cut is square: <c>dot(outward, p) ≤ dot(outward,
        /// end)</c>. At a corner of the lower plot it isn't cut at all, so the strip fills the corner up to the next edge:
        /// a square cut leaves a sliver of low ground against that edge at an obtuse corner, and a mitre leaves a
        /// narrowing corridor along it, both too narrow to walk out of when the next edge is a wall or a cliff.
        /// </summary>
        private static List<Vector2> ClipEnd(List<Vector2> strip, List<Vector2> lower, Vector2 end, Vector2 outward,
            float beyond = 0f)
        {
            if (IsCorner(lower, end)) return strip;
            return Polygon.ClipHalfPlane(strip, outward, Vector2.Dot(outward, end) + beyond);
        }

        /// <summary>
        /// The normalized profile of a slope from its foot (v = 0) to its top (v = 1): the grade rises linearly over the
        /// first <c>f / 2</c> (the sag curve), holds, and falls linearly over the last <c>f / 2</c> (the crest curve),
        /// so the slope leaves and meets level ground with no kink. The steepest grade is <c>1 / (1 − f / 2)</c> times
        /// the average, which is why the run is lengthened by that much.
        /// </summary>
        public static float Profile(float v, float f)
        {
            if (f <= 0f) return v;
            float c = f * 0.5f, g = 1f / (1f - c);
            if (v <= c) return g * v * v / (2f * c);
            if (v >= 1f - c) return 1f - g * (1f - v) * (1f - v) / (2f * c);
            return g * (c * 0.5f + (v - c));
        }

        /// <summary>
        /// One ramp's or bank's solids over <paramref name="outline"/>. The top is
        /// <c>low + (high − low) · profile(v)</c> at each point, where high is the higher plot's ground straight back
        /// at the face, low the lower ground straight ahead at the foot, and v how far up the run the point is
        /// (<see cref="Profile"/>, as <c>curveSegments</c> chords of the sag and crest curves and the straight middle).
        /// That is bilinear across a cell between two band lines and two of the foot's breaks (the corners of the
        /// outline and every terrain edge the foot crosses), so each cell is cut into triangles with exact heights at
        /// their corners. Each flared side gets a wedge per band, falling at <c>flareSlopeDegrees</c> from the slope's
        /// edge to the ground, which it meets exactly at every terrain edge along its outer edge. Every piece is a
        /// prism, clipped to the lower plot (whose edges are the cliffs the slope stops at) and to outside the slopes
        /// made before it (<paramref name="earlier"/>). The pieces go in <paramref name="pieces"/>, and the regions
        /// whose ground they replace in <paramref name="carve"/>.
        /// </summary>
        private static void AddSlope(LevelDescription level, int id, SegmentPlan segment, List<Vector2> outline,
            in SlopeFit fit, List<Vector2> lower, PlotGround high, GroundSurface ground,
            List<(Vector2 side, Vector2 outward)> flares, in LevelGenSettings s, List<(List<Vector2> polygon, Rect bounds)> earlier,
            List<List<Vector2>> pieces, List<List<Vector2>> carve)
        {
            if (outline.Count < 3 || fit.Run <= 1e-3f) return;
            float f = s.TransitionSizes.CurveFraction;
            var into = Into(lower, segment.From, segment.To);
            var dir = new Vector2(into.y, -into.x);
            var face = segment.From;
            float run = fit.Run;
            int low = segment.Lower;

            // The profile's chords: v at each band line, and the profile there.
            float c = f * 0.5f;
            int segments = s.TransitionSizes.CurveSegments;
            var breaks = new List<float> { 0f };
            if (f > 0f)
            {
                for (int k = 1; k <= segments; k++) breaks.Add(c * k / segments);
                for (int k = 0; k < segments; k++) breaks.Add(1f - c + c * k / segments);
            }
            breaks.Add(1f);
            var profile = breaks.ConvertAll(v => Profile(v, f));
            float Chord(float v)
            {
                v = Mathf.Clamp01(v);
                for (int k = 0; k + 1 < breaks.Count; k++)
                    if (v <= breaks[k + 1])
                        return profile[k] + (profile[k + 1] - profile[k]) * (v - breaks[k]) / (breaks[k + 1] - breaks[k]);
                return 1f;
            }
            float Height(Vector2 p)
            {
                float u = Vector2.Dot(into, p - face);
                var back = p - into * u;
                float foot = ground.At(low, back + into * run);
                return foot + (high.At(back) - foot) * Chord(1f - u / run);
            }

            // Lines across: the band lines (u from the face). Lines along: the outline's corners and every terrain
            // edge the foot crosses (t along the face).
            var us = new List<float>();
            for (int k = breaks.Count - 1; k >= 0; k--) us.Add((1f - breaks[k]) * run);
            Polygon.Extent(outline, dir, out float t0, out float t1);
            float faceT = Vector2.Dot(dir, face);
            var ts = new List<float>();
            foreach (var v in outline) ts.Add(Vector2.Dot(dir, v) - faceT);
            var footA = face + dir * (t0 - faceT) + into * run;
            var footB = face + dir * (t1 - faceT) + into * run;
            var crossings = new List<float>();
            ground.Crossings(low, footA, footB, crossings);
            foreach (float x in crossings) ts.Add(t0 - faceT + x * (t1 - t0));
            ts.Sort();
            for (int k = ts.Count - 1; k > 0; k--)
                if (ts[k] - ts[k - 1] < 1e-3f) ts.RemoveAt(k);

            var bandSurfaces = Surfaces.Of(segment.Type == TransitionType.Bank ? SurfaceTag.Bank : SurfaceTag.Ramp,
                SurfaceTag.RampSide, SurfaceTag.RampSide);
            Vector2 At(float u, float t) => face + into * u + dir * t;
            for (int i = 0; i + 1 < us.Count; i++)
                for (int j = 0; j + 1 < ts.Count; j++)
                {
                    var cell = new List<Vector2> { At(us[i], ts[j]), At(us[i], ts[j + 1]), At(us[i + 1], ts[j + 1]), At(us[i + 1], ts[j]) };
                    if (Polygon.SignedArea(cell) < 0f) cell.Reverse();
                    var piece = Polygon.ClipConvex(cell, outline, true);
                    for (int k = 1; k + 1 < piece.Count; k++)
                        AddPiece(level, id, Lift(piece[0], Height), Lift(piece[k], Height), Lift(piece[k + 1], Height),
                            lower, bandSurfaces, earlier, pieces);
                }
            carve.Add(outline);

            float flareGrade = Mathf.Tan(s.TransitionSizes.FlareSlopeDegrees * Mathf.Deg2Rad);
            var flareSurfaces = Surfaces.All(SurfaceTag.RampSide);
            foreach (var (side, outward) in flares)
                for (int i = 0; i + 1 < us.Count; i++)
                {
                    var near = side + into * us[i];
                    var far = side + into * us[i + 1];
                    float hNear = Height(near), hFar = Height(far);
                    float wNear = Mathf.Max(0f, hNear - ground.At(low, near)) / flareGrade;
                    float wFar = Mathf.Max(0f, hFar - ground.At(low, far)) / flareGrade;
                    if (wNear + wFar < 0.05f) continue;
                    var outerNear = near + outward * wNear;
                    var outerFar = far + outward * wFar;
                    // The wedge's ring: the slope's edge, then the ground's edge back, broken at every terrain edge.
                    var ring = new List<Vector3> { new(near.x, hNear, near.y), new(far.x, hFar, far.y) };
                    var along = new List<float>();
                    ground.Crossings(low, outerFar, outerNear, along);
                    along.Sort();
                    var outer = new List<Vector2> { outerFar };
                    foreach (float x in along) outer.Add(Vector2.Lerp(outerFar, outerNear, x));
                    outer.Add(outerNear);
                    foreach (var o in outer)
                        if ((o - near).sqrMagnitude > 1e-8f && (o - far).sqrMagnitude > 1e-8f)
                            ring.Add(new Vector3(o.x, ground.At(low, o), o.y));
                    for (int k = 1; k + 1 < ring.Count; k++)
                        AddPiece(level, id, ring[0], ring[k], ring[k + 1], lower, flareSurfaces, earlier, pieces);
                    var quad = new List<Vector2> { near, far, outerFar, outerNear };
                    if (Polygon.SignedArea(quad) < 0f) quad.Reverse();
                    var region = Polygon.ClipConvex(quad, lower, true);
                    if (region.Count >= 3) carve.Add(region);
                }
        }

        private static Vector3 Lift(Vector2 p, System.Func<Vector2, float> height) => new(p.x, height(p), p.y);

        /// <summary>
        /// A triangle of a slope's top as prisms: the triangle clipped to the lower plot, less the slopes before it,
        /// each piece in the triangle's plane and solid down to the ground's bottom.
        /// </summary>
        private static void AddPiece(LevelDescription level, int id, Vector3 a, Vector3 b, Vector3 c, List<Vector2> lower,
            in Surfaces surfaces, List<(List<Vector2> polygon, Rect bounds)> earlier, List<List<Vector2>> pieces)
        {
            var triangle = new List<Vector2> { new(a.x, a.z), new(b.x, b.z), new(c.x, c.z) };
            float area = Polygon.SignedArea(triangle);
            if (Mathf.Abs(area) < 1e-6f) return;
            if (area < 0f) triangle.Reverse();
            var gradient = TerrainStage.Gradient((a, b, c), out _);
            var parts = new List<List<Vector2>> { Polygon.ClipConvex(triangle, lower, true) };
            if (parts[0].Count < 3) return;
            var box = Bounds(parts[0]);
            foreach (var (other, otherBox) in earlier)
            {
                if (!box.Overlaps(otherBox)) continue;
                var next = new List<List<Vector2>>();
                foreach (var part in parts) next.AddRange(Polygon.Subtract(part, other));
                parts = next;
            }
            float lowest = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float bottom = Mathf.Min(GroundBottom, lowest - LevelGenSettings.SlabThickness);
            foreach (var part in parts)
            {
                if (Polygon.MinWidth(part) <= Polygon.MinSolidWidth) continue;
                var centroid = Polygon.Centroid(part);
                float height = a.y + Vector2.Dot(gradient, centroid - new Vector2(a.x, a.z));
                level.AddPrism(PrimitiveRole.Ramp, level.AddPolygon(part), bottom, height, gradient, OwnerKind.Transition, id,
                    surfaces);
                pieces.Add(part);
            }
        }

        /// <summary>Unit normal of the segment pointing into <paramref name="polygon"/>.</summary>
        public static Vector2 Into(List<Vector2> polygon, Vector2 from, Vector2 to)
        {
            var dir = (to - from).normalized;
            var normal = new Vector2(-dir.y, dir.x);
            return Vector2.Dot(Polygon.Centroid(polygon) - from, normal) < 0f ? -normal : normal;
        }
    }
}
