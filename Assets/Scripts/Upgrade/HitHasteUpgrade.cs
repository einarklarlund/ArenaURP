using UnityEngine;

/// <summary>
/// Hitting another pawn hastes the shooter's held weapon for a while. Pulled by each projectile on every peer;
/// only the shooter's copy acts on a hit.
/// </summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Hit Haste")]
public sealed class HitHasteUpgrade : Upgrade
{
    [Tooltip("Seconds of haste from the first stack.")]
    [SerializeField] private float firstDuration = 2f;
    [Tooltip("Seconds each further stack adds.")]
    [SerializeField] private float extraDuration = 1f;

    public override void ApplyTo(Projectile projectile)
    {
        projectile.hasteOnHitDuration += projectile.hasteOnHitDuration > 0f ? extraDuration : firstDuration;
    }
}
