using UnityEngine;

namespace Arena.Ability
{
    /// <summary>
    /// Pure inventory math for the active-ability bar: index cycling (with wrap), selection clamping
    /// after a slot is removed, and the per-use decrement that decides when a slot is exhausted. Kept
    /// engine-free and side-effect-free so the branchy bits (wrap-around, empty/size-1 edge cases,
    /// remove-at-zero) are covered by fast EditMode tests rather than PlayMode scenarios. The networked
    /// shell (PawnActiveAbilities) owns the actual inventory and just calls these.
    /// </summary>
    public static class AbilityInventoryLogic
    {
        /// <summary>Cycle <paramref name="current"/> by <paramref name="dir"/> (+1/-1) over a ring of
        /// <paramref name="count"/> slots, wrapping at both ends. Returns 0 when there are no slots.</summary>
        public static int CycleIndex(int current, int count, int dir)
        {
            if (count <= 0)
                return 0;
            int next = (current + dir) % count;
            if (next < 0)
                next += count;
            return next;
        }

        /// <summary>Clamp a selection index into a valid range after the slot count changed
        /// (e.g. an exhausted slot was removed). Returns 0 when empty.</summary>
        public static int ClampSelection(int selected, int count)
        {
            if (count <= 0)
                return 0;
            return Mathf.Clamp(selected, 0, count - 1);
        }

        /// <summary>Result of consuming one use of an active ability slot.</summary>
        public readonly struct UseResult
        {
            public readonly int UsesRemaining;
            public readonly bool Removed;

            public UseResult(int usesRemaining, bool removed)
            {
                UsesRemaining = usesRemaining;
                Removed = removed;
            }
        }

        /// <summary>Consume one use. The slot is removed when no uses remain.</summary>
        public static UseResult UseSlot(int usesRemaining)
        {
            int next = usesRemaining - 1;
            return new UseResult(next, next <= 0);
        }
    }
}
