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
    /// A world side effect the pure core wants performed, expressed as data. This is the SHARED effect
    /// vocabulary: both projectile reactions (<see cref="IHitReaction"/>) and player abilities
    /// (<c>IAbilityEffect</c>) append these to a reused buffer, and the same imperative shell executes
    /// them through concrete services (<see cref="ProjectileWorldProvider.Execute"/>). The producer
    /// never performs an effect itself — that's what keeps the core pure and deterministic.
    ///
    /// (Named WorldEffect rather than ProjectileEffect because it is no longer projectile-specific.)
    /// Despawn is NOT an effect: the projectile shell despawns when <see cref="ProjectileState.Alive"/>
    /// is false.
    /// </summary>
    public struct WorldEffect
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

        public static WorldEffect Damage(int target, int amount, int attacker, Vector3 point, Vector3 dir) => new()
        {
            Kind = EffectKind.Damage,
            TargetPlayerId = target,
            AttackerPlayerId = attacker,
            Amount = amount,
            Point = point,
            Dir = dir,
        };

        public static WorldEffect Spawn(ProjectileType type, int count, Vector3 point, Vector3 dir, int ownerId) => new()
        {
            Kind = EffectKind.Spawn,
            Type = type,
            Count = count,
            Point = point,
            Dir = dir,
            AttackerPlayerId = ownerId,
        };

        public static WorldEffect Debuff_(int target, DebuffType debuff) => new()
        {
            Kind = EffectKind.Debuff,
            TargetPlayerId = target,
            Debuff = debuff,
        };

        public static WorldEffect Vfx(ProjectileType type, Vector3 point, Vector3 normal) => new()
        {
            Kind = EffectKind.Vfx,
            Type = type,
            Point = point,
            Normal = normal,
        };
    }
}
