using UnityEngine;

/// <summary>Tighter random spread on held weapons. Applied on every peer; only the owner fires.</summary>
[CreateAssetMenu(menuName = "Arena/Upgrades/Spread")]
public sealed class SpreadUpgrade : Upgrade
{
    [Tooltip("Multiplies random spread per stack: 0.7 is -30%.")]
    [SerializeField] private float multiplier = 0.7f;

    public override void ApplyTo(Weapon weapon) => weapon.spreadMultiplier *= multiplier;
}
