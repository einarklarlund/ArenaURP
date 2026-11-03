using System;

namespace Arena.LevelGen
{
    /// <summary>
    /// Spreads a per-plot metric's level target over N plots (pipeline stage 1). Kept behind one function so the
    /// formula can be swapped.
    /// </summary>
    public static class TargetAllocation
    {
        /// <summary>
        /// Quantiles <c>u = (i + 0.5) / N</c>, warped to <c>v = u ^ (2 ^ (2·skew))</c> (positive skew: most values
        /// low with a few high outliers; negative: the reverse), centred and scaled so
        /// spread 1 stretches as far across 0..1 as the target allows, clamped with the mean restored to the
        /// target, then shuffled with <paramref name="rng"/>.
        /// </summary>
        public static float[] Allocate(in MetricTarget metric, int count, ref Pcg32 rng)
        {
            var values = new float[count];
            if (count == 0) return values;
            double target = Math.Clamp(metric.Target, 0f, 1f);

            double exponent = Math.Pow(2.0, 2.0 * metric.Skew);
            var v = new double[count];
            double mean = 0;
            for (int i = 0; i < count; i++)
            {
                v[i] = Math.Pow((i + 0.5) / count, exponent);
                mean += v[i];
            }
            mean /= count;

            double low = 0, high = 0;
            for (int i = 0; i < count; i++)
            {
                low = Math.Min(low, v[i] - mean);
                high = Math.Max(high, v[i] - mean);
            }
            double scale = double.PositiveInfinity;
            if (low < 0) scale = Math.Min(scale, target / -low);
            if (high > 0) scale = Math.Min(scale, (1 - target) / high);
            if (double.IsPositiveInfinity(scale)) scale = 0;

            var x = new double[count];
            for (int i = 0; i < count; i++) x[i] = target + metric.Spread * scale * (v[i] - mean);

            // Clamp, then hand the clamped-off amount to the values that can still move, until stable.
            for (int iteration = 0; iteration < 100; iteration++)
            {
                double sum = 0;
                for (int i = 0; i < count; i++)
                {
                    x[i] = Math.Clamp(x[i], 0.0, 1.0);
                    sum += x[i];
                }
                double deficit = target * count - sum;
                if (Math.Abs(deficit) < 1e-9) break;
                int free = 0;
                for (int i = 0; i < count; i++)
                    if (deficit > 0 ? x[i] < 1 : x[i] > 0) free++;
                if (free == 0) break;
                for (int i = 0; i < count; i++)
                    if (deficit > 0 ? x[i] < 1 : x[i] > 0) x[i] += deficit / free;
            }

            for (int i = 0; i < count; i++) values[i] = (float)x[i];
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
            return values;
        }
    }
}
