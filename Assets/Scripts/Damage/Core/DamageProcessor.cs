using UnityEngine;

namespace Arena.Damage
{
    /// <summary>
    /// The pure functional core of the Damage system: resolve a base damage amount into the final amount to
    /// apply, after any multiplicative modifier. Today it is a thin seam (one multiplier); it is the hook
    /// the modifier / level-up system grows into — bonus-damage passives, crits, resistances — without the
    /// Damage component or the hit effects having to know the arithmetic. EditMode-testable.
    /// </summary>
    public static class DamageProcessor
    {
        /// <summary>Final damage = base, never negative.</summary>
        public static int Resolve(int baseAmount) => Mathf.Max(0, baseAmount);

        /// <summary>Final damage = round(base × multiplier), never negative.</summary>
        public static int Resolve(int baseAmount, float multiplier) =>
            Mathf.Max(0, Mathf.RoundToInt(baseAmount * multiplier));
    }
}
