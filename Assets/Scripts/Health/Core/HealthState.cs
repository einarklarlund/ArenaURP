namespace Arena.Health
{
    /// <summary>
    /// Per-instance state of the generic Health system: current and maximum hit points. A value type with
    /// no behaviour; the pure <see cref="HealthProcessor"/> transforms it and the Health component holds it
    /// (its <c>Hp</c> as a SyncVar). Reusable by anything that can take damage — projectiles now, pawns and
    /// destructibles later.
    /// </summary>
    [System.Serializable]
    public struct HealthState
    {
        public int Hp;
        public int MaxHp;

        public readonly bool IsDead => Hp <= 0;

        /// <summary>Current HP as a 0..1 fraction of max — the single home for the "fraction = Hp/MaxHp"
        /// assumption, so consumers (bot brain, HUD) read it instead of each re-deriving the cast. Guards a
        /// zero (or negative) max: a degenerate max reports 0 rather than dividing by zero.</summary>
        public readonly float Fraction => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;

        public static HealthState Full(int maxHp) => new() { Hp = maxHp, MaxHp = maxHp };
    }
}
