using System.Collections.Generic;
using UnityEngine;

namespace Arena.Projectile
{
    /// <summary>
    /// Shared, immutable-at-runtime per-kind projectile data (Type Object + Flyweight).
    /// One asset per projectile kind (Linear bullet, Devil Daggers, laser, arc, homing).
    /// Replaces the old per-prefab <c>ProjectileData</c> component + prefab variants.
    ///
    /// Reaction lists use <c>[SerializeReference]</c> so concrete <see cref="IHitReaction"/>
    /// implementations are authored polymorphically in the inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Projectiles/Type", fileName = "ProjectileType")]
    public sealed class ProjectileType : ScriptableObject
    {
        [Header("Collision / Health")]
        public float Radius = 0.05f;
        public int MaxHealth = 1;

        [Header("Damage")]
        public int Damage = 3;

        [Header("Kinematics")]
        public float Speed = 50f;
        public float Acceleration = 0f;
        public float Lifetime = 5f;

        [Header("Movement strategy")]
        public MovementKind Movement = MovementKind.Linear;

        [Tooltip("Vertical gravity applied per second by ArcMovement (negative = down).")]
        public float Gravity = -9.81f;

        [Tooltip("Maximum beam length probed by LaserMovement each step.")]
        public float MaxRange = 100f;

        [Header("Reactions")]
        [SerializeReference] public List<IHitReaction> OnSurface = new();
        [SerializeReference] public List<IHitReaction> OnEnemy = new();
        [SerializeReference] public List<IHitReaction> OnProjectile = new();
    }
}
