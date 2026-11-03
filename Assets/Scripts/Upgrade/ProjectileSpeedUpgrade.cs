using UnityEngine;

/// <summary>Faster projectiles, and so longer range. Pulled by each projectile on every peer, so all of them agree.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Projectile Speed")]
public sealed class ProjectileSpeedUpgrade : Upgrade
{
    [Tooltip("Multiplies projectile speed per stack.")]
    [SerializeField] private float multiplier = 1.3f;

    public override void ApplyTo(Projectile projectile) => projectile.speedMultiplier *= multiplier;
}
