using System.Collections.Generic;
using System.Diagnostics;

namespace Arena.LevelGen
{
    /// <summary>
    /// Wall-clock time per generator stage, for the profiling runner (Tools/LevelGenProfile), which compiles the core with
    /// LEVELGEN_PROFILE. Everywhere else, Unity included, every call to it compiles away. A stage opened inside another
    /// is timed under its path ("Buildings/Local check/Rasterize"), so a time counts once per path.
    /// </summary>
    public static class StageProfiler
    {
        public sealed class Section
        {
            public double Milliseconds;
            public int Calls;
            /// <summary>Each call's time, in order.</summary>
            public readonly List<double> Samples = new();
        }

        /// <summary>Times by path, since the last <see cref="Reset"/>.</summary>
        public static readonly Dictionary<string, Section> Sections = new();

        private static readonly Stack<(string path, long start)> Open = new();

        [Conditional("LEVELGEN_PROFILE")]
        public static void Begin(string stage)
        {
            string path = Open.Count == 0 ? stage : Open.Peek().path + "/" + stage;
            Open.Push((path, Stopwatch.GetTimestamp()));
        }

        [Conditional("LEVELGEN_PROFILE")]
        public static void End()
        {
            var (path, start) = Open.Pop();
            Add(path, (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }

        /// <summary>A stage timed elsewhere (the generator's own laps), under the open stage.</summary>
        [Conditional("LEVELGEN_PROFILE")]
        public static void Record(string stage, double ms) => Add(Open.Count == 0 ? stage : Open.Peek().path + "/" + stage, ms);

        private static void Add(string path, double ms)
        {
            if (!Sections.TryGetValue(path, out var section)) Sections[path] = section = new Section();
            section.Milliseconds += ms;
            section.Calls++;
            section.Samples.Add(ms);
        }

        public static void Reset()
        {
            Sections.Clear();
            Open.Clear();
        }
    }
}
