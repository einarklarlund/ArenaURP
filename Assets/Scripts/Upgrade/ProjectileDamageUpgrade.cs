using UnityEngine;

/// <summary>More damage on every projectile's hit and explosion. Pulled by each projectile on every peer.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Projectile Damage")]
public sealed class ProjectileDamageUpgrade : Upgrade
{
    [SerializeField] private int amount = 1;

    public override void ApplyTo(Projectile projectile) => projectile.damageBonus += amount;
}
