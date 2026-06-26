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

        public static HealthState Full(int maxHp) => new() { Hp = maxHp, MaxHp = maxHp };
    }
}
