using UnityEngine;

namespace Arena.Projectile
{
    public enum EffectKind
    {
        Damage,
        Spawn,
        Debuff,
        Vfx,
    }

    /// <summary>
    /// A world side effect the pure core wants performed, expressed as data. The core appends these
    /// to a reused buffer; the imperative shell (PawnProjectile) reads them, applies authority/replay
    /// gating via <see cref="EffectGate"/>, and executes them through its concrete services. The core
    /// never performs an effect itself — that's what keeps it pure and deterministic.
    ///
    /// Despawn is NOT an effect: the shell despawns when <see cref="ProjectileState.Alive"/> is false.
    /// </summary>
    public struct ProjectileEffect
    {
        public EffectKind Kind;

        public int TargetPlayerId;    // Damage / Debuff target
        public int AttackerPlayerId;  // Damage attacker; doubles as spawn OwnerId for Spawn
        public int Amount;            // Damage amount
        public int Count;             // Spawn count

        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Dir;

        public ProjectileType Type;   // Spawn / Vfx
        public DebuffType Debuff;     // Debuff

        public static ProjectileEffect Damage(int target, int amount, int attacker, Vector3 point, Vector3 dir) => new()
        {
            Kind = EffectKind.Damage,
            TargetPlayerId = target,
            AttackerPlayerId = attacker,
            Amount = amount,
            Point = point,
            Dir = dir,
        };

        public static ProjectileEffect Spawn(ProjectileType type, int count, Vector3 point, Vector3 dir, int ownerId) => new()
        {
            Kind = EffectKind.Spawn,
            Type = type,
            Count = count,
            Point = point,
            Dir = dir,
            AttackerPlayerId = ownerId,
        };

        public static ProjectileEffect Debuff_(int target, DebuffType debuff) => new()
        {
            Kind = EffectKind.Debuff,
            TargetPlayerId = target,
            Debuff = debuff,
        };

        public static ProjectileEffect Vfx(ProjectileType type, Vector3 point, Vector3 normal) => new()
        {
            Kind = EffectKind.Vfx,
            Type = type,
            Point = point,
            Normal = normal,
        };
    }
}
