using UnityEngine;

namespace Arena.Health
{
    /// <summary>
    /// The pure functional core of the Health system: clamp-correct damage and healing arithmetic plus the
    /// death predicate. No engine lifecycle, no networking — EditMode-testable. The Health component drives
    /// it and applies the result to its SyncVar.
    /// </summary>
    public static class HealthProcessor
    {
        /// <summary>Subtract <paramref name="amount"/>, clamped at zero (never negative).</summary>
        public static HealthState ApplyDamage(HealthState state, int amount)
        {
            if (amount > 0)
                state.Hp = Mathf.Max(0, state.Hp - amount);
            return state;
        }

        /// <summary>Add <paramref name="amount"/>, clamped at <see cref="HealthState.MaxHp"/>.</summary>
        public static HealthState Heal(HealthState state, int amount)
        {
            if (amount > 0)
                state.Hp = Mathf.Min(state.MaxHp, state.Hp + amount);
            return state;
        }
    }
}
